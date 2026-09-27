using System.Windows;
using System.Windows.Controls;
using Riders.Mirroring.Desktop.Theming;
using Riders.Mirroring.Desktop.ViewModels;

namespace Riders.Mirroring.Desktop;

/// <summary>
/// Root shell window. All real behaviour lives on the data-bound
/// <see cref="ViewModels.MainViewModel"/>; this code-behind just validates
/// that the theme service has been registered at app startup, wires the
/// window-control buttons, the device-search text box, and tears down the
/// data-bound services when the window closes.
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();

        // Fail fast if App.OnStartup forgot to register the theme service.
        // Catching this here makes debugging much easier than waiting for a
        // NullReferenceException deep inside the Settings view-model.
        _ = AppServices.Require<IThemeService>();

        // The data context is a view-model that holds long-lived services
        // (AdbServerManager, AirPlayReceiverProcess, DeviceFeedService).
        // WPF doesn't natively call Dispose on the DataContext when the
        // window closes, so we do it ourselves here — otherwise the ADB
        // watcher keeps polling and the AirPlay process keeps listening
        // for a few seconds after the user closed the window.
        Closed += OnClosed;
    }

    private void OnClosed(object? sender, System.EventArgs e)
    {
        if (DataContext is System.IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // Disposal must never throw, especially during shutdown.
            }
        }
    }

    // ── Top-bar window controls ───────────────────────────────────────

    private void OnMinimiseClick(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    // ── Sidebar device search ─────────────────────────────────────────

    private void OnDeviceSearchChanged(object sender, TextChangedEventArgs e)
    {
        if (DataContext is MainViewModel vm && sender is TextBox box)
        {
            vm.DeviceFilter = box.Text ?? string.Empty;
        }
    }
}
