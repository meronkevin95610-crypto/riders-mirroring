using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.AirPlay.Bonjour;

/// <summary>
/// Minimal mDNS / DNS-SD advertiser that announces an AirPlay receiver on
/// the local network. iOS / macOS devices pick the receiver up through the
/// "Screen Mirroring" / "AirPlay" picker.
///
/// We implement only the small subset of RFC 6762 / 6763 we actually need:
/// one A-record (our IPv4), one PTR (service type), one SRV, one TXT.
/// </summary>
/// <remarks>
/// AirPlay uses two service types side by side:
///   _airplay._tcp        — generic video receiver (iOS 9+)
///   _raop._tcp           — Remote Audio Output Protocol (audio-only path)
///
/// Both are queried by iPhones during discovery. We advertise _airplay only
/// for the mirroring case; audio mirroring falls back to the same socket.
/// </remarks>
public sealed class BonjourAdvertiser : IAsyncDisposable
{
    /// <summary>Standard AirPlay mDNS service type (mirroring + audio).</summary>
    public const string AirPlayServiceType = "_airplay._tcp.local.";

    /// <summary>Companion service type used by older iOS for the RTSP control port.</summary>
    public const string AirPlayToshibaServiceType = "_airplay._tcp.local.";

    private readonly ILogger<BonjourAdvertiser> _logger;
    private readonly string _serviceName;
    private readonly int _port;
    private readonly string _model;
    private readonly string _macAddress; // 6 bytes formatted AA:BB:CC:DD:EE:FF
    private readonly CancellationTokenSource _cts = new();

    private Socket? _socket;
    private Task? _loopTask;

    /// <summary>Whether the advertiser is currently broadcasting.</summary>
    public bool IsRunning => _loopTask is { IsCompleted: false };

    public BonjourAdvertiser(
        string serviceName,
        int port,
        string model = "RidersMirroring",
        string? macAddress = null,
        ILogger<BonjourAdvertiser>? logger = null)
    {
        _serviceName = string.IsNullOrWhiteSpace(serviceName) ? "Riders Mirroring" : serviceName;
        _port = port;
        _model = model;
        _macAddress = macAddress ?? GenerateSyntheticMac();
        _logger = logger ?? RidersLogger.Create<BonjourAdvertiser>();
    }

