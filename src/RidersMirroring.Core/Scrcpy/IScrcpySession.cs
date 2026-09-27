namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Lifecycle of one scrcpy mirroring session. Started by
/// <see cref="ScrcpyServer"/> and disposed by the caller.
/// </summary>
public interface IScrcpySession : IAsyncDisposable
{
    /// <summary>Serial of the device being mirrored.</summary>
    string DeviceSerial { get; }

    /// <summary>Effective options used for this session.</summary>
    ScrcpyOptions Options { get; }

    /// <summary>True once the scrcpy-server handshake has succeeded.</summary>
    bool IsRunning { get; }
}