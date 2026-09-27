namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// One decoded video frame surfaced by <see cref="IScrcpyVideoDecoder"/>.
/// </summary>
/// <remarks>
/// We deliberately use an opaque byte payload instead of
/// <c>System.Windows.Media.Imaging.BitmapSource</c> so the Core layer stays
/// free of WPF dependencies. The Desktop layer is expected to wrap the
/// payload in a <c>BitmapSource</c> for rendering.
/// </remarks>
public sealed record DecodedVideoFrame(
    int Width,
    int Height,
    int Stride,
    ReadOnlyMemory<byte> BgraPixels,
    TimeSpan PresentationTime);

/// <summary>
/// Abstraction over a H.264 / H.265 / AV1 decoder used to render the
/// scrcpy video stream. Implementations live in the Desktop layer
/// (FFmpeg-backed) and in tests (mock).
/// </summary>
public interface IScrcpyVideoDecoder : IAsyncDisposable
{
    /// <summary>True once the decoder has been fed a valid header and is ready for NAL units.</summary>
    bool IsReady { get; }

    /// <summary>Effective video configuration, populated once the header has been parsed.</summary>
    ScrcpyStreamHeader? Header { get; }

    /// <summary>Raised for every decoded frame.</summary>
    event EventHandler<DecodedVideoFrame>? FrameDecoded;

    /// <summary>
    /// Configure the decoder with the stream metadata extracted by
    /// <see cref="ScrcpyStreamParser"/>. Must be called before
    /// <see cref="PushPacketAsync"/>.
    /// </summary>
    void Prime(ScrcpyStreamHeader header);

    /// <summary>
    /// Push the next chunk of scrcpy-encoded bytes (post-header) into the
    /// decoder. The decoder is responsible for NAL framing and
    /// packetisation.
    /// </summary>
    Task PushPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default);

    /// <summary>
    /// Signal end-of-stream — decoder should flush its internal buffers and
    /// raise any remaining <see cref="FrameDecoded"/> events.
    /// </summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}
