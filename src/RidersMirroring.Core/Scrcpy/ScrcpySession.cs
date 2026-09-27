using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Status of a single <see cref="ScrcpySession"/>.
/// </summary>
public enum ScrcpySessionStatus
{
    /// <summary>The session has been instantiated but not yet started.</summary>
    Idle,
    /// <summary>The session is listening for the device-side connection.</summary>
    AwaitingDevice,
    /// <summary>The device has connected and frames are flowing.</summary>
    Streaming,
    /// <summary>The session has stopped cleanly (Dispose or Stop).</summary>
    Stopped,
    /// <summary>The session terminated because of an unrecoverable error.</summary>
    Faulted,
}

/// <summary>
/// Coordinates the lifetime of one <c>scrcpy</c> mirroring session for a
/// single Android device. A session owns:
/// <list type="bullet">
///   <item>One <see cref="ScrcpyServer"/> — pushes the jar, opens the
///         reverse tunnel, spawns the server process.</item>
///   <item>One <see cref="ScrcpyClient"/> — accepts the inbound device-side
///         connection on the PC side.</item>
///   <item>One <see cref="ScrcpyVideoStream"/> — reads framed NAL units
///         off the network and pushes them into the decoder.</item>
///   <item>One <see cref="IScrcpyVideoDecoder"/> — produces decoded
///         <see cref="DecodedVideoFrame"/> events.</item>
/// </list>
///
/// <para>
/// Sessions are designed to be created, started and disposed independently.
/// The owning <see cref="ScrcpySessionManager"/> keeps a dictionary keyed by
/// device serial — adding a second device never disturbs the first.
/// </para>
/// </summary>
public class ScrcpySession : IAsyncDisposable, IDisposable
{
    private readonly ILogger<ScrcpySession> _logger;
    private readonly IAdbServerManager _adb;
    private readonly IScrcpyVideoDecoderFactory _decoderFactory;

    private readonly ScrcpyServer _server;
    private readonly ScrcpyClient _client;
    private ScrcpyVideoStream? _videoStream;
    private IScrcpyVideoDecoder? _decoder;
    private IScrcpySession? _legacyServerSession; // returned by ScrcpyServer.StartAsync; owns push+tunnel teardown.
    private ScrcpySessionStatus _status = ScrcpySessionStatus.Idle;
    private readonly object _statusLock = new();

    /// <summary>ADB serial of the device this session is mirroring.</summary>
    public string DeviceSerial { get; }

    /// <summary>Effective video configuration — populated once the decoder is primed.</summary>
    public ScrcpyStreamHeader? Header => _decoder?.Header;

    /// <summary>Current status of the session.</summary>
    public ScrcpySessionStatus Status
    {
        get { lock (_statusLock) { return _status; } }
        private set { lock (_statusLock) { _status = value; } }
    }

    /// <summary>
    /// Raised for every decoded frame. Marshalling to the UI thread is the
    /// subscriber's responsibility (the Core layer does not depend on WPF).
    /// </summary>
    public event EventHandler<DecodedVideoFrame>? FrameDecoded;

    /// <summary>
    /// Raised when the session terminates because of an unrecoverable error
    /// (ADB failure, device unplug mid-stream, decoder crash, etc.).
    /// </summary>
    public event EventHandler<ScrcpySessionFaultedEventArgs>? SessionFaulted;

    /// <summary>
    /// Construct a session for the given ADB serial.
    /// </summary>
    /// <param name="adb">ADB manager used to push the jar and open tunnels.</param>
    /// <param name="deviceSerial">ADB serial (e.g. <c>J7AXB760E454HZF</c>).</param>
    /// <param name="decoderFactory">Factory used to construct a per-session decoder.</param>
    public ScrcpySession(
        IAdbServerManager adb,
        string deviceSerial,
        IScrcpyVideoDecoderFactory decoderFactory)
        : this(adb, deviceSerial, decoderFactory, RidersLogger.Create<ScrcpySession>())
    {
    }

