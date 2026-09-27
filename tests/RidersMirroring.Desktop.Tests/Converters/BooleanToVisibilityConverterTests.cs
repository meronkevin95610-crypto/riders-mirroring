using System.Globalization;
using System.Windows;
using FluentAssertions;
using Riders.Mirroring.Desktop.Converters;

namespace Riders.Mirroring.Desktop.Tests.Converters;

public class BooleanToVisibilityConverterTests
{
    private readonly BooleanToVisibilityConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsVisible()
    {
        _converter.Convert(true, typeof(Visibility), null!, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Visible);
    }

    [Fact]
    public void Convert_False_ReturnsCollapsed()
    {
        _converter.Convert(false, typeof(Visibility), null!, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Convert_Null_ReturnsCollapsed()
    {
        _converter.Convert(null, typeof(Visibility), null!, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Convert_NonBool_ReturnsCollapsed()
    {
        _converter.Convert("hello", typeof(Visibility), null!, CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Convert_True_WithInvert_ReturnsCollapsed()
    {
        _converter.Convert(true, typeof(Visibility), "invert", CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Collapsed);
    }

    [Fact]
    public void Convert_False_WithInvert_ReturnsVisible()
    {
        _converter.Convert(false, typeof(Visibility), "INVERT", CultureInfo.InvariantCulture)
            .Should().Be(Visibility.Visible);
    }

    [Theory]
    [InlineData(Visibility.Visible, true)]
    [InlineData(Visibility.Collapsed, false)]
    [InlineData(Visibility.Hidden, false)]
    public void ConvertBack_ReturnsExpectedBool(Visibility input, bool expected)
    {
        _converter.ConvertBack(input, typeof(bool), null!, CultureInfo.InvariantCulture)
            .Should().Be(expected);
    }
}
