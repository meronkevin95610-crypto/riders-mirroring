using System.Windows.Controls;
using Riders.Mirroring.Desktop.ViewModels;

namespace Riders.Mirroring.Desktop.Views;

/// <summary>
/// "About" pane. Bound to <see cref="AboutViewModel"/> which exposes the
/// assembly version, product metadata, and the list of redistributed
/// third-party components.
/// </summary>
public partial class AboutView : UserControl
{
    public AboutView()
    {
        InitializeComponent();
        DataContext = new AboutViewModel();
    }
}