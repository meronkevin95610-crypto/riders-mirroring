namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Parsed metadata of one scrcpy video stream, extracted from the 12-byte
/// header that the server sends right after the socket handshake.
/// </summary>
/// <remarks>
/// Header layout (scrcpy 1.x / 2.x):
/// <code>
/// [0..3]  device_name length  (uint32 little-endian)
/// [4..N]  device_name (UTF-8, length = header[0..3])
/// [N..N+3] codec_id length     (uint32 little-endian)
/// [...]    codec_id (UTF-8, e.g. "h264")
/// [...]    width   (uint32 LE)
/// [...]    height  (uint32 LE)
/// </code>
/// We don't care about the actual pixel data here — only the framing so
/// the decoder can be wired up against any H.264/AV1 backend.
/// </remarks>
public sealed record ScrcpyStreamHeader(
    string DeviceName,
    string CodecId,
    int Width,
    int Height)
{
    /// <summary>True if the codec is one we know how to decode locally.</summary>
    public bool IsSupportedCodec =>
        string.Equals(CodecId, "h264",  StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CodecId, "h265",  StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CodecId, "av1",   StringComparison.OrdinalIgnoreCase);
}

/// <summary>
/// Parsed metadata of one scrcpy audio stream. Audio streams carry only a
/// 4-byte codec id (<c>"opus"</c>, <c>"aac"</c>, <c>"flac"</c>, <c>"raw"</c>) —
/// no device name, no dimensions, no session packets.
/// </summary>
public sealed record ScrcpyAudioStreamHeader(
    string CodecId,
    /// <summary>Sample rate in Hz. <c>0</c> for codecs where it's not pre-announced (opus defaults to 48 kHz).</summary>
    int SampleRateHz = 0,
    /// <summary>Number of channels. <c>0</c> for codecs where it's not pre-announced (opus is stereo).</summary>
    int Channels = 0)
{
    /// <summary>True if the codec is one we know how to decode with FFmpeg.</summary>
    public bool IsSupportedCodec =>
        string.Equals(CodecId, "opus", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CodecId, "aac",  StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CodecId, "flac", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(CodecId, "raw",  StringComparison.OrdinalIgnoreCase);
}
