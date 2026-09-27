using FluentAssertions;
using Riders.Mirroring.Core.Scrcpy;
using Xunit;

namespace Riders.Mirroring.Core.Tests.Scrcpy;

public class ScrcpyOptionsTests
{
    [Fact]
    public void Defaults_AreReasonable()
    {
        var opts = ScrcpyOptions.Default;

        opts.MaxFps.Should().Be(60);
        opts.MaxBitrateMbps.Should().Be(8);
        opts.Width.Should().Be(0);
        opts.Height.Should().Be(0);
        opts.DisableAudio.Should().BeFalse();
        opts.ShowTouches.Should().BeTrue();
        opts.StayAwake.Should().BeTrue();
        opts.Clipboard.Should().BeTrue();
    }

    [Fact]
    public void ToArguments_AlwaysIncludesCoreFlags()
    {
        var args = ScrcpyOptions.Default.ToArguments();

        args.Should().Contain(a => a.StartsWith("--max-fps="));
        args.Should().Contain(a => a.StartsWith("--bit-rate="));
        args.Should().Contain(a => a.StartsWith("--show-touches="));
        args.Should().Contain(a => a.StartsWith("--stay-awake="));
        args.Should().Contain(a => a.StartsWith("--no-clipboard="));
        args.Should().Contain(a => a.StartsWith("--no-audio="));
    }

    [Fact]
    public void ToArguments_OmitsVideoEncoderWhenNotSet()
    {
        ScrcpyOptions.Default.ToArguments()
            .Should().NotContain(a => a.StartsWith("--video-codec="));
    }

    [Fact]
    public void ToArguments_IncludesVideoEncoderWhenSet()
    {
        var args = new ScrcpyOptions { VideoEncoder = "OMX.qcom.video.encoder.avc" }.ToArguments();

        args.Should().Contain("--video-codec=OMX.qcom.video.encoder.avc");
    }

    [Fact]
    public void ToArguments_RendersBooleansAsStrings()
    {
        var args = new ScrcpyOptions { ShowTouches = false, StayAwake = true }.ToArguments();

        args.Should().Contain("--show-touches=false");
        args.Should().Contain("--stay-awake=true");
    }

    [Fact]
    public void ToClientArguments_ThrowsOnEmptySerial()
    {
        var action = () => ScrcpyOptions.Default.ToClientArguments(string.Empty);
        action.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ToClientArguments_IncludesTargetSerialAndTitle()
    {
        var args = ScrcpyOptions.Default.ToClientArguments("TEST1234", "My Phone");

        args.Should().ContainInOrder("-s", "TEST1234");
        args.Should().Contain("--window-title=My Phone");
        args.Should().Contain("--max-fps=60");
        args.Should().Contain("--video-bit-rate=8M");
        args.Should().Contain("--stay-awake");
        args.Should().Contain("--show-touches");
    }

    [Fact]
    public void ToClientArguments_HandlesTurnScreenOffAndFullscreen()
    {
        var opts = new ScrcpyOptions
        {
            TurnScreenOff = true,
            Fullscreen = true,
            AlwaysOnTop = true,
            DisableAudio = true,
            Width = 1080,
            Height = 1080,
        };

        var args = opts.ToClientArguments("DEV99");

        args.Should().Contain("--turn-screen-off");
        args.Should().Contain("--fullscreen");
        args.Should().Contain("--always-on-top");
        args.Should().Contain("--no-audio");
        args.Should().Contain("--max-size=1080");
    }
}