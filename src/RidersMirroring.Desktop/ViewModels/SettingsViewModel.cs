using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Riders.Mirroring.Desktop.Theming;

namespace Riders.Mirroring.Desktop.ViewModels;

/// <summary>
/// Bound to <c>SettingsView.xaml</c>. Lets the user pick an accent-colour
/// preset and toggle the dark/light theme. Changes apply live — there's no
/// "Save" button because <see cref="ThemeService"/> persists on every
/// change.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject, IDisposable
{
    private readonly IThemeService _theme;

    [ObservableProperty]
    private ThemePreset _selectedPreset;

    public IReadOnlyList<ThemePreset> AvailablePresets => ThemePreset.All;

    public SettingsViewModel(IThemeService theme)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        _selectedPreset = _theme.Preset;

        // Keep DarkModeLabel in sync whenever the theme changes from outside
        // (e.g. a second window or code calling _theme.Theme = …).
        _theme.Changed += OnThemeChanged;
    }

    partial void OnSelectedPresetChanged(ThemePreset value)
    {
        _theme.Preset = value;
    }

    [RelayCommand]
    private void ToggleDarkMode()
    {
        _theme.Theme = _theme.IsDark ? AppTheme.Light : AppTheme.Dark;
        // Notify the label immediately so the button text flips in the same frame.
        OnPropertyChanged(nameof(DarkModeLabel));
    }

    [RelayCommand]
    private void SelectPreset(ThemePreset preset)
    {
        ArgumentNullException.ThrowIfNull(preset);
        SelectedPreset = preset;
    }

    /// <summary>Used by the XAML template to display the toggle label.</summary>
    public string DarkModeLabel => _theme.IsDark ? "Switch to light mode" : "Switch to dark mode";

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        OnPropertyChanged(nameof(DarkModeLabel));
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _theme.Changed -= OnThemeChanged;
    }
}