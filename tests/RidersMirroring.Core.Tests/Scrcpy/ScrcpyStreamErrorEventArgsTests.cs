using FluentAssertions;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.Core.Tests.Scrcpy;

public class ScrcpyStreamErrorEventArgsTests
{
    [Fact]
    public void Constructor_StoresMessageAndKind()
    {
        var args = new ScrcpyStreamErrorEventArgs("Stream closed", ScrcpyStreamErrorKind.EndOfStream);

        args.Message.Should().Be("Stream closed");
        args.Kind.Should().Be(ScrcpyStreamErrorKind.EndOfStream);
    }

    [Fact]
    public void Constructor_TreatsNullMessageAsEmpty()
    {
        var args = new ScrcpyStreamErrorEventArgs(null!, ScrcpyStreamErrorKind.DecoderFailure);

        args.Message.Should().Be(string.Empty);
        args.Kind.Should().Be(ScrcpyStreamErrorKind.DecoderFailure);
    }

    [Theory]
    [InlineData(ScrcpyStreamErrorKind.EndOfStream)]
    [InlineData(ScrcpyStreamErrorKind.MalformedFrame)]
    [InlineData(ScrcpyStreamErrorKind.DecoderFailure)]
    public void Kind_Enum_HasExpectedValues(ScrcpyStreamErrorKind kind)
    {
        Enum.IsDefined(typeof(ScrcpyStreamErrorKind), kind).Should().BeTrue();
    }
}
