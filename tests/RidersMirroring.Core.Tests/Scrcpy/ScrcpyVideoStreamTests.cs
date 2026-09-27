using System.Text;
using FluentAssertions;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.Core.Tests.Scrcpy;

public class ScrcpyVideoStreamTests
{
    [Fact]
    public async Task StartAsync_ParsesHeader_AndFeedsPacketsToDecoder()
    {
        // Build a fake scrcpy stream: header + 3 framed NAL units.
        var header = ScrcpyStreamParser.BuildHeader("Pixel 8", "h264", 720, 1600);
        var packets = new List<byte[]>
        {
            new byte[] { 0x67, 0x42, 0x00, 0x1E }, // fake SPS
            new byte[] { 0x68, 0xCE, 0x38, 0x80 }, // fake PPS
            new byte[] { 0x65, 0x88, 0x84, 0x00 }, // fake IDR slice
        };

        using var stream = new MemoryStream();
        stream.Write(header, 0, header.Length);
        foreach (var pkt in packets)
        {
            var lenBuf = new byte[4];
            // Big-endian length, as per the scrcpy protocol.
            lenBuf[0] = (byte)(pkt.Length >> 24);
            lenBuf[1] = (byte)(pkt.Length >> 16);
            lenBuf[2] = (byte)(pkt.Length >> 8);
            lenBuf[3] = (byte) pkt.Length;
            stream.Write(lenBuf, 0, 4);
            stream.Write(pkt, 0, pkt.Length);
        }
        stream.Position = 0;

        var decoder = new FakeScrcpyVideoDecoder();
        decoder.FrameDecoded += (_, _) => { /* swallow */ };
        await using var videoStream = new ScrcpyVideoStream(stream, decoder);

        await videoStream.StartAsync();

        // Give the reader loop a tick to drain.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (decoder.Packets.Count < 3 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        videoStream.Header.Should().NotBeNull();
        videoStream.Header!.DeviceName.Should().Be("Pixel 8");
        videoStream.Header.Width.Should().Be(720);

        decoder.Packets.Should().HaveCount(3);
        decoder.Packets[0].Should().Equal(new byte[] { 0x67, 0x42, 0x00, 0x1E });
    }

    [Fact]
    public async Task StartAsync_EmitsAtLeastOneFrameOnFlush()
    {
        var header = ScrcpyStreamParser.BuildHeader("Pixel", "h264", 32, 64);
        using var stream = new MemoryStream(header);

        var decoder = new FakeScrcpyVideoDecoder();
        var raised = 0;
        decoder.FrameDecoded += (_, _) => Interlocked.Increment(ref raised);

        await using var videoStream = new ScrcpyVideoStream(stream, decoder);
        await videoStream.StartAsync();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (raised == 0 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        raised.Should().BeGreaterThan(0);
        decoder.Header.Should().NotBeNull();
    }

    [Fact]
    public async Task StartAsync_StopsCleanly_OnUnsupportedCodec()
    {
        var header = ScrcpyStreamParser.BuildHeader("Pixel", "mpeg2", 100, 200);
        using var stream = new MemoryStream(header);

        var decoder = new FakeScrcpyVideoDecoder();
        await using var videoStream = new ScrcpyVideoStream(stream, decoder);
        await videoStream.StartAsync();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (videoStream.Header is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        videoStream.Header.Should().NotBeNull();
        videoStream.Header!.IsSupportedCodec.Should().BeFalse();
        decoder.Packets.Should().BeEmpty();
    }
}
