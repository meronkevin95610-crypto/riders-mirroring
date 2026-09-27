using System.Globalization;
using System.Windows.Data;
using Riders.Mirroring.Desktop.ViewModels;

namespace Riders.Mirroring.Desktop.Converters;

/// <summary>
/// Multi-value converter used by the sidebar navigation RadioButtons.
/// Compares the current <see cref="AppView"/> (first binding, sourced
/// from the Window DataContext) to the per-item <see cref="AppView"/>
/// (second binding, sourced from the RadioButton's <c>Tag</c>) and
/// returns <c>true</c> when they match.
/// </summary>
/// <remarks>
/// We use a MultiBinding here because WPF forbids using a <c>{Binding}</c>
/// as <c>ConverterParameter</c> — only static values are allowed there.
/// Driving both inputs through <c>MultiBinding</c> sidesteps the
/// "Binding cannot be set on a Binding" XamlParseException that would
/// otherwise cascade into a MeasureOverride loop on startup.
/// </remarks>
[ValueConversion(typeof(AppView), typeof(bool))]
public sealed class AppViewMatchConverter : IMultiValueConverter
{
    public object Convert(object[]? values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values is null || values.Length < 2)
        {
            return false;
        }

        return values[0] is AppView current
            && values[1] is AppView tag
            && current == tag;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
    {
        // ConvertBack is unused because IsChecked flows one-way from the
        // SelectedView property; navigation commands drive SelectedView
        // via their CommandParameter.
        return new object[] { Binding.DoNothing, Binding.DoNothing };
    }
}
