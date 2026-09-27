using FluentAssertions;
using Riders.Mirroring.Desktop.ViewModels;

namespace Riders.Mirroring.Desktop.Tests.ViewModels;

public class AppViewLabelsTests
{
    [Theory]
    [InlineData(AppView.Mirror,       "Mirror")]
    [InlineData(AppView.AirPlay,      "AirPlay")]
    [InlineData(AppView.WirelessHost, "Wireless")]
    [InlineData(AppView.Settings,     "Settings")]
    [InlineData(AppView.About,        "About")]
    public void For_ReturnsExpectedLabel(AppView view, string expected)
    {
        AppViewLabels.For(view).Should().Be(expected);
    }

    [Fact]
    public void For_FallsBackToToString_OnUnknownEnumValue()
    {
        // Defensive: if a future enum value isn't labelled, return its name.
        var name = AppViewLabels.For((AppView)9999);
        name.Should().Be(((AppView)9999).ToString());
    }
}
