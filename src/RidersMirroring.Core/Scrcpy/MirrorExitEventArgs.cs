namespace Riders.Mirroring.Core.Scrcpy;
 
/// <summary>
/// Event arguments supplied when a scrcpy mirror session terminates.
/// </summary>
public sealed class MirrorExitEventArgs : EventArgs
{
    public int ExitCode { get; }
    public string? StderrTail { get; }

    public MirrorExitEventArgs(int exitCode, string? stderrTail = null)
    {
        ExitCode = exitCode;
        StderrTail = stderrTail;
    }
}
