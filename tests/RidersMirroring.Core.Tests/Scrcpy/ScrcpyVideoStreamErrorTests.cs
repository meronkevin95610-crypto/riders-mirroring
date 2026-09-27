using System.Text;
using FluentAssertions;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.Core.Tests.Scrcpy;

public class ScrcpyVideoStreamErrorTests
{
    [Fact]
    public async Task StreamClosedBeforeHeader_RaisesStreamError_WithEndOfStream()
    {
        using var stream = new MemoryStream(); // empty
        var decoder = new FakeScrcpyVideoDecoder();

        ScrcpyStreamErrorEventArgs? captured = null;
        await using var videoStream = new ScrcpyVideoStream(stream, decoder);
        videoStream.StreamError += (_, args) => captured = args;

        await videoStream.StartAsync();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (captured is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        captured.Should().NotBeNull();
        captured!.Kind.Should().Be(ScrcpyStreamErrorKind.EndOfStream);
    }

    [Fact]
    public async Task MalformedPacketLength_RaisesStreamError_WithMalformedFrame()
    {
        // Build a valid header, then append a 4-byte BE length of 0 (which is invalid).
        var bytes = ScrcpyStreamParser.BuildHeader("Pixel", "h264", 100, 100);
        var withBadLength = new byte[bytes.Length + 4];
        Buffer.BlockCopy(bytes, 0, withBadLength, 0, bytes.Length);
        // 4-byte BE length = 0 → invalid (<=0)
        // bytes are already zero, so TryReadExact sees length = 0 → triggers MalformedFrame.

        using var stream = new MemoryStream(withBadLength);
        var decoder = new FakeScrcpyVideoDecoder();

        ScrcpyStreamErrorEventArgs? captured = null;
        await using var videoStream = new ScrcpyVideoStream(stream, decoder);
        videoStream.StreamError += (_, args) => captured = args;

        await videoStream.StartAsync();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (captured is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        captured.Should().NotBeNull();
        captured!.Kind.Should().Be(ScrcpyStreamErrorKind.MalformedFrame);
        captured.Message.Should().Contain("out of range");
    }

    [Fact]
    public async Task StreamTruncatedMidPacket_RaisesStreamError_WithEndOfStream()
    {
        // Header + length prefix claiming 100 bytes but stream ends right after.
        var bytes = ScrcpyStreamParser.BuildHeader("Pixel", "h264", 100, 100);
        var lengthPrefix = new byte[] { 0x00, 0x00, 0x00, 0x64 }; // 100 BE
        var truncated = new byte[bytes.Length + lengthPrefix.Length];
        Buffer.BlockCopy(bytes, 0, truncated, 0, bytes.Length);
        Buffer.BlockCopy(lengthPrefix, 0, truncated, bytes.Length, lengthPrefix.Length);

        using var stream = new MemoryStream(truncated);
        var decoder = new FakeScrcpyVideoDecoder();

        ScrcpyStreamErrorEventArgs? captured = null;
        await using var videoStream = new ScrcpyVideoStream(stream, decoder);
        videoStream.StreamError += (_, args) => captured = args;

        await videoStream.StartAsync();

        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (captured is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        captured.Should().NotBeNull();
        captured!.Kind.Should().Be(ScrcpyStreamErrorKind.EndOfStream);
        captured.Message.Should().Contain("truncated");
    }
}
