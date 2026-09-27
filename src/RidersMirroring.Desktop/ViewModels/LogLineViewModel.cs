using System.Globalization;

namespace Riders.Mirroring.Desktop.ViewModels;

/// <summary>
/// A single line in the live log strip. Immutable because the strip just
/// appends — we never mutate an existing entry.
/// </summary>
public sealed record LogLineViewModel(DateTime Timestamp, LogLevel Level, string Message)
{
    /// <summary>Pre-formatted "HH:mm:ss" used by the log strip template.</summary>
    public string TimestampText => Timestamp.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>Short uppercase tag (INFO / WARN / ERR) rendered in the
    /// chronicle's pill column.</summary>
    public string LevelText => Level switch
    {
        LogLevel.Warning => "WARN",
        LogLevel.Error   => "ERR",
        _                => "INFO",
    };
}

public enum LogLevel
{
    Info,
    Warning,
    Error,
}