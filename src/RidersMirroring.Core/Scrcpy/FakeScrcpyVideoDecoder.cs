namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Test double for <see cref="IScrcpyVideoDecoder"/>: simply records every
/// packet it receives and synthesises a solid-coloured frame when asked to
/// flush. Useful for wiring up the Desktop layer without committing to a
/// specific FFmpeg backend yet.
/// </summary>
public sealed class FakeScrcpyVideoDecoder : IScrcpyVideoDecoder
{
    private readonly List<byte[]> _packets = new();
    private readonly object _lock = new();

    /// <inheritdoc/>
    public bool IsReady => Header is not null;

    /// <inheritdoc/>
    public ScrcpyStreamHeader? Header { get; private set; }

    /// <summary>Read-only view of the packets pushed so far.</summary>
    public IReadOnlyList<byte[]> Packets
    {
        get
        {
            lock (_lock)
            {
                return _packets.ToArray();
            }
        }
    }

    /// <summary>Number of <see cref="FrameDecoded"/> events raised so far.</summary>
    public int FramesRaised { get; private set; }

    /// <summary>
    /// Optional factory for synthetic frames. Defaults to a single solid
    /// black frame the first time <see cref="FlushAsync"/> is called.
    /// </summary>
    public Func<ScrcpyStreamHeader, IEnumerable<DecodedVideoFrame>>? FrameFactory { get; set; }

    /// <inheritdoc/>
    public event EventHandler<DecodedVideoFrame>? FrameDecoded;

    /// <inheritdoc/>
    public void Prime(ScrcpyStreamHeader header) => Header = header;

    /// <inheritdoc/>
    public Task PushPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
    {
        if (Header is null)
        {
            throw new InvalidOperationException(
                "Decoder received a packet before any header was supplied. " +
                "Did you forget to call ScrcpyVideoStream.StartAsync()?");
        }

        var copy = packet.ToArray();
        lock (_lock)
        {
            _packets.Add(copy);
        }
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (Header is null)
        {
            return Task.CompletedTask;
        }

        var factory = FrameFactory ?? DefaultFrameFactory;
        foreach (var frame in factory(Header))
        {
            FramesRaised++;
            FrameDecoded?.Invoke(this, frame);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Simulate the decoder being primed with metadata by the upper layer
    /// (called from <see cref="ScrcpyVideoStream"/>).
    /// </summary>
    private void PrimeDecoder(ScrcpyStreamHeader header) => Header = header;

    private static IEnumerable<DecodedVideoFrame> DefaultFrameFactory(ScrcpyStreamHeader header)
    {
        var pixels = new byte[header.Width * header.Height * 4];
        yield return new DecodedVideoFrame(
            Width: header.Width,
            Height: header.Height,
            Stride: header.Width * 4,
            BgraPixels: pixels,
            PresentationTime: TimeSpan.Zero);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