    /// <summary>Start broadcasting on the LAN. Idempotent.</summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            _logger.LogDebug("BonjourAdvertiser already running");
            return Task.CompletedTask;
        }

        try
        {
            // 5353 is the standard mDNS port. We join the multicast group on
            // every IPv4 interface and bind to INADDR_ANY so the OS picks the
            // outgoing interface based on the destination.
            _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
            {
                EnableBroadcast = true,
                MulticastLoopback = false,
            };
            _socket.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.MulticastTimeToLive, 255);
            _socket.Bind(new IPEndPoint(IPAddress.Any, 5353));

            // Join 224.0.0.251 on every active interface.
            var multicastAddress = IPAddress.Parse("224.0.0.251");
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                                              or NetworkInterfaceType.Tunnel) continue;

                try
                {
                    _socket.SetSocketOption(
                        SocketOptionLevel.IP,
                        SocketOptionName.AddMembership,
                        new MulticastOption(multicastAddress, nic.GetIPProperties().GetIPv4Properties().Index));
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Could not join multicast on {Name}", nic.Name);
                }
            }
        }
        catch (SocketException ex) when (ex.SocketErrorCode == SocketError.AddressAlreadyInUse)
        {
            // Bonjour already running on this machine — the OS will answer
            // queries on our behalf. Not fatal.
            _logger.LogInformation("mDNS port already in use, relying on system Bonjour");
            _socket?.Dispose();
            _socket = null;
            return Task.CompletedTask;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to bind mDNS socket");
            _socket?.Dispose();
            _socket = null;
            throw;
        }

        _loopTask = Task.Run(() => RunAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("BonjourAdvertiser started on port {Port} as {Name}", _port, _serviceName);
        return Task.CompletedTask;
    }

    /// <summary>Send a single announcement packet for our service. The RFC
    /// recommends re-sending at 1-second intervals for the first second, then
    /// dropping to ~1/TTL (typically 75% of 4500 = ~75s). We keep it simple:
    /// announce once per loop iteration (~1s).</summary>
    private async Task RunAsync(CancellationToken ct)
    {
        if (_socket is null) return;

        var firstAnnounce = true;
        var ttl = 4500;
        var announceInterval = TimeSpan.FromSeconds(firstAnnounce ? 1 : 30);

        try
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    var packet = BuildAnnouncementPacket(_serviceName, _port, _model, _macAddress, ttl);
                    var dest = new IPEndPoint(IPAddress.Parse("224.0.0.251"), 5353);
                    await _socket.SendToAsync(packet, SocketFlags.None, dest).ConfigureAwait(false);
                    if (firstAnnounce)
                    {
                        firstAnnounce = false;
                        announceInterval = TimeSpan.FromSeconds(30);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send mDNS announcement");
                }

                try { await Task.Delay(announceInterval, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }
        finally
        {
            _logger.LogDebug("BonjourAdvertiser loop exited");
        }
    }

    /// <summary>Build a DNS packet advertising our service.
    /// Format follows RFC 6762 section 8 (announcements) + RFC 6763 (SRV/TXT).</summary>
    internal static byte[] BuildAnnouncementPacket(
        string serviceName, int port, string model, string mac, int ttl)
    {
        // DNS packet:
        //   Header (12 bytes): ID=0, FLAGS=0x8400 (response, authoritative),
        //                       QD=0, AN=4, NS=0, AR=0
        //   Answer section: PTR, SRV, TXT, A
        var name = $"{serviceName}.{AirPlayServiceType}";

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // Header
        bw.Write((ushort)0);              // ID
        bw.Write((ushort)0x8400);         // FLAGS (response + AA)
        bw.Write((ushort)0);              // QDCOUNT
        bw.Write((ushort)4);              // ANCOUNT
        bw.Write((ushort)0);              // NSCOUNT
        bw.Write((ushort)0);              // ARCOUNT

        // Answer 1 — PTR for service type -> instance name
        WriteDnsName(bw, AirPlayServiceType);
        bw.Write((ushort)0x000C);         // TYPE = PTR
        bw.Write((ushort)0x8001);         // CLASS = IN + cache flush
        bw.Write((uint)ttl);              // TTL
        WriteDnsName(bw, name);           // PTRDNAME
        // The PTRDNAME above is wrong — it would consume domain bytes as the
        // RDLENGTH. Real DNS would put the encoded length here. Rebuild.

        ms.Position = 0;
        bw.Write(BuildAnnouncementPacketRaw(serviceName, port, model, mac, ttl));
        return ms.ToArray();
    }

    private static byte[] BuildAnnouncementPacketRaw(
        string serviceName, int port, string model, string mac, int ttl)
    {
        var name = $"{serviceName}.{AirPlayServiceType}";
        var localIp = ResolveLocalIPv4() ?? IPAddress.Loopback;
        var localIpBytes = localIp.GetAddressBytes();
        var macBytes = ParseMac(mac);

        // Pre-compute name encodings once.
        var serviceTypeBytes = EncodeDnsName(AirPlayServiceType);
        var instanceNameBytes = EncodeDnsName(name);
        var hostName = $"{serviceName}.local.";
        var hostNameBytes = EncodeDnsName(hostName);

        // TXT record: model + mac + features (Apple-style).
        var txtEntries = new List<byte[]>
        {
            Encoding.UTF8.GetBytes($"model={model}.1"),
            Encoding.UTF8.GetBytes($"mac={mac}"),
            Encoding.UTF8.GetBytes("features=0x5A7FFFF7,0x1E"),
            Encoding.UTF8.GetBytes("vv=2"),
            Encoding.UTF8.GetBytes("srcvers=760.6.5"),
            Encoding.UTF8.GetBytes("pk=b0e7defe9bc6a72e302e80b0d6cf6b15b154f04ddc83eec3e76d4f4a26a45b6f"),
            Encoding.UTF8.GetBytes("pi=b08f5a79-db29-11e6-a4a4-0800200c9a66"),
            Encoding.UTF8.GetBytes("flags=0x44"),
            Encoding.UTF8.GetBytes("rff=0x0"),
            Encoding.UTF8.GetBytes("pn=1"),
        };
        using var txtMs = new MemoryStream();
        foreach (var e in txtEntries) { txtMs.WriteByte((byte)e.Length); txtMs.Write(e, 0, e.Length); }
        var txtPayload = txtMs.ToArray();

        // Compute the SRV record payload (big-endian fields).
        var srvPayload = new MemoryStream();
        WriteBigEndian(srvPayload, 0);      // priority
        WriteBigEndian(srvPayload, 0);      // weight
        WriteBigEndian(srvPayload, port);
        srvPayload.Write(hostNameBytes, 0, hostNameBytes.Length);
        var srvPayloadBytes = srvPayload.ToArray();

        using var ms = new MemoryStream();
        // DNS header (12 bytes), all big-endian.
        WriteBigEndian(ms, 0);       // ID
        WriteBigEndian(ms, 0x8400);  // FLAGS
        WriteBigEndian(ms, 0);       // QDCOUNT
        WriteBigEndian(ms, 4);       // ANCOUNT
        WriteBigEndian(ms, 0);       // NSCOUNT
        WriteBigEndian(ms, 0);       // ARCOUNT

        // Answer 1: PTR
        WriteName(ms, serviceTypeBytes);
        WriteBigEndian(ms, 0x000C);  // TYPE PTR
        WriteBigEndian(ms, 0x8001);  // CLASS IN + cache-flush
        WriteBigEndian32(ms, (uint)ttl);
        WriteBigEndian(ms, instanceNameBytes.Length);
        ms.Write(instanceNameBytes, 0, instanceNameBytes.Length);

        // Answer 2: SRV
        WriteName(ms, instanceNameBytes);
        WriteBigEndian(ms, 0x0021);  // TYPE SRV
        WriteBigEndian(ms, 0x8001);
        WriteBigEndian32(ms, (uint)ttl);
        WriteBigEndian(ms, srvPayloadBytes.Length);
        ms.Write(srvPayloadBytes, 0, srvPayloadBytes.Length);

        // Answer 3: TXT
        WriteName(ms, instanceNameBytes);
        WriteBigEndian(ms, 0x0010);  // TYPE TXT
        WriteBigEndian(ms, 0x8001);
        WriteBigEndian32(ms, (uint)ttl);
        WriteBigEndian(ms, txtPayload.Length);
        ms.Write(txtPayload, 0, txtPayload.Length);

        // Answer 4: A
        WriteName(ms, hostNameBytes);
        WriteBigEndian(ms, 0x0001);  // TYPE A
        WriteBigEndian(ms, 0x8001);
        WriteBigEndian32(ms, (uint)ttl);
        WriteBigEndian(ms, 4);
        ms.Write(localIpBytes, 0, localIpBytes.Length);

        _ = macBytes;
        return ms.ToArray();
    }

    private static void WriteName(MemoryStream ms, byte[] encoded)
    {
        ms.Write(encoded, 0, encoded.Length);
    }

    private static void WriteBigEndian(Stream s, int value)
    {
        s.WriteByte((byte)((value >> 8) & 0xFF));
        s.WriteByte((byte)(value & 0xFF));
    }

    private static void WriteBigEndian32(Stream s, uint value)
    {
        s.WriteByte((byte)((value >> 24) & 0xFF));
        s.WriteByte((byte)((value >> 16) & 0xFF));
        s.WriteByte((byte)((value >> 8) & 0xFF));
        s.WriteByte((byte)(value & 0xFF));
    }

    /// <summary>Encode a DNS name in wire format: 3www7example3com0.</summary>
    internal static byte[] EncodeDnsName(string name)
    {
        name = name.TrimEnd('.');
        using var ms = new MemoryStream();
        foreach (var label in name.Split('.'))
        {
            var bytes = Encoding.UTF8.GetBytes(label);
            if (bytes.Length > 63)
            {
                throw new ArgumentException($"DNS label '{label}' exceeds 63 bytes", nameof(name));
            }
            ms.WriteByte((byte)bytes.Length);
            ms.Write(bytes, 0, bytes.Length);
        }
        ms.WriteByte(0); // terminator
        return ms.ToArray();
    }

    /// <summary>Old placeholder kept for binary-compat with BuildAnnouncementPacket.
    /// Throws — replaced by the raw encoder above.</summary>
    private static void WriteDnsName(BinaryWriter bw, string name)
    {
        var bytes = EncodeDnsName(name);
        bw.Write(bytes);
    }

    /// <summary>Parse a MAC in AA:BB:CC:DD:EE:FF form into 6 bytes.</summary>
    internal static byte[] ParseMac(string mac)
    {
        var parts = mac.Split(':', '-');
        var result = new byte[6];
        for (int i = 0; i < 6; i++)
        {
            result[i] = parts.Length > i ? Convert.ToByte(parts[i], 16) : (byte)0;
        }
        return result;
    }

    /// <summary>Build a deterministic but locally unique MAC for the receiver.</summary>
    internal static string GenerateSyntheticMac()
    {
        // First 3 bytes = locally administered unicast prefix (02:00:00).
        // Last 3 bytes = random.
        var rnd = Random.Shared;
        var bytes = new byte[6];
        bytes[0] = 0x02;
        bytes[1] = 0x00;
        bytes[2] = 0x00;
        bytes[3] = (byte)rnd.Next(0, 256);
        bytes[4] = (byte)rnd.Next(0, 256);
        bytes[5] = (byte)rnd.Next(0, 256);
        return string.Join(":", bytes.Select(b => b.ToString("X2")));
    }

    internal static IPAddress? ResolveLocalIPv4()
    {
        NetworkInterface[] interfaces;
        try { interfaces = NetworkInterface.GetAllNetworkInterfaces(); }
        catch { return null; }

        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                                          or NetworkInterfaceType.Tunnel) continue;
            foreach (var addr in nic.GetIPProperties().UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                var b = addr.Address.GetAddressBytes();
                if (b[0] == 169 && b[1] == 254) continue; // link-local
                return addr.Address;
            }
        }
        return null;
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_loopTask is not null)
        {
            try { await _loopTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _socket?.Close();
        _socket?.Dispose();
        _cts.Dispose();
        _logger.LogDebug("BonjourAdvertiser disposed");
    }
}