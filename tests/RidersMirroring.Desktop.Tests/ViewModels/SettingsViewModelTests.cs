using System.Windows;
using FluentAssertions;
using Riders.Mirroring.Desktop.Theming;
using Riders.Mirroring.Desktop.ViewModels;

namespace Riders.Mirroring.Desktop.Tests.ViewModels;

/// <summary>
/// Tests <see cref="SettingsViewModel"/> against an in-memory <see cref="IThemeService"/>.
/// We keep the theme service real (just ephemeral) — the VM's contract is about
/// forwarding user choices to it, not about persistence.
/// </summary>
public class SettingsViewModelTests
{
    [Fact]
    public void Constructor_SeedsSelectedPreset_FromThemeService()
    {
        var theme = new FakeThemeService(ThemePreset.Ocean);
        var vm = new SettingsViewModel(theme);

        vm.SelectedPreset.Should().Be(ThemePreset.Ocean);
        vm.AvailablePresets.Should().NotBeEmpty();
    }

    [Fact]
    public void Constructor_Throws_OnNullTheme()
    {
        Action act = () => new SettingsViewModel(null!);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void SelectPreset_UpdatesThemeService()
    {
        var theme = new FakeThemeService(ThemePreset.VioletPink);
        var vm = new SettingsViewModel(theme);

        vm.SelectPresetCommand.Execute(ThemePreset.Forest);

        theme.Preset.Should().Be(ThemePreset.Forest);
        vm.SelectedPreset.Should().Be(ThemePreset.Forest);
    }

    [Fact]
    public void ToggleDarkMode_TogglesTheme()
    {
        var theme = new FakeThemeService(ThemePreset.Ocean);
        theme.Theme = AppTheme.Dark;

        var vm = new SettingsViewModel(theme);
        vm.DarkModeLabel.Should().Be("Switch to light mode");

        vm.ToggleDarkModeCommand.Execute(null);

        theme.Theme.Should().Be(AppTheme.Light);
        vm.DarkModeLabel.Should().Be("Switch to dark mode");
    }

    [Fact]
    public void SelectPresetCommand_WithNull_Throws()
    {
        var theme = new FakeThemeService(ThemePreset.Ocean);
        var vm = new SettingsViewModel(theme);

        Action act = () => vm.SelectPresetCommand.Execute(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

/// <summary>
/// Minimal in-memory IThemeService for unit tests. Persistence is exercised
/// separately by the ThemeService itself.
/// </summary>
internal sealed class FakeThemeService : IThemeService
{
    public FakeThemeService(ThemePreset preset)
    {
        Preset = preset;
    }

    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public ThemePreset Preset { get; set; }
    public bool IsDark => Theme == AppTheme.Dark;

    public event EventHandler? Changed
    {
        add { /* no-op */ }
        remove { /* no-op */ }
    }

    public void ApplyTo(Application app)
    {
        // no-op
    }
}
