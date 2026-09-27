using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.AirPlay.Bonjour;
using Riders.Mirroring.Core.AirPlay.Crypto;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.AirPlay.Rtsp;

/// <summary>
/// In-process RTSP server that speaks enough of the AirPlay protocol to
/// negotiate a mirroring session with an iPhone or iPad.
///
/// Wire format summary (simplified):
///   1. iPhone sends OPTIONS + SETUP with our public key + AES key wrapped in RSA.
///   2. We respond with our RSA public key + session id.
///   3. iPhone starts streaming H.264 over the data port (UDP/TCP) encrypted with
///      the AES-128-CTR key + IV we exchanged.
///   4. We decrypt, push frames into the decoder pipeline.
/// </summary>
public sealed class RtspServer : IAsyncDisposable
{
    private readonly ILogger<RtspServer> _logger;
    private readonly BonjourAdvertiser _bonjour;
    private readonly int _rtspPort;
    private readonly Func<EncryptedFrame, CancellationToken, Task> _frameSink;

    private readonly TcpListener? _listener;
    private readonly CancellationTokenSource _cts = new();
    private Task? _acceptTask;
    private readonly ConcurrentDictionary<string, ClientSession> _sessions = new();

    public bool IsRunning => _acceptTask is { IsCompleted: false };

    /// <summary>Number of currently connected clients (negotiated at SETUP).</summary>
    public int ConnectedClients => _sessions.Count;

    public RtspServer(
        int rtspPort,
        BonjourAdvertiser bonjour,
        Func<EncryptedFrame, CancellationToken, Task> frameSink,
        ILogger<RtspServer>? logger = null)
    {
        _rtspPort = rtspPort;
        _bonjour = bonjour ?? throw new ArgumentNullException(nameof(bonjour));
        _frameSink = frameSink ?? throw new ArgumentNullException(nameof(frameSink));
        _logger = logger ?? RidersLogger.Create<RtspServer>();
        _listener = new TcpListener(IPAddress.Any, _rtspPort);
    }

