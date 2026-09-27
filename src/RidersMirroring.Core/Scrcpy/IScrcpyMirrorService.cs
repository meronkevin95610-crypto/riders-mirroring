namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Abstraction for managing the lifecycle of an active Android screen mirroring session.
/// </summary>
public interface IScrcpyMirrorService
{
    /// <summary>Whether a mirror process is currently active.</summary>
    bool IsRunning { get; }

    /// <summary>Serial of the currently mirrored Android device, or null if stopped.</summary>
    string? CurrentDeviceSerial { get; }

    /// <summary>Display name of the currently mirrored Android device.</summary>
    string? CurrentDeviceDisplayName { get; }

    /// <summary>Locates the <c>scrcpy.exe</c> executable, or null if not found.</summary>
    string? LocateScrcpyBinary();

    /// <summary>Starts mirroring the target device.</summary>
    Task StartAsync(
        string serial,
        string? deviceDisplayName = null,
        ScrcpyOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>Stops the active mirroring session.</summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>Fired when a mirroring session starts successfully.</summary>
    event EventHandler<string>? SessionStarted;

    /// <summary>Fired when the active mirroring process exits.</summary>
    event EventHandler<MirrorExitEventArgs>? SessionExited;

    /// <summary>Fired when the mirror process outputs a log line.</summary>
    event EventHandler<string>? LogOutputReceived;
}
