using FluentAssertions;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.Core.Tests.Scrcpy;

public class FakeScrcpyVideoDecoderTests
{
    [Fact]
    public async Task PushPacketAsync_Throws_BeforeHeaderIsPrimed()
    {
        var decoder = new FakeScrcpyVideoDecoder();

        Func<Task> act = async () => await decoder.PushPacketAsync(new byte[] { 0x01 });

        await act.Should().ThrowAsync<InvalidOperationException>()
                 .WithMessage("*header*");
    }

    [Fact]
    public async Task PushPacketAsync_RecordsPackets_WhenPrimed()
    {
        var decoder = new FakeScrcpyVideoDecoder();
        decoder.Prime(new ScrcpyStreamHeader("Pixel 8", "h264", 1080, 2400));

        await decoder.PushPacketAsync(new byte[] { 0x01, 0x02 });
        await decoder.PushPacketAsync(new byte[] { 0x03 });

        decoder.IsReady.Should().BeTrue();
        decoder.Packets.Should().HaveCount(2);
    }

    [Fact]
    public async Task FlushAsync_RaisesOneFrameByDefault()
    {
        var decoder = new FakeScrcpyVideoDecoder();
        decoder.Prime(new ScrcpyStreamHeader("Pixel 8", "h264", 4, 4));

        var raised = 0;
        decoder.FrameDecoded += (_, _) => Interlocked.Increment(ref raised);

        await decoder.FlushAsync();

        raised.Should().Be(1);
        decoder.FramesRaised.Should().Be(1);
    }

    [Fact]
    public async Task FlushAsync_UsesCustomFrameFactory()
    {
        var decoder = new FakeScrcpyVideoDecoder();
        decoder.Prime(new ScrcpyStreamHeader("Pixel", "h264", 4, 4));

        var raised = 0;
        decoder.FrameDecoded += (_, _) => Interlocked.Increment(ref raised);

        decoder.FrameFactory = BuildTwoFrameSequence;
        await decoder.FlushAsync();

        raised.Should().Be(2);
    }

    private static IEnumerable<DecodedVideoFrame> BuildTwoFrameSequence(ScrcpyStreamHeader header)
    {
        yield return new DecodedVideoFrame(header.Width, header.Height, header.Width * 4, new byte[16], TimeSpan.FromMilliseconds(16));
        yield return new DecodedVideoFrame(header.Width, header.Height, header.Width * 4, new byte[16], TimeSpan.FromMilliseconds(33));
    }
}
