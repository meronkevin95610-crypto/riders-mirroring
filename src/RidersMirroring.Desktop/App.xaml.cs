using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.AirPlay;
using Riders.Mirroring.Core.Logging;
using Riders.Mirroring.Desktop.Theming;
using Riders.Mirroring.Desktop.ViewModels;

namespace Riders.Mirroring.Desktop;

/// <summary>
/// WPF application entry point. Constructs the <see cref="ThemeService"/>
/// singleton (which loads the persisted dark/light + preset choice) and
/// the <see cref="AdbServerManager"/> that powers the device feed, then
/// wires everything into <see cref="MainViewModel"/>.
/// </summary>
public partial class App : Application
{
    private ILogger? _logger;

    public IThemeService Theme { get; private set; } = null!;
    public IAdbServerManager Adb { get; private set; } = null!;
    public IAirPlayReceiver AirPlay { get; private set; } = null!;
    public Riders.Mirroring.Core.Scrcpy.IScrcpyMirrorService Mirror { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Hook the global handlers first so any startup exception is logged
        // and surfaced to the user instead of vanishing into a crash dialog.
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        base.OnStartup(e);

        try
        {
            // Note: the actual lock to 1600x900 @ 160 DPI comes from
            // app.manifest (dpiAwareness=PerMonitorV2) + the explicit
            // Width/Height in MainWindow.xaml. We do not touch
            // HwndTarget.TransformToDevice here because it's a read-only
            // property that we can only influence by picking the right
            // DPI awareness mode — which the manifest does.
            Theme = new ThemeService();
            Theme.ApplyTo(this);

            Adb = new AdbServerManager();
            AirPlay = new AirPlayNativeServer();
            Mirror = new Riders.Mirroring.Core.Scrcpy.ScrcpyMirrorProcess();

            // Expose shared services to child views via the AppServices locator.
            AppServices.Register<IThemeService>(Theme);
            AppServices.Register<IAirPlayReceiver>(AirPlay);
            AppServices.Register<Riders.Mirroring.Core.Scrcpy.IScrcpyMirrorService>(Mirror);
            AppServices.Register<IAdbServerManager>(Adb);

            var mainWindow = new MainWindow
            {
                DataContext = new MainViewModel(Theme, Adb, AirPlay, Mirror),
            };

            // Adapt the window size to whatever screen the user is on so we
            // don't overflow on a 1366x768 laptop or leave big black borders
            // on a 2560x1440 monitor. We target a 1600x900 design surface
            // (16:9, matches the Dofus Touch mockup) and shrink to fit when
            // the primary screen is smaller than that.
            FitWindowToPrimaryScreen(mainWindow, designWidth: 1600, designHeight: 900);

            MainWindow = mainWindow;
            mainWindow.Show();

            // Fire-and-forget: warm up ADB once the UI is up.
            _logger = RidersLogger.Create<App>();
            _logger.LogInformation("Riders Mirroring starting up");
        }
        catch (Exception ex)
        {
            // Ensure the logger is built even if construction above threw —
            // we want the user to find the stack trace in riders-debug.log.
            try
            {
                RidersLogger.EnsureFactory();
                RidersLogger.Create<App>().LogCritical(ex, "Fatal error during startup");
            }
            catch
            {
                // If even the logger can't initialise (sandboxed %LOCALAPPDATA%),
                // fall back to stderr so the crash is at least visible in CI logs.
                Console.Error.WriteLine($"FATAL during startup: {ex}");
            }

            MessageBox.Show(
                "Riders Mirroring failed to start.\r\n\r\n" +
                $"See %LOCALAPPDATA%\\Riders Mirroring\\riders-debug.log for details.\r\n\r\n{ex.Message}",
                "Riders Mirroring",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            // Flush Serilog so any in-flight log line lands on disk before the
            // process exits — otherwise the user loses the very last lines
            // that often contain the actual crash context.
            RidersLogger.Shutdown();
        }
        catch
        {
            // Never throw from OnExit — the framework may already be tearing down.
        }

        base.OnExit(e);
    }

    /// <summary>
    /// Shrink the window to fit the primary screen if it's smaller than the
    /// design surface (1600x900). The design uses a 16:9 mockup so we also
    /// preserve that aspect ratio when downscaling. The window is centred
    /// via <see cref="WindowStartupLocation.CenterScreen"/>.
    /// </summary>
    private static void FitWindowToPrimaryScreen(Window window, double designWidth, double designHeight)
    {
        var workArea = SystemParameters.WorkArea;
        const double margin = 40; // px of breathing room around the window

        var maxWidth = Math.Max(designWidth * 0.6, workArea.Width - margin * 2);
        var maxHeight = Math.Max(designHeight * 0.6, workArea.Height - margin * 2);

        // If the screen is smaller than 1600x900, scale down preserving the
        // 16:9 aspect ratio so the layout never overflows the screen.
        if (workArea.Width < designWidth || workArea.Height < designHeight)
        {
            var widthRatio = (workArea.Width - margin * 2) / designWidth;
            var heightRatio = (workArea.Height - margin * 2) / designHeight;
            var scale = Math.Min(widthRatio, heightRatio);
            window.Width = Math.Max(960, designWidth * scale);
            window.Height = Math.Max(540, designHeight * scale);
        }

        // Clamp to the work area in case the screen is bigger but DPI scaling
        // is unusual (e.g. 4K at 200% = effective 1920x1080).
        if (window.Width > maxWidth) window.Width = maxWidth;
        if (window.Height > maxHeight) window.Height = maxHeight;
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            RidersLogger.EnsureFactory();
            // Write the exception details to a side file so we can see the
            // actual message even if Serilog's structured formatter collapses
            // it (the {Exception} template was previously rendering as an
            // empty `{}` in riders-debug.log).
            var dumpPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Riders Mirroring",
                "unhandled-exceptions.log");
            System.IO.File.AppendAllText(dumpPath,
                $"[{DateTime.UtcNow:O}] {e.Exception}\r\n----\r\n");
            RidersLogger.Create<App>().LogError(e.Exception, "Unhandled dispatcher exception");
        }
        catch
        {
            Console.Error.WriteLine($"UNHANDLED: {e.Exception}");
        }

        MessageBox.Show(
            "An unexpected error occurred.\r\n\r\n" +
            $"Details: {e.Exception.Message}\r\n\r\n" +
            "The log file at %LOCALAPPDATA%\\Riders Mirroring\\riders-debug.log has more information.",
            "Riders Mirroring",
            MessageBoxButton.OK,
            MessageBoxImage.Error);

        // Mark the exception as handled so the process keeps running.
        e.Handled = true;
    }

    private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        try
        {
            RidersLogger.EnsureFactory();
            RidersLogger.Create<App>().LogCritical(
                e.ExceptionObject as Exception,
                "Fatal AppDomain exception (terminating={Terminating})",
                e.IsTerminating);
        }
        catch
        {
            Console.Error.WriteLine($"FATAL AppDomain: {e.ExceptionObject}");
        }
    }
}
