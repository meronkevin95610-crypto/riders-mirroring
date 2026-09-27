using System.Globalization;
using FluentAssertions;
using Riders.Mirroring.Desktop.Converters;

namespace Riders.Mirroring.Desktop.Tests.Converters;

public class InverseBooleanConverterTests
{
    private readonly InverseBooleanConverter _converter = new();

    [Fact]
    public void Convert_True_ReturnsFalse()
    {
        _converter.Convert(true, typeof(bool), null!, CultureInfo.InvariantCulture)
            .Should().Be(false);
    }

    [Fact]
    public void Convert_False_ReturnsTrue()
    {
        _converter.Convert(false, typeof(bool), null!, CultureInfo.InvariantCulture)
            .Should().Be(true);
    }

    [Fact]
    public void Convert_Null_ReturnsFalse()
    {
        // null is treated as "not a true bool" so `value is bool b && !b` is false.
        _converter.Convert(null, typeof(bool), null!, CultureInfo.InvariantCulture)
            .Should().Be(false);
    }

    [Fact]
    public void Convert_NonBool_ReturnsFalse()
    {
        _converter.Convert("anything", typeof(bool), null!, CultureInfo.InvariantCulture)
            .Should().Be(false);
    }

    [Fact]
    public void ConvertBack_True_ReturnsFalse()
    {
        _converter.ConvertBack(true, typeof(bool), null!, CultureInfo.InvariantCulture)
            .Should().Be(false);
    }

    [Fact]
    public void ConvertBack_False_ReturnsTrue()
    {
        _converter.ConvertBack(false, typeof(bool), null!, CultureInfo.InvariantCulture)
            .Should().Be(true);
    }
}
