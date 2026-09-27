using System.Windows.Controls;
using Riders.Mirroring.Desktop.Theming;
using Riders.Mirroring.Desktop.ViewModels;

namespace Riders.Mirroring.Desktop.Views;

/// <summary>
/// Code-behind for the Settings pane. Pulls the shared theme service from
/// the <see cref="AppServices"/> locator populated in <c>App.OnStartup</c>.
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();

        DataContext = new SettingsViewModel(AppServices.Require<IThemeService>());
    }
    private void OnToggleDarkClick(object sender, System.Windows.RoutedEventArgs e)
    {
        if (DataContext is SettingsViewModel vm && vm.ToggleDarkModeCommand.CanExecute(null))
        {
            vm.ToggleDarkModeCommand.Execute(null);
        }
    }
}
