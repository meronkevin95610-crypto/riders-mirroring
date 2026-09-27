using System.Globalization;
using System.Windows;
using FluentAssertions;
using Riders.Mirroring.Desktop.Converters;

namespace Riders.Mirroring.Desktop.Tests.Converters;

public class NullOrEmptyToVisibilityConverterTests
{
    private readonly NullOrEmptyToVisibilityConverter _converter = new();

    [Theory]
    [InlineData(null,    Visibility.Visible)]
    [InlineData("",      Visibility.Visible)]
    public void Convert_NullOrEmpty_ReturnsVisible(string? input, Visibility expected)
    {
        _converter.Convert(input, typeof(Visibility), null!, CultureInfo.InvariantCulture)
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("hello",  Visibility.Collapsed)]
    [InlineData(" ",      Visibility.Collapsed)]   // whitespace is not empty
    public void Convert_NonEmpty_ReturnsCollapsed(string input, Visibility expected)
    {
        _converter.Convert(input, typeof(Visibility), null!, CultureInfo.InvariantCulture)
            .Should().Be(expected);
    }

    [Fact]
    public void Convert_NonString_ReturnsVisible()
    {
        // Treat anything that's not a non-empty string as "empty"
        _converter.Convert(42, typeof(Visibility), null!, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Visible);
    }

    [Fact]
    public void ConvertBack_Throws()
    {
        Action act = () => _converter.ConvertBack(Visibility.Visible, typeof(string), null!, CultureInfo.InvariantCulture);
        act.Should().Throw<NotSupportedException>();
    }
}
