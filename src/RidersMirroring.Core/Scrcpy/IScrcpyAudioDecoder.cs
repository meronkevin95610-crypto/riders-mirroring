namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// One decoded audio packet produced by <see cref="IScrcpyAudioDecoder"/>.
/// PCM samples are interleaved Float32 in range [-1, 1].
/// </summary>
/// <remarks>
/// Float32 is what WASAPI shared mode consumes natively, so we avoid an
/// extra sample-format conversion on the render path.
/// </remarks>
public sealed record DecodedAudioPacket(
    /// <summary>Sample rate in Hz.</summary>
    int SampleRateHz,
    /// <summary>Number of channels (1 = mono, 2 = stereo).</summary>
    int Channels,
    /// <summary>Interleaved Float32 samples. Length MUST be a multiple of <see cref="Channels"/>.</summary>
    ReadOnlyMemory<float> Samples,
    /// <summary>Presentation time of the first sample.</summary>
    TimeSpan PresentationTime);

/// <summary>
/// Abstraction over a scrcpy audio decoder. Mirrors
/// <see cref="IScrcpyVideoDecoder"/>'s shape but emits PCM frames instead
/// of BGRA pixels.
/// </summary>
public interface IScrcpyAudioDecoder : IAsyncDisposable
{
    /// <summary>True once <see cref="Prime"/> has been called and the decoder is ready for packets.</summary>
    bool IsReady { get; }

    /// <summary>Effective stream configuration, populated after <see cref="Prime"/>.</summary>
    ScrcpyAudioStreamHeader? Header { get; }

    /// <summary>Raised for every decoded PCM frame.</summary>
    event EventHandler<DecodedAudioPacket>? AudioPacketDecoded;

    /// <summary>Configure the decoder with the codec id extracted from the audio stream.</summary>
    void Prime(ScrcpyAudioStreamHeader header);

    /// <summary>Push the next encoded audio packet (post-header). Must be called after <see cref="Prime"/>.</summary>
    Task PushPacketAsync(ReadOnlyMemory<byte> packet, TimeSpan presentationTime, CancellationToken cancellationToken = default);

    /// <summary>Signal end-of-stream — decoder should flush its internal buffers.</summary>
    Task FlushAsync(CancellationToken cancellationToken = default);
}

/// <summary>Factory for <see cref="IScrcpyAudioDecoder"/>. Mirrors <see cref="IScrcpyVideoDecoderFactory"/>.</summary>
public interface IScrcpyAudioDecoderFactory
{
    IScrcpyAudioDecoder Create();
}
