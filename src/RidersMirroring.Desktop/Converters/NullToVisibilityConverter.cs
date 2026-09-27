using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Riders.Mirroring.Desktop.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the supplied value is
/// not <c>null</c> and not an empty string; <see cref="Visibility.Collapsed"/>
/// otherwise.
/// </summary>
/// <remarks>
/// Inverse of <see cref="NullOrEmptyToVisibilityConverter"/>. Used to
/// show or hide a panel depending on whether a backing resource was
/// successfully loaded (the scrcpy logo on the About page is the
/// canonical example — when the bundle is missing, the panel collapses
/// instead of rendering a broken image icon).
/// </remarks>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var visible = value switch
        {
            null => false,
            string s => !string.IsNullOrEmpty(s),
            _ => true,
        };

        if (parameter is string p && string.Equals(p, "invert", StringComparison.OrdinalIgnoreCase))
        {
            visible = !visible;
        }

        return visible ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}