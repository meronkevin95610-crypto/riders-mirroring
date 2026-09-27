using FluentAssertions;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.Core.Tests.Scrcpy;

/// <summary>
/// Tests covering <see cref="ScrcpyVideoStream.UseRawStream"/>, the
/// v4.0 raw-stream mode where the server no longer writes the
/// device-name / codec-id / width / height metadata header on the wire.
///
/// In this mode <see cref="ScrcpyVideoStream"/> is a pure forwarder:
/// it never parses a header, and the consumer is expected to have
/// already primed the decoder with a fabricated
/// <see cref="ScrcpyStreamHeader"/> derived from server-side
/// <c>--video-*</c> options.
/// </summary>
public class ScrcpyVideoStreamRawModeTests
{
    [Fact]
    public async Task UseRawStream_DoesNotConsumeHeaderBytes_FromStream()
    {
        // Build a stream that starts with NAL-framed data — NO scrcpy
        // metadata header. The reader must treat every byte as post-header.
        var packets = new List<byte[]>
        {
            new byte[] { 0x67, 0x42, 0x00, 0x1E }, // fake SPS
            new byte[] { 0x65, 0x88, 0x84, 0x00 }, // fake IDR slice
        };

        using var stream = new MemoryStream();
        foreach (var pkt in packets)
        {
            WriteFramedPacket(stream, pkt);
        }
        stream.Position = 0;

        var decoder = new FakeScrcpyVideoDecoder();
        // In raw mode the consumer must pre-Prime the decoder.
        decoder.Prime(new ScrcpyStreamHeader("Pixel 8", "h264", 720, 1600));

        await using var videoStream = new ScrcpyVideoStream(stream, decoder)
        {
            UseRawStream = true,
        };

        await videoStream.StartAsync();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (decoder.Packets.Count < 2 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        decoder.IsReady.Should().BeTrue();
        decoder.Packets.Should().HaveCount(2);
        // Header is set by the consumer's Prime() call, not by the stream.
        videoStream.Header.Should().BeNull(
            "raw mode skips header parsing — Header remains null until the consumer sets it");
        decoder.Header!.CodecId.Should().Be("h264");
        decoder.Header.Width.Should().Be(720);
    }

    [Fact]
    public async Task UseRawStream_ForwardsEveryByte_VerbatimToDecoder()
    {
        // Build NAL-framed data that includes a sentinel non-zero byte
        // (0xAB) that would never appear in a scrcpy metadata header
        // prefix. If the reader tried to interpret the first bytes as
        // a header it would corrupt the payload.
        var payload = new byte[] { 0xAB, 0xCD, 0xEF, 0x12, 0x34, 0x56, 0x78, 0x9A };
        using var stream = new MemoryStream();
        WriteFramedPacket(stream, payload);
        stream.Position = 0;

        var decoder = new FakeScrcpyVideoDecoder();
        decoder.Prime(new ScrcpyStreamHeader("Pixel 8", "h264", 100, 200));

        await using var videoStream = new ScrcpyVideoStream(stream, decoder)
        {
            UseRawStream = true,
        };

        await videoStream.StartAsync();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (decoder.Packets.Count < 1 && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        decoder.Packets.Should().HaveCount(1);
        decoder.Packets[0].Should().Equal(payload);
    }

    [Fact]
    public async Task StartAsync_CalledTwice_Throws()
    {
        var header = ScrcpyStreamParser.BuildHeader("Pixel", "h264", 32, 64);
        using var stream = new MemoryStream(header);

        var decoder = new FakeScrcpyVideoDecoder();
        await using var videoStream = new ScrcpyVideoStream(stream, decoder);

        await videoStream.StartAsync();

        Func<Task> act = async () => await videoStream.StartAsync();
        await act.Should().ThrowAsync<InvalidOperationException>()
                 .WithMessage("*already running*");
    }

    [Fact]
    public async Task UseRawStream_TruncatedMidPacket_RaisesStreamError()
    {
        // Header signals a 1024-byte packet but we only write 100 bytes.
        var lengthBytes = new byte[4];
        lengthBytes[0] = 0x00; lengthBytes[1] = 0x00;
        lengthBytes[2] = 0x04; lengthBytes[3] = 0x00; // 1024
        var partialPayload = new byte[100];
        var stream = new MemoryStream();
        stream.Write(lengthBytes, 0, 4);
        stream.Write(partialPayload, 0, partialPayload.Length);
        stream.Position = 0;

        var decoder = new FakeScrcpyVideoDecoder();
        decoder.Prime(new ScrcpyStreamHeader("Pixel", "h264", 32, 64));

        await using var videoStream = new ScrcpyVideoStream(stream, decoder)
        {
            UseRawStream = true,
        };

        var errorRaised = new TaskCompletionSource<ScrcpyStreamErrorEventArgs>();
        videoStream.StreamError += (_, e) => errorRaised.TrySetResult(e);

        await videoStream.StartAsync();

        var completed = await Task.WhenAny(errorRaised.Task, Task.Delay(TimeSpan.FromSeconds(2)));
        completed.Should().Be(errorRaised.Task,
            "a truncated mid-packet must surface as a StreamError event");

        var err = await errorRaised.Task;
        err.Kind.Should().BeOneOf(
            ScrcpyStreamErrorKind.EndOfStream,
            ScrcpyStreamErrorKind.MalformedFrame);
        err.Message.Should().NotBeNullOrWhiteSpace();
    }

    private static void WriteFramedPacket(Stream stream, byte[] payload)
    {
        var lenBuf = new byte[4];
        lenBuf[0] = (byte)(payload.Length >> 24);
        lenBuf[1] = (byte)(payload.Length >> 16);
        lenBuf[2] = (byte)(payload.Length >> 8);
        lenBuf[3] = (byte) payload.Length;
        stream.Write(lenBuf, 0, 4);
        stream.Write(payload, 0, payload.Length);
    }
}
