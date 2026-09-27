using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Riders.Mirroring.Desktop.ViewModels;
using Riders.Mirroring.Desktop.Windows;

namespace Riders.Mirroring.Desktop.Views;

/// <summary>
/// Interaction logic for MirrorView.xaml. Watches the
/// <see cref="MirrorViewModel.IsRunning"/> flag and, while mirroring is
/// active, attaches a DWM thumbnail of the <c>scrcpy.exe</c> window inside
/// <see cref="MirrorPreviewContainer"/> so the mirror shows up in the hub
/// instead of spawning a separate top-level window.
/// </summary>
public partial class MirrorView : UserControl
{
    private DwmThumbnail? _thumbnail;
    private DispatcherTimer? _attachTimer;

    public MirrorView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MirrorViewModel oldVm)
        {
            oldVm.PropertyChanged -= OnVmPropertyChanged;
        }
        if (e.NewValue is MirrorViewModel newVm)
        {
            newVm.PropertyChanged += OnVmPropertyChanged;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MirrorViewModel vm)
        {
            vm.PropertyChanged += OnVmPropertyChanged;
            if (vm.IsRunning) StartAttachRetry();
        }
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MirrorViewModel vm)
        {
            vm.PropertyChanged -= OnVmPropertyChanged;
        }
        StopAttachRetry();
        _thumbnail?.Dispose();
        _thumbnail = null;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MirrorViewModel.IsRunning)) return;
        if (DataContext is not MirrorViewModel vm) return;

        if (vm.IsRunning)
        {
            StartAttachRetry();
        }
        else
        {
            StopAttachRetry();
            _thumbnail?.Dispose();
            _thumbnail = null;
        }
    }

    // ── DWM attachment with DispatcherTimer retry ──────────────────────────

    /// <summary>
    /// Start a <see cref="DispatcherTimer"/> that calls <see cref="TryAttachOnce"/>
    /// every 500 ms on the UI thread until the scrcpy window is found.
    /// Using a timer keeps the dispatcher free between attempts (no
    /// <c>Thread.Sleep</c> or busy-wait).
    /// </summary>
    private void StartAttachRetry()
    {
        StopAttachRetry(); // guard against double-start
        _attachTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _attachTimer.Tick += OnAttachTimerTick;
        _attachTimer.Start();
    }

    private void StopAttachRetry()
    {
        if (_attachTimer is null) return;
        _attachTimer.Stop();
        _attachTimer.Tick -= OnAttachTimerTick;
        _attachTimer = null;
    }

    private void OnAttachTimerTick(object? sender, EventArgs e)
    {
        if (TryAttachOnce())
        {
            // Successfully attached — no need to keep retrying.
            StopAttachRetry();
        }
        // else: leave the timer running; it will tick again in 500 ms.
    }

    /// <summary>
    /// Attempt to locate the scrcpy window and attach the DWM thumbnail.
    /// </summary>
    /// <returns><c>true</c> if attachment succeeded; <c>false</c> to signal
    /// the caller to retry later.</returns>
    private bool TryAttachOnce()
    {
        // Find the scrcpy window by process image name.
        var hwnd = ExternalWindowFinder.FindByProcessFileName("scrcpy.exe");
        if (hwnd is null || hwnd == IntPtr.Zero)
        {
            return false; // scrcpy hasn't spawned yet
        }

        var container = MirrorPreviewContainer;
        if (container is null) return false;

        var host = Window.GetWindow(this);
        if (host is null) return false;

        // Convert the container's screen position to MainWindow client coordinates.
        var topLeftScreen = container.PointToScreen(new Point(0, 0));
        var originScreen  = host.PointToScreen(new Point(0, 0));
        var dest = new DwmThumbnail.RECT(
            (int)(topLeftScreen.X - originScreen.X),
            (int)(topLeftScreen.Y - originScreen.Y),
            (int)(topLeftScreen.X - originScreen.X + container.ActualWidth),
            (int)(topLeftScreen.Y - originScreen.Y + container.ActualHeight));

        _thumbnail ??= new DwmThumbnail(container);
        return _thumbnail.Attach(hwnd.Value, dest);
    }
}
