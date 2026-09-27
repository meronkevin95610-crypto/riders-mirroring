namespace Riders.Mirroring.Core.AirPlay;

/// <summary>
/// Abstraction over the local AirPlay receiver so the rest of the app
/// doesn't have to know whether we're shelling out to UxPlay, running an
/// in-process RAOP server, or using a mock in tests.
/// </summary>
public interface IAirPlayReceiver : IAsyncDisposable
{
    /// <summary>True if the underlying process is alive and listening.</summary>
    bool IsRunning { get; }

    /// <summary>Last stdout / stderr line the receiver produced, for the UI.</summary>
    string LastStatusLine { get; }

    /// <summary>Snapshot of currently connected iPhone / iPad sessions.</summary>
    IReadOnlyList<AirPlaySession> ActiveSessions { get; }

    /// <summary>Raised when <see cref="ActiveSessions"/> changes.</summary>
    event EventHandler<IReadOnlyList<AirPlaySession>>? SessionsChanged;

    /// <summary>Raised when the process exits unexpectedly or by user request.</summary>
    event EventHandler<AirPlayExitEventArgs>? Exited;

    /// <summary>
    /// Spawn the receiver and start listening for incoming AirPlay
    /// connections. Safe to call multiple times — already-running receivers
    /// are returned as-is.
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>Kill the underlying process and free the port.</summary>
    Task StopAsync();
}

/// <summary>Payload for <see cref="IAirPlayReceiver.Exited"/>.</summary>
public sealed class AirPlayExitEventArgs : EventArgs
{
    public AirPlayExitEventArgs(int exitCode, string? stderrTail)
    {
        ExitCode = exitCode;
        StderrTail = stderrTail;
    }

    public int ExitCode { get; }
    public string? StderrTail { get; }
}