namespace Riders.Mirroring.Desktop.Theming;

/// <summary>
/// Which colour set / palette the UI is rendered with. The Dofus Touch
/// theme is dark-by-nature but has its own dedicated dictionary because
/// every brush (backgrounds, text, accent) is replaced — not just the
/// light/dark toggle.
/// </summary>
public enum AppTheme
{
    Dark,
    Light,
    DofusTouch,
}