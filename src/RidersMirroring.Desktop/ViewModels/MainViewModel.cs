using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.AirPlay;
using Riders.Mirroring.Core.Scrcpy;
using Riders.Mirroring.Desktop.Theming;

namespace Riders.Mirroring.Desktop.ViewModels;

/// <summary>
/// Root view-model bound to <c>MainWindow.xaml</c>. Owns the list of devices
/// shown in the sidebar, the recent log lines displayed in the bottom strip,
/// and the currently-selected view (<see cref="SelectedView"/>).
/// </summary>
/// <remarks>
/// Device data is sourced from <see cref="DeviceFeedService"/> which in turn
/// drives <see cref="Core.Adb.AdbDeviceWatcher"/>. The collection of items
/// is exposed so XAML can bind to it directly.
/// </remarks>
public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IThemeService _theme;
    private readonly DeviceFeedService _feed;
    private System.ComponentModel.PropertyChangedEventHandler? _feedStatusHandler;

    /// <summary>All known devices, displayed in the left sidebar.</summary>
    public ObservableCollection<DeviceListItem> Devices => _feed.Items;

    /// <summary>
    /// Devices filtered by <see cref="DeviceFilter"/>. The sidebar ListBox
    /// binds here so a search query is applied without mutating the
    /// source-of-truth collection owned by <see cref="DeviceFeedService"/>.
    /// </summary>
    public ObservableCollection<DeviceListItem> FilteredDevices { get; } = new();

    private string _deviceFilter = string.Empty;
    /// <summary>Free-text filter applied to <see cref="Devices"/>. Bound to the
    /// sidebar search box.</summary>
    public string DeviceFilter
    {
        get => _deviceFilter;
        set
        {
            if (SetProperty(ref _deviceFilter, value))
            {
                ApplyFilter();
            }
        }
    }

    private void ApplyFilter()
    {
        FilteredDevices.Clear();
        var query = _deviceFilter?.Trim();
        if (string.IsNullOrEmpty(query))
        {
            foreach (var d in _feed.Items)
            {
                FilteredDevices.Add(d);
            }
        }
        else
        {
            foreach (var d in _feed.Items)
            {
                if (d.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || d.Serial.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || d.StateLabel.Contains(query, StringComparison.OrdinalIgnoreCase))
                {
                    FilteredDevices.Add(d);
                }
            }
        }
    }

    /// <summary>Short label rendered next to the DEVICES section header,
    /// e.g. "3 online" / "0" so the user sees the device count at a glance.</summary>
    public string DeviceCountLabel
    {
        get
        {
            var total = _feed.Items.Count;
            if (total == 0)
            {
                return "0";
            }

            var online = _feed.Items.Count(d => d.State == DeviceState.Connected);
            return total == online ? $"{total} online" : $"{online}/{total}";
        }
    }

    /// <summary>One-liner shown in the top bar between brand and window
    /// controls. Surfaces the most useful runtime hint (ADB reachability,
    /// active session, etc.).</summary>
    public string TopBarStatus
    {
        get
        {
            var devices = _feed.Items.Count;
            if (devices == 0)
            {
                return "No device connected";
            }

            var online = _feed.Items.Count(d => d.State == DeviceState.Connected);
            return online == 0
                ? "Waiting for device…"
                : $"{online} device{(online == 1 ? string.Empty : "s")} ready";
        }
    }

    /// <summary>True when the supplied <see cref="AppView"/> matches the
    /// currently-selected view. Kept for XAML convenience (lets the
    /// sidebar nav use an <c>IsChecked="{Binding IsViewSelected}"</c>
    /// pattern without a per-view converter).</summary>
    public bool IsViewSelected(AppView view) => view == SelectedView;

    /// <summary>The last N log entries rendered in the bottom strip.</summary>
    public ObservableCollection<LogLineViewModel> LogLines { get; } = new();

    /// <summary>
    /// All currently-mirroring sessions, one per active device. Backs the
    /// multi-device tile grid in the centre pane. Each item owns its own
    /// <see cref="System.Windows.Media.Imaging.WriteableBitmap"/>.
    /// </summary>
    public ObservableCollection<ScrcpySessionViewModel> ActiveSessions { get; } = new();

    private readonly ScrcpySessionManager? _sessionManager;

    /// <summary>Cap on <see cref="LogLines"/> to avoid unbounded growth.
    /// Older entries are trimmed FIFO when the cap is exceeded.</summary>
    public const int MaxLogLines = 500;

    /// <summary>View-model for the AirPlay centre pane. Lazy: built on first access.</summary>
    public AirPlayViewModel AirPlay { get; }

    /// <summary>View-model for the Android Mirror centre pane.</summary>
    public MirrorViewModel Mirror { get; }

    /// <summary>
    /// Chrome-style multi-instance mirror hub. Renders one tab per active
    /// mirroring session and the selected tab's live frame in the centre pane.
    /// Inspired by TabDesk's tabbed interface.
    /// </summary>
    public HubViewModel Hub { get; }

    [ObservableProperty]
    private DeviceListItem? _selectedDevice;

    [ObservableProperty]
    private string _statusMessage = "Ready.";

    [ObservableProperty]
    private AppView _selectedView = AppView.Mirror;

    public IReadOnlyList<AppView> AvailableViews { get; } = Enum.GetValues<AppView>();

    public string ProductName => "Riders Mirroring";

    public string ProductTagline => "Mirror Android & iPhone, side by side.";

    public MainViewModel(
        IThemeService theme,
        IAdbServerManager adb,
        IAirPlayReceiver airPlay,
        IScrcpyMirrorService? mirrorService = null,
        ScrcpySessionManager? sessionManager = null)
    {
        _theme = theme ?? throw new ArgumentNullException(nameof(theme));
        ArgumentNullException.ThrowIfNull(adb);
        ArgumentNullException.ThrowIfNull(airPlay);
        mirrorService ??= new Riders.Mirroring.Core.Scrcpy.ScrcpyMirrorProcess();
        _sessionManager = sessionManager ?? new ScrcpySessionManager(
            adb, new ScrcpyVideoDecoderFactory());

        _feed = new DeviceFeedService(adb);
        AirPlay = new AirPlayViewModel(airPlay);
        Mirror = new MirrorViewModel(mirrorService, adb);
        Hub = new HubViewModel();

        // Keep Mirror.SelectedDevice synchronized with MainViewModel.SelectedDevice
        Mirror.SelectedDevice = SelectedDevice;

        // Auto-select first device when detected; keep the filter list in sync
        // and refresh the top-bar counters on every change.
        _feed.Items.CollectionChanged += (_, e) =>
        {
            ApplyFilter();
            if (SelectedDevice == null && _feed.Items.Count > 0)
            {
                SelectedDevice = _feed.Items[0];
            }
            OnPropertyChanged(nameof(DeviceCountLabel));
            OnPropertyChanged(nameof(TopBarStatus));
        };

        // Mirror log output into live log strip
        mirrorService.LogOutputReceived += (_, log) =>
        {
            App.Current?.Dispatcher?.BeginInvoke(() =>
            {
                LogLines.Add(new LogLineViewModel(
                    Timestamp: DateTime.Now,
                    Level: LogLevel.Info,
                    Message: $"[scrcpy] {log}"));
            });
        };

        // Push the feed's status into the status bar.
        _feedStatusHandler = (_, e) =>
        {
            if (e.PropertyName == nameof(DeviceFeedService.Status))
            {
                if (!Mirror.IsRunning)
                {
                    StatusMessage = _feed.Status;
                }
            }
        };
        _feed.PropertyChanged += _feedStatusHandler;

        Mirror.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MirrorViewModel.StatusMessage))
            {
                StatusMessage = Mirror.StatusMessage;
            }
        };

        // Cap the live-log strip to avoid unbounded growth when the user
        // leaves the app open for hours. Trimming happens on the dispatcher
        // thread so the binding system stays coherent.
        LogLines.CollectionChanged += OnLogLinesChanged;

        SeedDemoLog();
    }

    /// <summary>
    /// Start mirroring the currently-selected device. Adds a new
    /// <see cref="ScrcpySessionViewModel"/> to <see cref="ActiveSessions"/>
    /// and starts the underlying scrcpy session asynchronously.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanStartSession))]
    private async Task StartSessionAsync()
    {
        var device = SelectedDevice;
        if (device is null || _sessionManager is null)
        {
            return;
        }

        var serial = device.Serial;
        if (string.IsNullOrWhiteSpace(serial))
        {
            return;
        }

        try
        {
            var session = await _sessionManager.StartSessionAsync(serial);
            var viewModel = new ScrcpySessionViewModel(session);
            ActiveSessions.Add(viewModel);
            await viewModel.StartAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to start mirroring: {ex.Message}";
        }
    }

    private bool CanStartSession() =>
        SelectedDevice is not null && _sessionManager is not null;

    /// <summary>
    /// Stop mirroring the given session (typically called from a Close
    /// button on each tile).
    /// </summary>
    [RelayCommand]
    private async Task StopSessionAsync(ScrcpySessionViewModel? viewModel)
    {
        if (viewModel is null || _sessionManager is null)
        {
            return;
        }

        try
        {
            await viewModel.StopAsync();
        }
        finally
        {
            ActiveSessions.Remove(viewModel);
            await _sessionManager.StopSessionAsync(viewModel.DeviceSerial);
        }
    }

    partial void OnSelectedDeviceChanged(DeviceListItem? value)
    {
        Mirror.SelectedDevice = value;
        StartSessionCommand.NotifyCanExecuteChanged();
    }

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        // Only react to additions (the typical case). Reset is also handled
        // because Clear()/re-seeds use it.
        if (e.Action != NotifyCollectionChangedAction.Add && e.Action != NotifyCollectionChangedAction.Reset)
        {
            return;
        }

        var overflow = LogLines.Count - MaxLogLines;
        if (overflow <= 0)
        {
            return;
        }

        // RemoveAt(0) is O(n) on ObservableCollection but happens at most
        // once per insertion event, so the amortized cost stays linear.
        for (int i = 0; i < overflow; i++)
        {
            LogLines.RemoveAt(0);
        }
    }

    /// <summary>
    /// Switch the centre pane to the requested view (Mirror / Settings / About).
    /// Bound to the bottom-nav buttons in <c>MainWindow.xaml</c>.
    /// </summary>
    [RelayCommand]
    private void SelectView(AppView view)
    {
        SelectedView = view;
    }

    /// <summary>Drop a single welcome line in the live log so the strip
    /// isn't empty on first launch.</summary>
    private void SeedDemoLog()
    {
        LogLines.Add(new LogLineViewModel(
            Timestamp: DateTime.Now,
            Level: LogLevel.Info,
            Message: "Welcome to Riders Mirroring — ready."));
    }

    /// <summary>
    /// Dispose the device feed, mirror session, and the AirPlay view-model. WPF doesn't call
    /// Dispose on the DataContext automatically, so
    /// <see cref="MainWindow.OnClosed"/> invokes this manually.
    /// </summary>
    public void Dispose()
    {
        if (_feedStatusHandler is not null)
        {
            _feed.PropertyChanged -= _feedStatusHandler;
            _feedStatusHandler = null;
        }

        LogLines.CollectionChanged -= OnLogLinesChanged;

        // Stop every active mirror synchronously. Each ScrcpySessionViewModel
        // disposes its underlying session; the manager tears down the
        // process/tunnel side.
        foreach (var session in ActiveSessions.ToArray())
        {
            try { session.Dispose(); } catch { /* best-effort */ }
        }
        ActiveSessions.Clear();

        if (_sessionManager is not null)
        {
            try { _sessionManager.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
            catch { /* best-effort */ }
        }

        _feed.Dispose();
        AirPlay.Dispose();
        Mirror.Dispose();
        Hub.Dispose();
    }
}

/// <summary>Top-level views that can be displayed in the centre pane.</summary>
public enum AppView
{
    Hub,
    Mirror,
    AirPlay,
    WirelessHost,
    Settings,
    About,
}

/// <summary>Human-readable label for each <see cref="AppView"/> entry.</summary>
public static class AppViewLabels
{
    public static string For(AppView view) => view switch
    {
        AppView.Hub          => "Hub",
        AppView.Mirror       => "Mirror",
        AppView.AirPlay      => "AirPlay",
        AppView.WirelessHost => "Wireless",
        AppView.Settings     => "Settings",
        AppView.About        => "About",
        _ => view.ToString(),
    };
}