namespace Riders.Mirroring.Desktop.Theming;

/// <summary>
/// A named palette of accent colours that the user can pick from the Settings
/// pane. Each preset maps to a triplet of brushes consumed by the WPF theme
/// dictionaries — see <c>Themes/Dark.xaml</c> and <c>Themes/Light.xaml</c>.
/// </summary>
/// <remarks>
/// Keep the list short: the goal is to give the user a few tasteful defaults
/// (violet/pink being the brand preset), not to ship a full-blown colour
/// picker. A custom picker can be layered on top later without touching the
/// presets.
/// </remarks>
public sealed record ThemePreset(
    string Id,
    string DisplayName,
    string PrimaryHex,
    string SecondaryHex,
    string AccentHex)
{
    /// <summary>The brand-default preset (violet → pink gradient).</summary>
    public static readonly ThemePreset VioletPink = new(
        Id: "violet-pink",
        DisplayName: "Violet & Pink",
        PrimaryHex: "#7F77DD",
        SecondaryHex: "#A87FE0",
        AccentHex: "#D4537E");

    /// <summary>Cool blue accent for users who prefer a calmer palette.</summary>
    public static readonly ThemePreset Ocean = new(
        Id: "ocean",
        DisplayName: "Ocean",
        PrimaryHex: "#4A8FE7",
        SecondaryHex: "#6BA9F0",
        AccentHex: "#3DBCD6");

    /// <summary>Forest green for the nature-inclined.</summary>
    public static readonly ThemePreset Forest = new(
        Id: "forest",
        DisplayName: "Forest",
        PrimaryHex: "#5DAE5D",
        SecondaryHex: "#7BC97B",
        AccentHex: "#C9B23B");

    /// <summary>High-temperature sunset (orange + red).</summary>
    public static readonly ThemePreset Sunset = new(
        Id: "sunset",
        DisplayName: "Sunset",
        PrimaryHex: "#E07B5A",
        SecondaryHex: "#F09B6B",
        AccentHex: "#D4537E");

    /// <summary>Dofus Touch theme — gold + parchment on deep purple.</summary>
    public static readonly ThemePreset DofusTouch = new(
        Id: "dofus-touch",
        DisplayName: "Dofus Touch",
        PrimaryHex: "#FFD700",
        SecondaryHex: "#D4AF37",
        AccentHex: "#B8941F");

    /// <summary>All presets shipped with the app, in display order.</summary>
    public static IReadOnlyList<ThemePreset> All { get; } = new[]
    {
        VioletPink,
        Ocean,
        Forest,
        Sunset,
        DofusTouch,
    };

    public static ThemePreset FromId(string? id) =>
        All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase))
        ?? VioletPink;
}