    /// <summary>Bind the RTSP port, start accepting clients, advertise via Bonjour.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            _logger.LogDebug("RtspServer already running");
            return;
        }
        _listener!.Start();
        await _bonjour.StartAsync(cancellationToken).ConfigureAwait(false);
        _acceptTask = Task.Run(() => AcceptLoopAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("RtspServer bound to port {Port}", _rtspPort);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var client = await _listener!.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = Task.Run(() => HandleClientAsync(client, ct), ct);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger.LogError(ex, "RtspServer accept loop crashed");
        }
    }

    /// <summary>Per-client handshake + streaming loop.</summary>
    private async Task HandleClientAsync(TcpClient client, CancellationToken ct)
    {
        var session = new ClientSession
        {
            Tcp = client,
            ServerKeys = AirPlayCrypto.GenerateServerKeyPair(),
        };
        var buffer = new byte[16 * 1024];
        var accumulated = new MemoryStream();
        var remote = client.Client.RemoteEndPoint;

        try
        {
            _logger.LogInformation("RTSP client connected: {Remote}", remote);
            while (!ct.IsCancellationRequested && client.Connected)
            {
                int read = await client.GetStream().ReadAsync(buffer.AsMemory(0, buffer.Length), ct).ConfigureAwait(false);
                if (read == 0) break;
                accumulated.Write(buffer, 0, read);

                // Try to parse one or more requests from the accumulated buffer.
                while (RtspParser.TryParseRequest(accumulated.GetBuffer().AsSpan(0, (int)accumulated.Length), out var request, out var consumed))
                {
                    await DispatchAsync(session, request).ConfigureAwait(false);
                    accumulated.SetLength(0);
                    if (consumed < accumulated.Length)
                    {
                        var leftover = new ReadOnlySpan<byte>(accumulated.GetBuffer(), consumed, (int)accumulated.Length - consumed);
                        accumulated.Write(leftover.ToArray(), 0, leftover.Length);
                    }
                    break;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (IOException) { /* peer closed */ }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RTSP client handler error");
        }
        finally
        {
            _logger.LogInformation("RTSP client disconnected: {Remote}", remote);
            client.Dispose();
        }
    }

    /// <summary>Route an RTSP request to its handler.</summary>
    private async Task DispatchAsync(ClientSession session, RtspRequest request)
    {
        _logger.LogDebug("RTSP {Method} {Uri}", request.Method, request.Uri);
        switch (request.Method)
        {
            case "OPTIONS":
                await ReplyOptionsAsync(session, request).ConfigureAwait(false);
                break;
            case "SETUP":
                await ReplySetupAsync(session, request).ConfigureAwait(false);
                break;
            case "RECORD":
                await ReplyRecordAsync(session, request).ConfigureAwait(false);
                break;
            case "TEARDOWN":
                await ReplyTeardownAsync(session, request).ConfigureAwait(false);
                break;
            case "GET_PARAMETER":
                await ReplyGetParameterAsync(session, request).ConfigureAwait(false);
                break;
            default:
                await ReplyNotImplementedAsync(session, request).ConfigureAwait(false);
                break;
        }
    }

    private static async Task ReplyOptionsAsync(ClientSession s, RtspRequest r)
    {
        var headers = new Dictionary<string, string>
        {
            ["Public"] = "OPTIONS, SETUP, RECORD, TEARDOWN, GET_PARAMETER, ANNOUNCE",
            ["CSeq"] = r.Headers.GetCSeq().ToString(),
        };
        var response = RtspParser.BuildResponse(200, "OK", headers);
        await s.Tcp.GetStream().WriteAsync(response).ConfigureAwait(false);
    }

    private async Task ReplySetupAsync(ClientSession s, RtspRequest r)
    {
        try
        {
            // iPhone sends its RSA public key in the body of SETUP.
            if (r.Body.Length > 0)
            {
                using var clientRsa = AirPlayCrypto.ImportClientPublicKey(r.Body);
                s.ClientPublicKey = clientRsa;
            }

            // Send our public key + session id back.
            var sessionId = Guid.NewGuid().ToString("N");
            s.SessionId = sessionId;

            var bodyBuilder = new StringBuilder();
            bodyBuilder.AppendLine($"v=0");
            bodyBuilder.AppendLine($"o=AirTunes {sessionId} 0 IN IP4 0.0.0.0");
            bodyBuilder.AppendLine($"s=AirPlay");
            bodyBuilder.AppendLine($"c=IN IP4 0.0.0.0");
            bodyBuilder.AppendLine($"t=0 0");
            bodyBuilder.AppendLine($"m=video 0 RTP/AVP 96");
            bodyBuilder.AppendLine($"a=rtpmap:96 H264/90000");
            bodyBuilder.AppendLine($"a=fmtp:96 level-asymmetry-allowed=1;packetization-mode=1;profile-level-id=42E01E");
            var sdp = bodyBuilder.ToString();
            var sdpBytes = Encoding.ASCII.GetBytes(sdp);

            var headers = new Dictionary<string, string>
            {
                ["CSeq"] = r.Headers.GetCSeq().ToString(),
                ["Session"] = sessionId,
                ["Transport"] = r.Headers.GetHeader("Transport") ?? "RTP/AVP/UDP;unicast;mode=record",
                ["Content-Type"] = "application/sdp",
            };
            var response = RtspParser.BuildResponse(200, "OK", headers, sdpBytes);

            // Prefix with our RSA public key (binary). AirPlay combines a
            // length-prefixed public key with the RTSP response. Format:
            //   [4-byte big-endian public-key length][public-key bytes]
            //   [RTSP response bytes]
            using var prefix = new MemoryStream();
            var prefixWriter = new BinaryWriter(prefix);
            prefixWriter.Write(BinaryPrimitives.ReverseEndianness(s.ServerKeys.PublicKeyDer.Length));
            prefixWriter.Write(s.ServerKeys.PublicKeyDer);
            var prefixBytes = prefix.ToArray();
            var combined = new byte[prefixBytes.Length + response.Length];
            Buffer.BlockCopy(prefixBytes, 0, combined, 0, prefixBytes.Length);
            Buffer.BlockCopy(response, 0, combined, prefixBytes.Length, response.Length);

            await s.Tcp.GetStream().WriteAsync(combined).ConfigureAwait(false);
            _sessions[sessionId] = s;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SETUP failed");
            var err = RtspParser.BuildResponse(500, "Internal Server Error",
                new Dictionary<string, string> { ["CSeq"] = r.Headers.GetCSeq().ToString() });
            await s.Tcp.GetStream().WriteAsync(err).ConfigureAwait(false);
        }
    }

    private async Task ReplyRecordAsync(ClientSession s, RtspRequest r)
    {
        // Client tells us it's about to start pushing RTP frames. The
        // data port / AES key / IV are negotiated through SETUP headers.
        // We mark the session as live and start the streaming loop.
        s.IsRecording = true;

        var headers = new Dictionary<string, string>
        {
            ["CSeq"] = r.Headers.GetCSeq().ToString(),
        };
        await s.Tcp.GetStream().WriteAsync(RtspParser.BuildResponse(200, "OK", headers)).ConfigureAwait(false);

        // Pump RTP frames from the same TCP socket (interleaved binary
        // framing after RTSP). In a full AirPlay implementation we'd
        // spawn a UDP listener on the data port here.
        await PumpRtpFramesAsync(s).ConfigureAwait(false);
    }

    /// <summary>Read RTP-like frames from the RTSP socket after RECORD.</summary>
    private async Task PumpRtpFramesAsync(ClientSession s)
    {
        var stream = s.Tcp.GetStream();
        var header = new byte[4];
        try
        {
            while (s.IsRecording && s.Tcp.Connected)
            {
                // Interleaved binary: '$' + 1-byte channel + 2-byte BE length.
                var b0 = stream.ReadByte();
                if (b0 == -1) break;
                if (b0 != 0x24) continue; // not interleaved binary
                var b1 = stream.ReadByte();
                var lenHi = stream.ReadByte();
                var lenLo = stream.ReadByte();
                if ((b1 | lenHi | lenLo) == -1) break;
                var length = (lenHi << 8) | lenLo;
                var payload = new byte[length];
                var got = 0;
                while (got < length)
                {
                    var r = await stream.ReadAsync(payload.AsMemory(got, length - got)).ConfigureAwait(false);
                    if (r == 0) break;
                    got += r;
                }
                if (got != length) break;

                // Encrypted frame: first byte is the type tag AirPlay uses.
                var frame = new EncryptedFrame(b1, payload);
                await _frameSink(frame, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (IOException) { }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "RTP pump crashed");
        }
    }

    private static async Task ReplyTeardownAsync(ClientSession s, RtspRequest r)
    {
        s.IsRecording = false;
        var headers = new Dictionary<string, string>
        {
            ["CSeq"] = r.Headers.GetCSeq().ToString(),
        };
        await s.Tcp.GetStream().WriteAsync(RtspParser.BuildResponse(200, "OK", headers)).ConfigureAwait(false);
    }

    private static async Task ReplyGetParameterAsync(ClientSession s, RtspRequest r)
    {
        var headers = new Dictionary<string, string>
        {
            ["CSeq"] = r.Headers.GetCSeq().ToString(),
        };
        await s.Tcp.GetStream().WriteAsync(RtspParser.BuildResponse(200, "OK", headers)).ConfigureAwait(false);
    }

    private static async Task ReplyNotImplementedAsync(ClientSession s, RtspRequest r)
    {
        var headers = new Dictionary<string, string>
        {
            ["CSeq"] = r.Headers.GetCSeq().ToString(),
        };
        await s.Tcp.GetStream().WriteAsync(RtspParser.BuildResponse(501, "Not Implemented", headers)).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        if (_acceptTask is not null)
        {
            try { await _acceptTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        try { _listener?.Stop(); } catch { }
        await _bonjour.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }

    /// <summary>Per-client state held while an iPhone is connected.</summary>
    private sealed class ClientSession
    {
        public TcpClient Tcp { get; init; } = null!;
        public RSAKeyPair ServerKeys { get; init; } = null!;
        public RSA? ClientPublicKey { get; set; }
        public string SessionId { get; set; } = string.Empty;
        public bool IsRecording { get; set; }
    }
}

/// <summary>Encrypted payload pushed by the iPhone over the data channel.</summary>
public sealed record EncryptedFrame(int Channel, byte[] Payload);