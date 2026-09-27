using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.Wireless;

/// <summary>
/// Computes the SSID/password/IP/port triple the user needs to scan from
/// their Android device, and pairs it with the ADB server to make sure
/// port <c>5555</c> is open for incoming wireless connections.
/// </summary>
/// <remarks>
/// This service is *stateless* — it doesn't actually own the Wi-Fi
/// connection (that's the OS's job). It just resolves the current network
/// and assembles a <see cref="WirelessPairingInfo"/> ready to render.
/// </remarks>
public sealed class WirelessHostService
{
    /// <summary>Default TCP port ADB listens on for wireless pairing.</summary>
    public const int DefaultAdbPort = 5555;

    private readonly ILogger<WirelessHostService> _logger;
    private readonly IAdbServerManager _adb;

    public WirelessHostService(IAdbServerManager adb)
        : this(adb, RidersLogger.Create<WirelessHostService>())
    {
    }

    public WirelessHostService(IAdbServerManager adb, ILogger<WirelessHostService> logger)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Resolve the local Wi-Fi (or wired) adapter and return pairing info
    /// pointing at it. Throws <see cref="InvalidOperationException"/> if
    /// the host doesn't seem to be on any usable network.
    /// </summary>
    public async Task<WirelessPairingInfo> ResolveAsync(
        string ssid,
        string password,
        int adbPort = DefaultAdbPort,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ssid))
        {
            throw new ArgumentException("SSID is required.", nameof(ssid));
        }

        if (string.IsNullOrEmpty(password))
        {
            // WPA networks require a password. Open networks (rare) could
            // be supported later by changing the scheme to "WIFI:T:nopass;".
            throw new ArgumentException("Password is required for WPA networks.", nameof(password));
        }

        var ip = ResolveLocalIPv4();
        if (ip is null)
        {
            throw new InvalidOperationException(
                "No active IPv4 network adapter found. Connect to Wi-Fi or plug in Ethernet first.");
        }

        // Make sure the ADB server is up and listening on the wireless port.
        var adbUp = await _adb.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        if (!adbUp)
        {
            _logger.LogWarning("ADB server not reachable — pairing QR generated anyway");
        }

        var info = new WirelessPairingInfo(
            Ssid: ssid,
            Password: password,
            HostEndpoint: $"{ip}:{adbPort}");

        // SSIDs can leak the user's location (e.g. "CafeDuCoin_5GHz",
        // "iPhone de Bob") — log only the last 4 chars so support can
        // still correlate logs without exposing the full identifier.
        _logger.LogInformation(
            "Wireless pairing ready: SSID=***{SsidTail} endpoint={Endpoint}",
            Tail(ssid), info.HostEndpoint);

        return info;
    }

    /// <summary>Return the last 4 characters of <paramref name="value"/>,
    /// or <c>"****"</c> if the value is shorter than 4 chars.
    /// Public so VMs can show the same masked SSID in their own log
    /// messages without reaching into reflection.</summary>
    public static string Tail(string value)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= 4)
        {
            return "****";
        }
        return value[^4..];
    }

    /// <summary>
    /// Best-effort lookup of the host's primary outbound IPv4 address.
    /// Skips loopback, tunnel interfaces, and link-local addresses.
    /// </summary>
    /// <remarks>
    /// O(n_adapters × m_unicast) — &lt; 100 iterations in typical configurations.
    /// Returns <c>null</c> if no usable IPv4 adapter is present.
    /// </remarks>
    public static IPAddress? ResolveLocalIPv4()
    {
        NetworkInterface[] interfaces;
        try
        {
            interfaces = NetworkInterface.GetAllNetworkInterfaces();
        }
        catch (Exception)
        {
            // Some locked-down environments (sandboxed CI) refuse to enumerate.
            return null;
        }

        foreach (var nic in interfaces)
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
            {
                continue;
            }

            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback
                                       or NetworkInterfaceType.Tunnel)
            {
                continue;
            }

            var props = nic.GetIPProperties();
            foreach (var addr in props.UnicastAddresses)
            {
                if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                {
                    continue;
                }

                var bytes = addr.Address.GetAddressBytes();
                if (bytes[0] == 169 && bytes[1] == 254)
                {
                    // 169.254.x.x — link-local, skip.
                    continue;
                }

                return addr.Address;
            }
        }

        return null;
    }
}