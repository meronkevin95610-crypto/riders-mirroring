using System.Globalization;
using System.Windows;
using FluentAssertions;
using Riders.Mirroring.Desktop.Converters;
using Riders.Mirroring.Desktop.ViewModels;

namespace Riders.Mirroring.Desktop.Tests.Converters;

public class AppViewLabelConverterTests
{
    private readonly AppViewLabelConverter _converter = new();

    [Theory]
    [InlineData(AppView.Mirror,       "Mirror")]
    [InlineData(AppView.AirPlay,      "AirPlay")]
    [InlineData(AppView.WirelessHost, "Wireless")]
    [InlineData(AppView.Settings,     "Settings")]
    [InlineData(AppView.About,        "About")]
    public void Convert_AppView_ReturnsLabel(AppView view, string expected)
    {
        _converter.Convert(view, typeof(string), null!, CultureInfo.InvariantCulture)
            .Should().Be(expected);
    }

    [Fact]
    public void Convert_NullValue_ReturnsEmptyString()
    {
        _converter.Convert(null, typeof(string), null, CultureInfo.InvariantCulture)
            .Should().Be(string.Empty);
    }

    [Fact]
    public void Convert_ArbitraryObject_FallsBackToToString()
    {
        _converter.Convert(42, typeof(string), null, CultureInfo.InvariantCulture)
            .Should().Be("42");
    }

    [Fact]
    public void ConvertBack_Throws()
    {
        Action act = () => _converter.ConvertBack("Mirror", typeof(AppView), null, CultureInfo.InvariantCulture);
        act.Should().Throw<NotSupportedException>();
    }
}
