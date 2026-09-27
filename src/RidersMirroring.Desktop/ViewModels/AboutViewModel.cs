using System.Diagnostics;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Riders.Mirroring.Desktop.ViewModels;

/// <summary>
/// View-model for the <c>AboutView</c> pane. Exposes version information
/// read from the entry assembly and opens external URLs for the project
/// home page and the licences folder.
/// </summary>
public sealed partial class AboutViewModel : ObservableObject
{
    private static readonly Assembly _assembly =
        Assembly.GetEntryAssembly() ?? typeof(AboutViewModel).Assembly;

    // ── Product identity ────────────────────────────────────────────────────

    /// <summary>Human-readable version string shown in the pane header.</summary>
    public string VersionString { get; } = BuildVersionString();

    /// <summary>Full product name.</summary>
    public string ProductName { get; } = "Riders Mirroring";

    /// <summary>Short copyright blurb.</summary>
    public string Copyright { get; } = BuildCopyright();

    /// <summary>Short one-line description of what this app does.</summary>
    public string Description { get; } =
        "Mirror Android & iPhone, side by side — powered by scrcpy and UxPlay.";

    /// <summary>
    /// Source for the bundled Riders Mirroring logo PNG, exposed as a
    /// <c>pack://</c> URI so WPF can resolve it from the assembly's
    /// manifest resources without needing the file on disk. Returns
    /// <c>null</c> if the asset was stripped from the publish output
    /// (the view binds with <c>FallbackValue</c> and silently hides itself).
    /// </summary>
    public System.Windows.Media.ImageSource? AppLogoSource { get; } =
        LoadResourceImage("Resources/Branding/riders-mirroring-logo.png");

    private static System.Windows.Media.ImageSource? LoadResourceImage(string relativePath)
    {
        try
        {
            var uri = new System.Uri(
                $"pack://application:,,,/{typeof(AboutViewModel).Assembly.GetName().Name};component/{relativePath}",
                System.UriKind.Absolute);
            var streamInfo = System.Windows.Application.GetResourceStream(uri);
            if (streamInfo?.Stream is null)
            {
                return null;
            }

            var image = new System.Windows.Media.Imaging.BitmapImage();
            image.BeginInit();
            image.StreamSource = streamInfo.Stream;
            image.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
            image.EndInit();
            image.Freeze(); // cross-thread safe
            return image;
        }
        catch
        {
            return null;
        }
    }

    // ── Third-party component list (shown in the licence card) ───────────────

    /// <summary>Ordered list of redistributed components and their licences.</summary>
    public IReadOnlyList<ThirdPartyComponent> Components { get; } =
    [
        new("scrcpy",                 "Apache 2.0", "https://github.com/Genymobile/scrcpy",         "Server JAR pushed to the Android device."),
        new("AdvancedSharpAdbClient", "MIT",        "https://github.com/SharpAdb/AdvancedSharpAdbClient", "Managed ADB client — no adb.exe spawn."),
        new("UxPlay",                 "GPLv3",      "https://github.com/FDH2/UxPlay",               "Spawned as an external process; not linked."),
        new("FFmpeg",                 "LGPL 2.1",   "https://ffmpeg.org/",                          "H.264 decoding; redistributed as unmodified DLLs."),
        new("QRCoder",                "MIT",        "https://github.com/codebude/QRCoder",          "QR code rendering for wireless-adb pairing."),
        new("CommunityToolkit.Mvvm",  "MIT",        "https://github.com/CommunityToolkit/dotnet",   "MVVM helpers (ObservableObject, RelayCommand)."),
        new("Serilog",                "Apache 2.0", "https://serilog.net/",                         "Structured logging with daily JSON file sink."),
    ];

    // ── Commands ─────────────────────────────────────────────────────────────

    /// <summary>Open the project GitHub repository in the default browser.</summary>
    [RelayCommand]
    private static void OpenProjectUrl()
        => TryOpenUrl("https://github.com/your-org/riders-mirroring");

    /// <summary>Open the licences folder next to the executable.</summary>
    [RelayCommand]
    private static void OpenLicencesFolder()
    {
        var dir = System.IO.Path.Combine(AppContext.BaseDirectory, "licenses");
        if (System.IO.Directory.Exists(dir))
        {
            Process.Start("explorer.exe", dir);
        }
        else
        {
            MessageBox.Show(
                $"The licences folder was not found at:\n{dir}",
                "Riders Mirroring",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
    }

    /// <summary>Open the URL for a specific component.</summary>
    [RelayCommand]
    private static void OpenComponentUrl(ThirdPartyComponent? component)
    {
        if (component is null) return;
        TryOpenUrl(component.Url);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static void TryOpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch
        {
            // Silently ignore — the user can always copy the URL manually.
        }
    }

    private static string BuildVersionString()
    {
        var version = _assembly.GetName().Version;
        if (version is null) return "1.0.0";
        // Omit the revision component (last segment) when it is zero.
        return version.Revision == 0
            ? $"{version.Major}.{version.Minor}.{version.Build}"
            : version.ToString();
    }

    private static string BuildCopyright()
    {
        var attr = _assembly.GetCustomAttribute<AssemblyCopyrightAttribute>();
        return attr?.Copyright ?? $"© {DateTime.Now.Year} Riders Corp. Released under the MIT Licence.";
    }
}

/// <summary>
/// One row in the third-party components table shown in <c>AboutView</c>.
/// </summary>
/// <param name="Name">Component display name.</param>
/// <param name="Licence">Short SPDX-style licence identifier.</param>
/// <param name="Url">Project home-page URL.</param>
/// <param name="Usage">One-line description of how we use it.</param>
public sealed record ThirdPartyComponent(
    string Name,
    string Licence,
    string Url,
    string Usage);
