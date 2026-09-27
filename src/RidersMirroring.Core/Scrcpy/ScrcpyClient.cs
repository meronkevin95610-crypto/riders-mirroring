using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Listens on the local TCP port that <c>adb reverse</c> forwards to,
/// accepts the inbound scrcpy-server connection, and exposes the resulting
/// <see cref="NetworkStream"/> for the consumer to feed into
/// <see cref="ScrcpyVideoStream"/> or to read raw bytes from.
///
/// <para>
/// Direction reminder: <c>adb reverse tcp:27183 tcp:27183</c> causes any
/// device-side connection to <c>localhost:27183</c> to be tunnelled to
/// <c>localhost:27183</c> on the PC. The scrcpy server (running on the
/// device via <c>app_process</c>) opens that socket; on the PC we LISTEN,
/// not CONNECT.
/// </para>
/// </summary>
/// <remarks>
/// Used only by the v4.0 raw-stream pipeline. The legacy path goes through
/// <see cref="ScrcpyMirrorProcess"/> which spawns an external <c>scrcpy.exe</c>
/// subprocess and never touches this listener.
/// </remarks>
public sealed class ScrcpyClient : IAsyncDisposable
{
    /// <summary>Default tunnel port — matches the value used by <see cref="ScrcpyServer"/>.</summary>
    public const int DefaultPort = 27183;

    private readonly ILogger<ScrcpyClient> _logger;
    private readonly int _port;
    private readonly IPAddress _bindAddress;

    private TcpListener? _listener;
    private TcpClient? _accepted;
    private NetworkStream? _stream;
    private CancellationTokenSource? _acceptCts;

    // Audio + control are obtained by *connecting* to our own listening port
    // (scrcpy v4 tunnel_forward=false): the ADB daemon opens a fresh socket
    // to the device-side LocalServerSocket on every new TCP connection.
    private TcpClient? _audioClient;
    private NetworkStream? _audioStream;
    private TcpClient? _controlClient;
    private NetworkStream? _controlStream;

    /// <summary>True once the device-side scrcpy-server has connected to us.</summary>
    public bool IsConnected => _accepted is { Connected: true } && _stream is not null;

    /// <summary>Remote end-point of the accepted device-side connection (null until Accept).</summary>
    public IPEndPoint? RemoteEndPoint => _accepted?.Client?.RemoteEndPoint as IPEndPoint;

    /// <summary>Stream of bytes coming from the scrcpy-server (v4.0 raw mode).</summary>
    /// <exception cref="InvalidOperationException">Thrown if accessed before <see cref="AcceptAsync"/> completes.</exception>
    public NetworkStream Stream =>
        _stream ?? throw new InvalidOperationException(
            "ScrcpyClient has not been accepted yet — call AwaitAcceptAsync first.");

    /// <summary>Audio stream. Lazily opened on first access via <see cref="EnsureAudioStreamAsync"/>.</summary>
    public NetworkStream AudioStream =>
        _audioStream ?? throw new InvalidOperationException(
            "ScrcpyClient audio stream has not been opened yet — call EnsureAudioStreamAsync first.");

    /// <summary>Control stream. Lazily opened on first access via <see cref="EnsureControlStreamAsync"/>.</summary>
    public NetworkStream ControlStream =>
        _controlStream ?? throw new InvalidOperationException(
            "ScrcpyClient control stream has not been opened yet — call EnsureControlStreamAsync first.");

    public ScrcpyClient()
        : this(DefaultPort, IPAddress.Loopback, RidersLogger.Create<ScrcpyClient>())
    {
    }

    public ScrcpyClient(int port)
        : this(port, IPAddress.Loopback, RidersLogger.Create<ScrcpyClient>())
    {
    }

