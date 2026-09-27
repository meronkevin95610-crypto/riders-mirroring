using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;

namespace Riders.Mirroring.Desktop.Converters;

/// <summary>
/// Returns <see cref="Visibility.Visible"/> when the supplied value is a
/// non-null, non-empty string that points to an existing file on disk;
/// <see cref="Visibility.Collapsed"/> otherwise.
/// </summary>
/// <remarks>
/// Used by <c>AboutView.xaml</c> to gracefully hide the scrcpy logo when
/// the bundled asset is missing (e.g. trimmed publish output). The XAML
/// bind site uses <c>FallbackValue</c> to absorb the converter path
/// during design-time.
/// </remarks>
[ValueConversion(typeof(string), typeof(Visibility))]
public sealed class FileExistsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path))
        {
            return Visibility.Collapsed;
        }

        return File.Exists(path) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}