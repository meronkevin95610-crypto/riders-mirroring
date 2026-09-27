using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using Riders.Mirroring.Desktop.Theming;

namespace Riders.Mirroring.Desktop.Theming;

/// <summary>
/// Converts a hex colour string (e.g. <c>"#7F77DD"</c>) to a WPF
/// <see cref="SolidColorBrush"/>. Singleton — exposed as a static
/// <see cref="Instance"/> so XAML can reference it without instantiating a
/// resource dictionary entry.
/// </summary>
[ValueConversion(typeof(string), typeof(SolidColorBrush))]
public sealed class HexToBrushConverter : IValueConverter
{
    public static readonly HexToBrushConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrEmpty(hex))
        {
            return BrushFactory.FromHex(hex);
        }

        return System.Windows.Media.Brushes.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}