using System.Globalization;
using System.Windows;
using FluentAssertions;
using Riders.Mirroring.Desktop.Converters;

namespace Riders.Mirroring.Desktop.Tests.Converters;

public class StringToVisibilityConverterTests
{
    private readonly StringToVisibilityConverter _converter = new();

    [Theory]
    [InlineData("hello",  Visibility.Visible)]
    [InlineData(" ",      Visibility.Visible)]  // single space is not empty
    public void Convert_NonEmpty_ReturnsVisible(string input, Visibility expected)
    {
        _converter.Convert(input, typeof(Visibility), null!, CultureInfo.InvariantCulture)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData(null,    Visibility.Collapsed)]
    [InlineData("",      Visibility.Collapsed)]
    public void Convert_NullOrEmpty_ReturnsCollapsed(string? input, Visibility expected)
    {
        _converter.Convert(input, typeof(Visibility), null!, CultureInfo.InvariantCulture)
            .Should().Be(expected);
    }

    [Fact]
    public void Convert_NonEmpty_WithInvert_ReturnsCollapsed()
    {
        _converter.Convert("hello", typeof(Visibility), "invert", CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Convert_NullOrEmpty_WithInvert_ReturnsVisible()
    {
        _converter.Convert(null, typeof(Visibility), "invert", CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Visible);
    }

    [Fact]
    public void Convert_NonString_ReturnsCollapsed()
    {
        _converter.Convert(42, typeof(Visibility), null!, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void ConvertBack_Throws()
    {
        Action act = () => _converter.ConvertBack(Visibility.Visible, typeof(string), null!, CultureInfo.InvariantCulture);
        act.Should().Throw<NotSupportedException>();
    }
}
