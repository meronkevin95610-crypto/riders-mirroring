using System.Globalization;
using System.Windows.Data;

namespace Riders.Mirroring.Desktop.Converters;

/// <summary>
/// Maps an <see cref="ViewModels.AppView"/> value to a Segoe Fluent Icons /
/// Segoe MDL2 Assets glyph used in the sidebar nav buttons.
/// </summary>
/// <remarks>
/// Glyphs chosen to be visually consistent with the Windows 11 Settings app:
/// <list type="bullet">
///   <item>Mirror: <c>&#xE8AB;</c> (Connect)</item>
///   <item>AirPlay: <c>&#xE93C;</c> (Project)</item>
///   <item>WirelessHost: <c>&#xE701;</c> (Wifi)</item>
///   <item>Settings: <c>&#xE713;</c> (Settings)</item>
///   <item>About: <c>&#xE946;</c> (Info)</item>
/// </list>
/// Falls back to an empty glyph for unknown values.
/// </remarks>
[ValueConversion(typeof(ViewModels.AppView), typeof(string))]
public sealed class AppViewIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ViewModels.AppView view)
        {
            return string.Empty;
        }

        return view switch
        {
            ViewModels.AppView.Mirror       => "\uE8AB", // Connect / Miracast
            ViewModels.AppView.AirPlay      => "\uE93C", // Project to second screen
            ViewModels.AppView.WirelessHost => "\uE701", // Wifi
            ViewModels.AppView.Settings     => "\uE713", // Settings
            ViewModels.AppView.About        => "\uE946", // Info
            _ => string.Empty,
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}