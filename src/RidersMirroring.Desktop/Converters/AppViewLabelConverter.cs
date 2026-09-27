using System.Globalization;
using System.Windows.Data;
using Riders.Mirroring.Desktop.ViewModels;

namespace Riders.Mirroring.Desktop.Converters;

/// <summary>
/// Converts an <see cref="AppView"/> value into the human-readable label
/// shown in the bottom navigation strip.
/// </summary>
public sealed class AppViewLabelConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AppView view)
        {
            return AppViewLabels.For(view);
        }

        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // One-way binding only — the buttons take AppView as CommandParameter.
        throw new NotSupportedException();
    }
}
