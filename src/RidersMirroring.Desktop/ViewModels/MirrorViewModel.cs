using System.Diagnostics;
using System.IO;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.Logging;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.Desktop.ViewModels;

/// <summary>
/// View-model driving the primary Android Screen Mirroring view.
/// Exposes start/stop controls, configuration toggles (FPS, bitrate, stay-awake),
/// session timer, and quick remote control actions (Home, Back, Recents, Volume, Power).
/// </summary>
public sealed partial class MirrorViewModel : ObservableObject, IDisposable
{
    private readonly IScrcpyMirrorService _mirror;
    private readonly IAdbServerManager _adb;
    private readonly ILogger<MirrorViewModel> _logger;
    private readonly SynchronizationContext _ui;
    private readonly DispatcherTimer _sessionTimer;
    private DateTimeOffset? _sessionStartTime;

    [ObservableProperty]
    private DeviceListItem? _selectedDevice;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "Prêt à démarrer le mirroring.";

    [ObservableProperty]
    private string? _exitBanner;

    [ObservableProperty]
    private string _sessionDuration = "00:00";

    // --- Mirroring Options ---

    [ObservableProperty]
    private bool _stayAwake = true;

    [ObservableProperty]
    private bool _turnScreenOff = false;

    [ObservableProperty]
    private bool _alwaysOnTop = false;

    [ObservableProperty]
    private bool _fullscreen = false;

    [ObservableProperty]
    private bool _audioEnabled = true;

    [ObservableProperty]
    private bool _showTouches = false;

    [ObservableProperty]
    private int _selectedFps = 60;

    [ObservableProperty]
    private int _selectedBitrate = 8;

    [ObservableProperty]
    private int _selectedMaxSize = 0; // 0 = Auto / Native

    public IReadOnlyList<int> AvailableFps { get; } = new[] { 0, 30, 60, 90, 120 };
    public IReadOnlyList<int> AvailableBitrates { get; } = new[] { 4, 8, 16, 24 };
    public IReadOnlyList<int> AvailableMaxSizes { get; } = new[] { 0, 720, 1080, 1440 };

    public bool HasDevice => SelectedDevice != null;

    public MirrorViewModel(IScrcpyMirrorService mirror, IAdbServerManager adb)
    {
        _mirror = mirror ?? throw new ArgumentNullException(nameof(mirror));
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _logger = RidersLogger.Create<MirrorViewModel>();
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        _mirror.SessionStarted += OnSessionStarted;
        _mirror.SessionExited += OnSessionExited;
        _mirror.LogOutputReceived += OnLogOutputReceived;

        _sessionTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _sessionTimer.Tick += OnTimerTick;

        IsRunning = _mirror.IsRunning;
    }

    partial void OnSelectedDeviceChanged(DeviceListItem? value)
    {
        OnPropertyChanged(nameof(HasDevice));
        StartMirrorCommand.NotifyCanExecuteChanged();

        if (value != null && !IsRunning)
        {
            StatusMessage = $"Appareil sélectionné : {value.DisplayName} ({value.KindLabel})";
        }
    }

    partial void OnIsRunningChanged(bool value)
    {
        StartMirrorCommand.NotifyCanExecuteChanged();
        StopMirrorCommand.NotifyCanExecuteChanged();

        if (value)
        {
            _sessionStartTime = DateTimeOffset.Now;
            SessionDuration = "00:00";
            _sessionTimer.Start();
        }
        else
        {
            _sessionTimer.Stop();
            _sessionStartTime = null;
        }
    }

    partial void OnIsBusyChanged(bool value)
    {
        StartMirrorCommand.NotifyCanExecuteChanged();
        StopMirrorCommand.NotifyCanExecuteChanged();
    }

    private void OnTimerTick(object? sender, EventArgs e)
    {
        if (_sessionStartTime.HasValue)
        {
            var elapsed = DateTimeOffset.Now - _sessionStartTime.Value;
            SessionDuration = elapsed.TotalHours >= 1
                ? elapsed.ToString(@"hh\:mm\:ss")
                : elapsed.ToString(@"mm\:ss");
        }
    }

