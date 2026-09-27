using System.Text;
using FluentAssertions;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.Core.Tests.Scrcpy;

public class ScrcpyStreamParserTests
{
    [Fact]
    public void TryReadHeader_ParsesAllFields_FromCompleteStream()
    {
        var bytes = ScrcpyStreamParser.BuildHeader(
            deviceName: "Pixel 8",
            codecId: "h264",
            width: 1080,
            height: 2400);

        using var ms = new MemoryStream(bytes);

        var ok = ScrcpyStreamParser.TryReadHeader(ms, out var header, out var consumed);

        ok.Should().BeTrue();
        consumed.Should().Be(bytes.Length);
        header.Should().NotBeNull();
        header!.DeviceName.Should().Be("Pixel 8");
        header.CodecId.Should().Be("h264");
        header.Width.Should().Be(1080);
        header.Height.Should().Be(2400);
        header.IsSupportedCodec.Should().BeTrue();
    }

    [Fact]
    public void TryReadHeader_AcceptsH265_Av1()
    {
        var bytes = ScrcpyStreamParser.BuildHeader("Device", "h265", 720, 1280);
        using var ms = new MemoryStream(bytes);

        ScrcpyStreamParser.TryReadHeader(ms, out var header, out _);

        header!.CodecId.Should().Be("h265");
        header.IsSupportedCodec.Should().BeTrue();
    }

    [Fact]
    public void TryReadHeader_AcceptsAv1()
    {
        var bytes = ScrcpyStreamParser.BuildHeader("Device", "av1", 480, 800);
        using var ms = new MemoryStream(bytes);

        ScrcpyStreamParser.TryReadHeader(ms, out var header, out _);

        header!.IsSupportedCodec.Should().BeTrue();
    }

    [Fact]
    public void TryReadHeader_RejectsUnknownCodecAsUnsupported()
    {
        var bytes = ScrcpyStreamParser.BuildHeader("Device", "mpeg2", 100, 100);
        using var ms = new MemoryStream(bytes);

        ScrcpyStreamParser.TryReadHeader(ms, out var header, out _);

        header!.IsSupportedCodec.Should().BeFalse();
    }

    [Fact]
    public void TryReadHeader_ReturnsFalse_OnTruncatedStream()
    {
        // Header claims "Pixel" but we only provide 2 bytes of the name.
        var bytes = new byte[6];
        BitConverter.GetBytes(5u).CopyTo(bytes, 0); // name length = 5
        Encoding.UTF8.GetBytes("Pi").CopyTo(bytes, 4); // only 2 of 5 bytes

        using var ms = new MemoryStream(bytes);
        var ok = ScrcpyStreamParser.TryReadHeader(ms, out var header, out _);

        ok.Should().BeFalse();
        header.Should().BeNull();
    }

    [Fact]
    public void TryReadHeader_ThrowsOnImplausibleNameLength()
    {
        var bytes = new byte[4];
        // 0xFFFFFF00 → 4 294 967 040, way over the 1024 sanity limit.
        bytes[0] = 0xFF; bytes[1] = 0xFF; bytes[2] = 0xFF; bytes[3] = 0x00;

        using var ms = new MemoryStream(bytes);

        Action act = () => ScrcpyStreamParser.TryReadHeader(ms, out _, out _);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*exceeds sanity limit*");
    }
    [Fact]
    public void TryReadHeader_ThrowsOnImplausibleDimensions()
    {
        var bytes = ScrcpyStreamParser.BuildHeader("d", "h264", 0, 0);

        using var ms = new MemoryStream(bytes);

        Action act = () => ScrcpyStreamParser.TryReadHeader(ms, out _, out _);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*implausible dimensions*");
    }

    [Fact]
    public void BuildHeader_IsRoundTripSafe()
    {
        var original = ScrcpyStreamParser.BuildHeader("Galaxy S24", "h264", 1080, 2340);
        using var ms = new MemoryStream(original);

        ScrcpyStreamParser.TryReadHeader(ms, out var header, out var consumed);

        consumed.Should().Be(original.Length);
        header!.DeviceName.Should().Be("Galaxy S24");
        header.Width.Should().Be(1080);
        header.Height.Should().Be(2340);
    }

    [Fact]
    public void TryReadHeader_Throws_ArgumentNullException_On_NullStream()
    {
        Action act = () => ScrcpyStreamParser.TryReadHeader(null!, out _, out _);
        act.Should().Throw<ArgumentNullException>()
           .WithParameterName("stream");
    }

    [Fact]
    public void TryReadHeader_Throws_When_NameLength_Exceeds_SanityLimit()
    {
        // nameLen = 2000, just over the 1024 ceiling.
        var bytes = new byte[4];
        BitConverter.GetBytes(2000u).CopyTo(bytes, 0);

        using var ms = new MemoryStream(bytes);

        Action act = () => ScrcpyStreamParser.TryReadHeader(ms, out _, out _);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*exceeds sanity limit*");
    }

    [Fact]
    public void TryReadHeader_Throws_When_Dimensions_Exceed_Max()
    {
        // 10000x10000 — over the 8192 limit per axis.
        var bytes = ScrcpyStreamParser.BuildHeader("d", "h264", 10000, 10000);

        using var ms = new MemoryStream(bytes);

        Action act = () => ScrcpyStreamParser.TryReadHeader(ms, out _, out _);
        act.Should().Throw<InvalidDataException>()
           .WithMessage("*implausible dimensions*");
    }

    [Fact]
    public void TryReadHeader_Accepts_EmptyDeviceName_AndEmptyCodecId()
    {
        var bytes = ScrcpyStreamParser.BuildHeader(string.Empty, string.Empty, 320, 480);

        using var ms = new MemoryStream(bytes);

        var ok = ScrcpyStreamParser.TryReadHeader(ms, out var header, out _);

        ok.Should().BeTrue();
        header!.DeviceName.Should().BeEmpty();
        header.CodecId.Should().BeEmpty();
        header.IsSupportedCodec.Should().BeFalse();
    }
}
