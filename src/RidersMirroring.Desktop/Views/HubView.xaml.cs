using System.Windows.Controls;

namespace Riders.Mirroring.Desktop.Views;

/// <summary>
/// Chrome-style multi-instance mirror hub. Renders one tab per active
/// mirroring session and the selected tab's live frame in the centre pane.
/// </summary>
public partial class HubView : UserControl
{
    public HubView()
    {
        InitializeComponent();
    }
}
