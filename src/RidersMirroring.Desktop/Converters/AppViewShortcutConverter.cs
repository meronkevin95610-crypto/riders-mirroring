using System.Globalization;
using System.Windows.Data;
using Riders.Mirroring.Desktop.ViewModels;

namespace Riders.Mirroring.Desktop.Converters;

/// <summary>
/// Maps an <see cref="AppView"/> value to the keyboard shortcut label
/// displayed in the bottom nav rail ("⌘1", "⌘2", ...).
/// </summary>
public sealed class AppViewShortcutConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AppView view)
        {
            // Keep in sync with AvailableViews ordering in MainViewModel.
            return view switch
            {
                AppView.Mirror       => "\u23181",
                AppView.AirPlay      => "\u23182",
                AppView.WirelessHost => "\u23183",
                AppView.Settings     => "\u23184",
                AppView.About        => "\u23185",
                _                    => string.Empty,
            };
        }

        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
