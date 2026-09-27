using FluentAssertions;
using Riders.Mirroring.Core.Scrcpy;
using Xunit;

namespace Riders.Mirroring.Core.Tests.Scrcpy;

/// <summary>
/// Unit tests for <see cref="ScrcpyStreamHeader"/> codec support.
/// </summary>
public sealed class ScrcpyStreamHeaderTests
{
    [Theory]
    [InlineData("h264", true)]
    [InlineData("H264", true)]
    [InlineData("h265", true)]
    [InlineData("av1",  true)]
    [InlineData("AV1",  true)]
    [InlineData("vp9",  false)]
    [InlineData("mpeg4", false)]
    [InlineData("h266", false)]
    public void IsSupportedCodec_Returns_Expected(string codec, bool expected)
    {
        var header = new ScrcpyStreamHeader("Pixel 8", codec, 1080, 1920);
        header.IsSupportedCodec.Should().Be(expected);
    }

    [Fact]
    public void IsSupportedCodec_Returns_False_For_Empty_String()
    {
        var header = new ScrcpyStreamHeader("Pixel 8", string.Empty, 1080, 1920);
        header.IsSupportedCodec.Should().BeFalse();
    }
}