    [RelayCommand(CanExecute = nameof(CanStartMirror))]
    public async Task StartMirrorAsync()
    {
        if (SelectedDevice == null || IsRunning || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            ExitBanner = null;
            StatusMessage = $"Lancement du mirroring pour {SelectedDevice.DisplayName}…";

            var options = new ScrcpyOptions
            {
                MaxFps = SelectedFps,
                MaxBitrateMbps = SelectedBitrate,
                Width = SelectedMaxSize,
                Height = SelectedMaxSize,
                StayAwake = StayAwake,
                TurnScreenOff = TurnScreenOff,
                AlwaysOnTop = AlwaysOnTop,
                Fullscreen = Fullscreen,
                DisableAudio = !AudioEnabled,
                ShowTouches = ShowTouches,
                Clipboard = true,
                EmbedInHost = true,
                EmbedWidth = 1024,
                EmbedHeight = 640,
            };

            await _mirror.StartAsync(
                serial: SelectedDevice.Serial,
                deviceDisplayName: SelectedDevice.DisplayName,
                options: options).ConfigureAwait(true);

            IsRunning = _mirror.IsRunning;
            StatusMessage = $"Mirroring en cours : {SelectedDevice.DisplayName}";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start mirroring for {Serial}", SelectedDevice.Serial);
            StatusMessage = $"Erreur de démarrage : {ex.Message}";
            ExitBanner = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStopMirror))]
    public async Task StopMirrorAsync()
    {
        if (!IsRunning || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Arrêt du mirroring…";
            await _mirror.StopAsync().ConfigureAwait(true);
            IsRunning = _mirror.IsRunning;
            StatusMessage = "Mirroring arrêté.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop mirroring");
            StatusMessage = $"Erreur lors de l'arrêt : {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    public async Task ToggleMirrorAsync()
    {
        if (IsRunning)
        {
            await StopMirrorAsync();
        }
        else
        {
            await StartMirrorAsync();
        }
    }

    private bool CanStartMirror() => HasDevice && !IsRunning && !IsBusy;
    private bool CanStopMirror() => IsRunning && !IsBusy;

    // --- Android Remote Quick Actions ---

    [RelayCommand]
    public async Task SendKeyAsync(string keyName)
    {
        ArgumentNullException.ThrowIfNull(keyName);
        if (SelectedDevice == null) return;

        var keycode = keyName.ToLowerInvariant() switch
        {
            "back" => "4",
            "home" => "3",
            "recents" or "appswitch" => "187",
            "power" => "26",
            "volumeup" or "volup" => "24",
            "volumedown" or "voldown" => "25",
            "wake" => "224",
            "sleep" => "223",
            _ => null,
        };

        if (keycode is null) return;

        try
        {
            await _adb.ExecuteShellCommandAsync(SelectedDevice.Serial, $"input keyevent {keycode}");
            _logger.LogInformation("Sent keyevent {KeyName} ({Code}) to {Serial}", keyName, keycode, SelectedDevice.Serial);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to send keyevent {KeyName} to {Serial}", keyName, SelectedDevice.Serial);
        }
    }

    [RelayCommand]
    public async Task ExpandNotificationsAsync()
    {
        if (SelectedDevice == null) return;
        try
        {
            await _adb.ExecuteShellCommandAsync(SelectedDevice.Serial, "cmd statusbar expand-notifications");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to expand notifications on {Serial}", SelectedDevice.Serial);
        }
    }

    [RelayCommand]
    public async Task TakeScreenshotAsync()
    {
        if (SelectedDevice == null) return;

        try
        {
            var picturesDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
                "RidersMirroring");
            Directory.CreateDirectory(picturesDir);

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var localPath = Path.Combine(picturesDir, $"screenshot_{timestamp}.png");
            var remotePath = $"/sdcard/screencap_{timestamp}.png";

            await _adb.ExecuteShellCommandAsync(SelectedDevice.Serial, $"screencap -p {remotePath}");

            // Pull via ADB command line or shell cat
            var adbBinary = "adb";
            var psi = new ProcessStartInfo(adbBinary, $"-s {SelectedDevice.Serial} pull \"{remotePath}\" \"{localPath}\"")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
            };
            using (var pullProc = Process.Start(psi))
            {
                if (pullProc != null)
                {
                    await pullProc.WaitForExitAsync();
                }
            }

            // Clean up remote temp screenshot
            await _adb.ExecuteShellCommandAsync(SelectedDevice.Serial, $"rm {remotePath}");

            StatusMessage = $"Capture enregistrée : {Path.GetFileName(localPath)}";

            if (File.Exists(localPath))
            {
                // Open screenshot in default viewer
                Process.Start(new ProcessStartInfo(localPath) { UseShellExecute = true });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to take screenshot for {Serial}", SelectedDevice.Serial);
            StatusMessage = $"Erreur capture d'écran : {ex.Message}";
        }
    }

    [RelayCommand]
    private void DismissExitBanner() => ExitBanner = null;

    private void OnSessionStarted(object? sender, string serial)
    {
        _ui.Post(_ =>
        {
            IsRunning = true;
            ExitBanner = null;
            StatusMessage = $"Mirroring actif pour {SelectedDevice?.DisplayName ?? serial}";
        }, null);
    }

    private void OnSessionExited(object? sender, MirrorExitEventArgs e)
    {
        _ui.Post(_ =>
        {
            IsRunning = false;
            if (e.ExitCode == 0)
            {
                StatusMessage = "Session de mirroring terminée.";
            }
            else
            {
                StatusMessage = $"Session de mirroring arrêtée (code {e.ExitCode}).";
                if (!string.IsNullOrWhiteSpace(e.StderrTail))
                {
                    ExitBanner = e.StderrTail;
                }
            }
        }, null);
    }

    private void OnLogOutputReceived(object? sender, string logLine)
    {
        // Could be used for status bar hints if needed
    }

    public void Dispose()
    {
        _sessionTimer.Stop();
        _sessionTimer.Tick -= OnTimerTick;

        _mirror.SessionStarted -= OnSessionStarted;
        _mirror.SessionExited -= OnSessionExited;
        _mirror.LogOutputReceived -= OnLogOutputReceived;

        if (_mirror is IDisposable disp)
        {
            disp.Dispose();
        }
    }
}
