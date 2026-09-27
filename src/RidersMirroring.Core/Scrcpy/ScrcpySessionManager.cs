using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Owns the lifetime of every active <see cref="ScrcpySession"/> in
/// the application. One manager per process; the WPF UI binds to its
/// <see cref="Sessions"/> collection.
///
/// <para>
/// Adding a serial that's already mirrored is a no-op (the existing
/// session is returned). Removing a serial cancels and disposes the
/// session. Disposing the manager disposes every session.
/// </para>
/// </summary>
public sealed class ScrcpySessionManager : IAsyncDisposable, IDisposable
{
    private readonly IAdbServerManager _adb;
    private readonly IScrcpyVideoDecoderFactory _decoderFactory;
    private readonly Func<string, ScrcpySession> _sessionFactory;
    private readonly ILogger<ScrcpySessionManager> _logger;

    /// <summary>
    /// Active sessions, keyed by ADB serial. Read concurrently by the UI;
    /// mutated only inside the manager's start/stop methods (which the UI
    /// calls on the dispatcher).
    /// </summary>
    public ConcurrentDictionary<string, ScrcpySession> Sessions { get; } = new();

    /// <summary>
    /// Raised whenever a session is added to or removed from
    /// <see cref="Sessions"/>. Subscribers typically refresh their view.
    /// </summary>
    public event EventHandler<SessionsChangedEventArgs>? SessionsChanged;

    public ScrcpySessionManager(IAdbServerManager adb, IScrcpyVideoDecoderFactory decoderFactory)
        : this(adb, decoderFactory,
               serial => new ScrcpySession(adb, serial, decoderFactory),
               RidersLogger.Create<ScrcpySessionManager>())
    {
    }

    /// <summary>Test seam: lets unit tests inject a stub session factory.</summary>
    public ScrcpySessionManager(
        IAdbServerManager adb,
        IScrcpyVideoDecoderFactory decoderFactory,
        Func<string, ScrcpySession> sessionFactory)
        : this(adb, decoderFactory, sessionFactory, RidersLogger.Create<ScrcpySessionManager>())
    {
    }

    public ScrcpySessionManager(
        IAdbServerManager adb,
        IScrcpyVideoDecoderFactory decoderFactory,
        Func<string, ScrcpySession> sessionFactory,
        ILogger<ScrcpySessionManager> logger)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _decoderFactory = decoderFactory ?? throw new ArgumentNullException(nameof(decoderFactory));
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Start mirroring the device with the given serial. If a session
    /// already exists, returns the existing one without restarting it.
    /// </summary>
    public async Task<ScrcpySession> StartSessionAsync(
        string deviceSerial,
        CancellationToken cancellationToken = default)
    {
        // get-or-add without race: if a session already exists we return it
        // immediately. The factory closure runs only on the "create" path.
        var existing = Sessions.GetOrAdd(deviceSerial, _sessionFactory);
        if (existing.Status is ScrcpySessionStatus.Streaming
                          or ScrcpySessionStatus.AwaitingDevice)
        {
            return existing;
        }

        try
        {
            await existing.StartAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // StartAsync failed — remove the doomed session so the next
            // call to StartSessionAsync can try again from a clean slate.
            Sessions.TryRemove(new KeyValuePair<string, ScrcpySession>(deviceSerial, existing));
            await existing.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        SessionsChanged?.Invoke(this, new SessionsChangedEventArgs(
            SessionsChangedKind.Added, deviceSerial));
        _logger.LogInformation("Started scrcpy session for {Serial}", deviceSerial);
        return existing;
    }

    /// <summary>
    /// Stop and dispose the session for the given serial. No-op if
    /// the serial is not currently mirrored.
    /// </summary>
    public async Task StopSessionAsync(string deviceSerial)
    {
        if (!Sessions.TryRemove(deviceSerial, out var session))
        {
            return;
        }

        try
        {
            await session.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "error while disposing session for {Serial}", deviceSerial);
        }
        SessionsChanged?.Invoke(this, new SessionsChangedEventArgs(
            SessionsChangedKind.Removed, deviceSerial));
        _logger.LogInformation("Stopped scrcpy session for {Serial}", deviceSerial);
    }

    /// <summary>Number of currently-tracked sessions.</summary>
    public int Count => Sessions.Count;

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        // Snapshot keys to avoid mutating the dictionary while enumerating.
        var serials = Sessions.Keys.ToArray();
        foreach (var s in serials)
        {
            await StopSessionAsync(s).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Sync IDisposable bridge for WPF data-binding and `using var` usage
    /// in tests. Blockingly waits on the async teardown — fine because
    /// the only callers are tests or app shutdown.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}

/// <summary>Event payload for <see cref="ScrcpySessionManager.SessionsChanged"/>.</summary>
public sealed record SessionsChangedEventArgs(SessionsChangedKind Kind, string DeviceSerial);

/// <summary>What happened to a session in <see cref="ScrcpySessionManager"/>.</summary>
public enum SessionsChangedKind
{
    /// <summary>A new session was added.</summary>
    Added,
    /// <summary>An existing session was removed.</summary>
    Removed,
}