    /// <summary>Test-friendly constructor.</summary>
    public ScrcpySession(
        IAdbServerManager adb,
        string deviceSerial,
        IScrcpyVideoDecoderFactory decoderFactory,
        ILogger<ScrcpySession> logger)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        DeviceSerial = deviceSerial ?? throw new ArgumentNullException(nameof(deviceSerial));
        _decoderFactory = decoderFactory ?? throw new ArgumentNullException(nameof(decoderFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _server = new ScrcpyServer(_adb) { UseRawStream = true };
        _client = new ScrcpyClient();
    }

    /// <summary>
    /// Start the session. Returns once the server has been spawned and we
    /// are listening for the inbound device-side connection.
    /// </summary>
    /// <remarks>
    /// Re-entrant calls throw <see cref="InvalidOperationException"/> —
    /// one session instance serves exactly one start/stop cycle.
    /// </remarks>
    public virtual async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (Status != ScrcpySessionStatus.Idle)
        {
            throw new InvalidOperationException(
                $"Cannot start session in status {Status}.");
        }

        Status = ScrcpySessionStatus.AwaitingDevice;
        try
        {
            // Two-step server bring-up: push jar + open tunnel, then spawn
            // the server process. We KEEP the returned IScrcpySession
            // because its Dispose tears down the tunnel + remote jar.
            _legacyServerSession = await _server.StartAsync(DeviceSerial, cancellationToken: cancellationToken).ConfigureAwait(false);
            await _server.ExecuteAppProcessAsync(DeviceSerial, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            Status = ScrcpySessionStatus.Faulted;
            SessionFaulted?.Invoke(this, new ScrcpySessionFaultedEventArgs(
                $"scrcpy-server start failed for {DeviceSerial}: {ex.Message}", ex));
            throw;
        }

        // Fire-and-forget: start the accept loop on the thread pool so
        // StartAsync can return immediately to the caller. Exceptions
        // raised inside AcceptAsync are routed to SessionFaulted.
        _ = Task.Run(async () =>
        {
            try
            {
                await _client.AcceptAsync(cancellationToken).ConfigureAwait(false);

                // Build the decoder + stream + wire events.
                _decoder = _decoderFactory.Create();
                _decoder.FrameDecoded += OnFrameDecoded;
                _decoder.Prime(new ScrcpyStreamHeader(
                    DeviceName: DeviceSerial,
                    CodecId: "h264",
                    Width: 1920,
                    Height: 1080));

                _videoStream = new ScrcpyVideoStream(_client.Stream, _decoder)
                {
                    UseRawStream = true,
                };
                _videoStream.StreamError += OnVideoStreamError;
                await _videoStream.StartAsync().ConfigureAwait(false);

                Status = ScrcpySessionStatus.Streaming;
            }
            catch (OperationCanceledException)
            {
                Status = ScrcpySessionStatus.Stopped;
            }
            catch (Exception ex)
            {
                Status = ScrcpySessionStatus.Faulted;
                _logger.LogError(ex, "scrcpy session crashed for {Serial}", DeviceSerial);
                SessionFaulted?.Invoke(this, new ScrcpySessionFaultedEventArgs(
                    $"Session for {DeviceSerial} crashed: {ex.Message}", ex));
            }
        }, cancellationToken);
    }

    private void OnFrameDecoded(object? sender, DecodedVideoFrame frame)
    {
        // Forward the event to subscribers unchanged. The sender is the
        // per-session decoder; we replace it with `this` so consumers can
        // identify which device produced the frame.
        FrameDecoded?.Invoke(this, frame);
    }

    private void OnVideoStreamError(object? sender, ScrcpyStreamErrorEventArgs e)
    {
        _logger.LogWarning(
            "scrcpy stream error for {Serial}: {Kind} — {Message}",
            DeviceSerial, e.Kind, e.Message);
        Status = ScrcpySessionStatus.Faulted;
        SessionFaulted?.Invoke(this, new ScrcpySessionFaultedEventArgs(
            $"Stream error ({e.Kind}) for {DeviceSerial}: {e.Message}", null));
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Tear down in reverse construction order: stream → decoder →
        // client → server. Each Dispose is best-effort and swallows its
        // own exceptions so one failed teardown never skips the next.
        if (_videoStream is not null)
        {
            try { await _videoStream.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "video stream dispose failed"); }
            _videoStream = null;
        }

        if (_decoder is not null)
        {
            try
            {
                _decoder.FrameDecoded -= OnFrameDecoded;
                await _decoder.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) { _logger.LogDebug(ex, "decoder dispose failed"); }
            _decoder = null;
        }

        try { await _client.DisposeAsync().ConfigureAwait(false); }
        catch (Exception ex) { _logger.LogDebug(ex, "client dispose failed"); }

        if (_legacyServerSession is not null)
        {
            try { await _legacyServerSession.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { _logger.LogDebug(ex, "legacy server session dispose failed"); }
            _legacyServerSession = null;
        }

        if (Status == ScrcpySessionStatus.Streaming || Status == ScrcpySessionStatus.AwaitingDevice)
        {
            Status = ScrcpySessionStatus.Stopped;
        }
    }

    /// <summary>
    /// Sync IDisposable bridge. Same trade-off as on
    /// <see cref="ScrcpySessionManager"/> — only called from tests or
    /// during process shutdown.
    /// </summary>
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}

/// <summary>Event payload for <see cref="ScrcpySession.SessionFaulted"/>.</summary>
public sealed record ScrcpySessionFaultedEventArgs(string Message, Exception? Exception);
