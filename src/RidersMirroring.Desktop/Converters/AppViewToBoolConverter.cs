using System.Globalization;
using System.Windows.Data;

namespace Riders.Mirroring.Desktop.Converters;

/// <summary>
/// Two-way binding helper that flips a <see cref="bool"/> (for
/// <c>RadioButton.IsChecked</c>) based on whether the bound value equals
/// the RadioButton's <c>Tag</c> (an <see cref="ViewModels.AppView"/>).
/// </summary>
/// <remarks>
/// Usage:
/// <code>
/// &lt;RadioButton Tag="{Binding}"
///               IsChecked="{Binding DataContext.SelectedView,
///                                  RelativeSource={RelativeSource AncestorType=Window},
///                                  Converter={StaticResource AppViewToBoolConverter}}"
///               CommandParameter="{Binding}" /&gt;
/// </code>
/// We bind the view item (the per-RadioButton AppView) into <c>Tag</c>
/// and pass <c>Tag</c> via <c>ConverterParameter</c> using a static
/// <see cref="Binding"/> to the TemplatedParent's Tag — <c>{Binding Tag,
/// RelativeSource={RelativeSource Self}}</c>. This avoids the WPF
/// "Binding cannot be set on a Binding" exception that triggers when
/// someone tries <c>ConverterParameter="{Binding}"</c>.
/// </remarks>
[ValueConversion(typeof(ViewModels.AppView), typeof(bool))]
public sealed class AppViewToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ViewModels.AppView current || parameter is not ViewModels.AppView target)
        {
            return false;
        }

        return current == target;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isChecked && isChecked && parameter is ViewModels.AppView target)
        {
            return target;
        }

        return Binding.DoNothing;
    }
}