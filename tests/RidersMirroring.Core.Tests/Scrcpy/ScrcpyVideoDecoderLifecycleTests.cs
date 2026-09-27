using FluentAssertions;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.Core.Tests.Scrcpy;

/// <summary>
/// Tests for the FFmpeg-backed <see cref="ScrcpyVideoDecoder"/>
/// that exercise only the public lifecycle (constructor, IsReady,
/// Header, Dispose) without actually invoking FFmpeg.
///
/// <para>
/// Why not decode a real frame here? <see cref="ScrcpyVideoDecoder"/>
/// has an unconditional dependency on the vendored FFmpeg shared DLLs
/// (avcodec-62.dll et al.) being present at <c>ffmpeg.RootPath</c>.
/// That condition is true on the developer machine and on the release
/// MSI, but cannot be guaranteed for every CI matrix entry — and we
/// deliberately keep Core.Tests hardware-agnostic. End-to-end FFmpeg
/// decoding is covered by the manual
/// <c>tools/RidersMirroring.RawTest</c> runner instead.
/// </para>
/// </summary>
public class ScrcpyVideoDecoderLifecycleTests
{
    [Fact]
    public void Constructor_SetsIsReadyFalse_AndHeaderIsNull()
    {
        var decoder = new ScrcpyVideoDecoder();

        decoder.IsReady.Should().BeFalse();
        decoder.Header.Should().BeNull();

        // IAsyncDisposable — explicit cleanup, not a `using var`.
        decoder.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Dispose_IsIdempotent()
    {
        var decoder = new ScrcpyVideoDecoder();
        await decoder.DisposeAsync();
        // A second Dispose must NOT throw — IDisposable contract.
        var act = async () => await decoder.DisposeAsync();
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void DisposeAsync_DoubleCall_ReturnsCompletedValueTask()
    {
        var decoder = new ScrcpyVideoDecoder();
        var first = decoder.DisposeAsync();
        var second = decoder.DisposeAsync();
        first.IsCompletedSuccessfully.Should().BeTrue();
        second.IsCompletedSuccessfully.Should().BeTrue();
    }

    [Fact]
    public async Task FrameDecoded_AllowsSubscription_BeforePrime()
    {
        // Subscribing before Prime() must not throw. The actual event
        // is only raised after a frame is decoded (which requires
        // Prime() + PushPacketAsync() + a real H.264 stream).
        var decoder = new ScrcpyVideoDecoder();
        try
        {
            var raised = 0;
            decoder.FrameDecoded += (_, _) => Interlocked.Increment(ref raised);
            // No assertion on raised — just that the subscription took.
            decoder.IsReady.Should().BeFalse();
        }
        finally
        {
            await decoder.DisposeAsync();
        }
    }
}
