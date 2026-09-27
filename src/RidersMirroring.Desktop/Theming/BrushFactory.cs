using System.Windows.Media;

namespace Riders.Mirroring.Desktop.Theming;

/// <summary>
/// Small helper that converts a hex colour string into a WPF
/// <see cref="SolidColorBrush"/>. Pulled out of <see cref="ThemeService"/>
/// because <see cref="System.Windows.Media.Brush"/> is a WindowsBase type and
/// keeping the helper here keeps the service focused on persistence + dict
/// swapping.
/// </summary>
internal static class BrushFactory
{
    public static SolidColorBrush FromHex(string hex)
    {
        var color = (Color)ColorConverter.ConvertFromString(hex)!;
        return new SolidColorBrush(color);
    }
}