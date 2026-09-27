namespace Riders.Mirroring.Desktop.Theming;

/// <summary>
/// Read/write contract on the active theme, consumed by view-models. The
/// concrete implementation is <see cref="ThemeService"/>.
/// </summary>
public interface IThemeService
{
    /// <summary>Current light/dark setting.</summary>
    AppTheme Theme { get; set; }

    /// <summary>Current accent-colour preset.</summary>
    ThemePreset Preset { get; set; }

    /// <summary>Convenience flag derived from <see cref="Theme"/>.</summary>
    bool IsDark { get; }

    /// <summary>
    /// Fires after <see cref="Theme"/> or <see cref="Preset"/> has been applied
    /// to the running application. Subscribers should refresh any UI that
    /// binds to non-<c>DynamicResource</c> resources.
    /// </summary>
    event EventHandler? Changed;

    /// <summary>
    /// Apply the current <see cref="Theme"/> and <see cref="Preset"/> to the
    /// supplied <see cref="Application"/> instance. Safe to call from
    /// <c>App.OnStartup</c>.
    /// </summary>
    void ApplyTo(System.Windows.Application app);
}