    public ScrcpyClient(int port, IPAddress bindAddress, ILogger<ScrcpyClient> logger)
    {
        if (port <= 0 || port > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port), port, "TCP port must be in (0, 65535].");
        }

        _port = port;
        _bindAddress = bindAddress ?? throw new ArgumentNullException(nameof(bindAddress));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Start listening on the local port. Returns immediately. Await
    /// <see cref="AwaitAcceptAsync"/> to wait for the inbound connection.
    /// </summary>
    public void StartListening()
    {
        if (_listener is not null)
        {
            throw new InvalidOperationException("ScrcpyClient is already listening.");
        }

        _listener = new TcpListener(_bindAddress, _port);
        _listener.Start();
        _logger.LogInformation(
            "ScrcpyClient listening on {Address}:{Port} — waiting for device-side scrcpy-server to connect.",
            _bindAddress, _port);
    }

    /// <summary>
    /// Block until the scrcpy-server connects to our listener (or the
    /// cancellation token fires).
    /// </summary>
    public async Task AcceptAsync(CancellationToken cancellationToken = default)
    {
        if (_listener is null)
        {
            throw new InvalidOperationException("Call StartListening() before AcceptAsync().");
        }

        _acceptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            _logger.LogInformation("Awaiting inbound scrcpy connection on port {Port}...", _port);
            _accepted = await _listener.AcceptTcpClientAsync(_acceptCts.Token).ConfigureAwait(false);
            _stream = _accepted.GetStream();
            var remote = _accepted.Client.RemoteEndPoint;
            _logger.LogInformation(
                "scrcpy-server connected from {Remote} — stream ready ({Bytes} bytes pre-buffered).",
                remote, _accepted.Available);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("ScrcpyClient.AcceptAsync was cancelled before a connection arrived.");
            throw;
        }
        catch (ObjectDisposedException)
        {
            _logger.LogWarning("ScrcpyClient listener was disposed before accepting a connection.");
            throw;
        }
    }

    /// <summary>
    /// Open the audio socket by connecting to our own listening port.
    /// scrcpy v4 (tunnel_forward=false) requires the client to open a fresh
    /// TCP connection for each secondary channel — the device-side
    /// <c>LocalServerSocket</c> accepts multiple consecutive connections.
    /// </summary>
    /// <remarks>
    /// Idempotent: calling twice returns the cached stream. The video
    /// connection (<see cref="AcceptAsync"/>) must have completed first.
    /// </remarks>
    public async Task<NetworkStream> EnsureAudioStreamAsync(CancellationToken cancellationToken = default)
    {
        if (_audioStream is not null)
        {
            return _audioStream;
        }

        _logger.LogInformation("Opening audio socket by connecting to localhost:{Port}...", _port);
        _audioClient = new TcpClient();
        await _audioClient.ConnectAsync(_bindAddress, _port).WaitAsync(cancellationToken).ConfigureAwait(false);
        _audioStream = _audioClient.GetStream();
        _logger.LogInformation(
            "Audio socket established from {Remote}.",
            _audioClient.Client.RemoteEndPoint);
        return _audioStream;
    }

    /// <summary>
    /// Open the control socket by connecting to our own listening port.
    /// See <see cref="EnsureAudioStreamAsync"/> for the rationale.
    /// </summary>
    public async Task<NetworkStream> EnsureControlStreamAsync(CancellationToken cancellationToken = default)
    {
        if (_controlStream is not null)
        {
            return _controlStream;
        }

        _logger.LogInformation("Opening control socket by connecting to localhost:{Port}...", _port);
        _controlClient = new TcpClient();
        await _controlClient.ConnectAsync(_bindAddress, _port).WaitAsync(cancellationToken).ConfigureAwait(false);
        _controlStream = _controlClient.GetStream();
        _logger.LogInformation(
            "Control socket established from {Remote}.",
            _controlClient.Client.RemoteEndPoint);
        return _controlStream;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try
        {
            _acceptCts?.Cancel();
        }
        catch
        {
            // swallow
        }

        if (_stream is not null)
        {
            try
            {
                _stream.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to dispose scrcpy network stream");
            }
            _stream = null;
        }

        if (_accepted is not null)
        {
            try
            {
                _accepted.Close();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to close scrcpy TcpClient");
            }
            _accepted = null;
        }

        if (_audioStream is not null)
        {
            try { _audioStream.Dispose(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to dispose audio stream"); }
            _audioStream = null;
        }
        if (_audioClient is not null)
        {
            try { _audioClient.Close(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to close audio TcpClient"); }
            _audioClient = null;
        }
        if (_controlStream is not null)
        {
            try { _controlStream.Dispose(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to dispose control stream"); }
            _controlStream = null;
        }
        if (_controlClient is not null)
        {
            try { _controlClient.Close(); }
            catch (Exception ex) { _logger.LogDebug(ex, "Failed to close control TcpClient"); }
            _controlClient = null;
        }

        if (_listener is not null)
        {
            try
            {
                _listener.Stop();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to stop scrcpy TcpListener");
            }
            _listener = null;
        }

        _acceptCts?.Dispose();
        _acceptCts = null;
    }
}
