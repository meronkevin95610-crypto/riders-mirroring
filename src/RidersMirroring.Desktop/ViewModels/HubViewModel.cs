using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.Desktop.ViewModels;

/// <summary>
/// Root view-model for the chrome-style multi-instance mirror hub.
/// Owns the collection of <see cref="MirrorTabViewModel"/> (one per
/// device or per active mirroring session) and tracks which tab is
/// currently selected.
///
/// <para>
/// Visual model (rendered by <c>HubView.xaml</c>):
/// <code>
///   +---------+---------+---------+----+
///   | Device1 | Device2 | Device3 | +  |  ← Tab strip
///   +---------+---------+---------+----+
///   |                                     |
///   |   [active tab's mirror surface]      |
///   |                                     |
///   +-------------------------------------+
/// </code>
/// </para>
/// </summary>
/// <remarks>
/// Inspired by TabDesk's tabbed interface — the user can pin one
/// mirroring session per tab and switch instantly via the chrome
/// strip, without juggling multiple top-level scrcpy windows.
/// </remarks>
public sealed partial class HubViewModel : ObservableObject, IDisposable
{
    private readonly Func<ScrcpySession, ScrcpySessionViewModel> _sessionVmFactory;

    public HubViewModel()
        : this(static session => new ScrcpySessionViewModel(session))
    {
    }

    public HubViewModel(Func<ScrcpySession, ScrcpySessionViewModel> sessionVmFactory)
    {
        _sessionVmFactory = sessionVmFactory
            ?? throw new ArgumentNullException(nameof(sessionVmFactory));
    }

    /// <summary>All open tabs. Bound by XAML to the chrome strip.</summary>
    public ObservableCollection<MirrorTabViewModel> Tabs { get; } = new();

    [ObservableProperty]
    private MirrorTabViewModel? _selectedTab;

    partial void OnSelectedTabChanged(MirrorTabViewModel? value)
    {
        // Keep each tab's IsActive flag in sync — drives the highlight style.
        foreach (var tab in Tabs)
        {
            tab.IsActive = ReferenceEquals(tab, value);
        }
        OnPropertyChanged(nameof(HasTabs));
    }

    /// <summary>True when at least one tab exists — drives empty-state UI.</summary>
    public bool HasTabs => Tabs.Count > 0;

    /// <summary>
    /// Open a new tab for the given session. Returns the existing tab if
    /// one is already open for this device serial (mirroring TabDesk's
    /// "one tab per device" model — opening twice just switches focus).
    /// </summary>
    public MirrorTabViewModel OpenTab(ScrcpySession session)
    {
        ArgumentNullException.ThrowIfNull(session);

        var existing = FindTabBySerial(session.DeviceSerial);
        if (existing is not null)
        {
            SelectedTab = existing;
            return existing;
        }

        var sessionVm = _sessionVmFactory(session);
        var tab = new MirrorTabViewModel(sessionVm) { IsActive = true };
        Tabs.Add(tab);
        SelectedTab = tab;
        return tab;
    }

    /// <summary>Close the tab for the given serial. No-op if not found.</summary>
    public void CloseTab(string serial)
    {
        var tab = FindTabBySerial(serial);
        if (tab is null)
        {
            return;
        }

        CloseTab(tab);
    }

    /// <summary>Close + dispose the supplied tab. Selects the neighbour if any.</summary>
    [RelayCommand]
    public void CloseTab(MirrorTabViewModel? tab)
    {
        if (tab is null)
        {
            return;
        }

        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        tab.Dispose();

        // Pick a neighbour so the chrome strip never points at a phantom tab.
        if (SelectedTab == tab)
        {
            if (Tabs.Count == 0)
            {
                SelectedTab = null;
            }
            else
            {
                var neighbourIndex = Math.Min(index, Tabs.Count - 1);
                SelectedTab = Tabs[neighbourIndex];
            }
        }
        OnPropertyChanged(nameof(HasTabs));
    }

    /// <summary>Select the tab for the supplied serial (no-op if absent).</summary>
    [RelayCommand]
    public void SelectTab(string? serial)
    {
        if (string.IsNullOrEmpty(serial))
        {
            return;
        }

        var tab = FindTabBySerial(serial);
        if (tab is not null)
        {
            SelectedTab = tab;
        }
    }

    private MirrorTabViewModel? FindTabBySerial(string serial)
    {
        foreach (var tab in Tabs)
        {
            if (string.Equals(tab.Serial, serial, StringComparison.OrdinalIgnoreCase))
            {
                return tab;
            }
        }
        return null;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var tab in Tabs)
        {
            tab.Dispose();
        }
        Tabs.Clear();
    }
}
