namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>Payload for <see cref="ScrcpyVideoStream.StreamError"/>.</summary>
public sealed class ScrcpyStreamErrorEventArgs : EventArgs
{
    public ScrcpyStreamErrorEventArgs(string message, ScrcpyStreamErrorKind kind)
    {
        Message = message ?? string.Empty;
        Kind = kind;
    }

    /// <summary>Human-readable description, safe to surface to the UI.</summary>
    public string Message { get; }

    /// <summary>Category of the failure — drives icons / suggested actions.</summary>
    public ScrcpyStreamErrorKind Kind { get; }
}

/// <summary>Coarse classification of why a scrcpy stream stopped.</summary>
public enum ScrcpyStreamErrorKind
{
    /// <summary>The TCP socket closed without an explicit EOF — usually a USB cable yank.</summary>
    EndOfStream,

    /// <summary>A NAL packet length was out of range or the payload was malformed.</summary>
    MalformedFrame,

    /// <summary>The decoder raised an exception while processing a packet.</summary>
    DecoderFailure,
}
