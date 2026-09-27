using System.Windows.Controls;

namespace Riders.Mirroring.Desktop.Views;

/// <summary>
/// "Wireless Host" pane. Renders the QR-code pairing flow driven by
/// <see cref="ViewModels.WirelessHostViewModel"/>.
/// </summary>
public partial class WirelessHostView : UserControl
{
    public WirelessHostView()
    {
        InitializeComponent();

        // Pull the IAdbServerManager that App.OnStartup registered so we
        // can talk to ADB without taking a hard dependency on the singleton
        // from the view layer.
        DataContext = new ViewModels.WirelessHostViewModel(AppServices.Require<Core.Adb.IAdbServerManager>());

        // Keep the VM password in sync with the PasswordBox control — WPF
        // PasswordBox doesn't bind safely so we hook the change event and
        // read the new value directly from the control. We route through
        // SetPassword so the VM never exposes the value through INPC.
        PasswordBox.PasswordChanged += (_, _) =>
        {
            if (DataContext is ViewModels.WirelessHostViewModel vm)
            {
                vm.SetPassword(PasswordBox.Password);
            }
        };
    }
}