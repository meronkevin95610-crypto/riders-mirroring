using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.AirPlay;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Desktop.ViewModels;

/// <summary>
/// View-model for the AirPlay tab. Wraps <see cref="IAirPlayReceiver"/>,
/// exposes Start/Stop commands, a status line, and a live list of connected
/// iPhone / iPad sessions.
/// </summary>
public sealed partial class AirPlayViewModel : ObservableObject, IDisposable
{
    private readonly IAirPlayReceiver _receiver;
    private readonly ILogger<AirPlayViewModel> _logger;
    private readonly SynchronizationContext _ui;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusMessage = "AirPlay receiver is stopped.";

    [ObservableProperty]
    private string _lastStatusLine = string.Empty;

    [ObservableProperty]
    private string _binaryHint = string.Empty;

    [ObservableProperty]
    private string? _exitBanner;

    /// <summary>Sessions currently connected to the receiver.</summary>
    public ObservableCollection<AirPlaySession> Sessions { get; } = new();

    /// <summary>True when at least one device is connected.</summary>
    public bool HasSessions => Sessions.Count > 0;

    public AirPlayViewModel(IAirPlayReceiver receiver)
    {
        _receiver = receiver ?? throw new ArgumentNullException(nameof(receiver));
        _logger = RidersLogger.Create<AirPlayViewModel>();
        _ui = SynchronizationContext.Current ?? new SynchronizationContext();

        _receiver.SessionsChanged += OnSessionsChanged;
        _receiver.Exited += OnExited;

        IsRunning = _receiver.IsRunning;
        LastStatusLine = _receiver.LastStatusLine;
        BinaryHint = BuildBinaryHint();

        // Seed with whatever the receiver already knows about.
        RefreshSessions(_receiver.ActiveSessions);
        UpdateStatus();
    }

    private static string BuildBinaryHint()
    {
        // Keep the user informed about where we expect uxplay.exe to live.
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return "Expected uxplay.exe at one of:\r\n"
             + $"  • {localApp}\\Riders Mirroring\\bin\\uxplay\\uxplay.exe\r\n"
             + $"  • <install>\\vendor\\uxplay\\uxplay.exe (bundled with the app)\r\n"
             + "  • anywhere on PATH";
    }

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        if (IsRunning || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Starting AirPlay receiver…";
            await _receiver.StartAsync().ConfigureAwait(true);
            IsRunning = _receiver.IsRunning;
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to start AirPlay receiver");
            StatusMessage = $"Failed to start: {ex.Message}";
            ExitBanner = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanStop))]
    private async Task StopAsync()
    {
        if (!IsRunning || IsBusy)
        {
            return;
        }

        try
        {
            IsBusy = true;
            StatusMessage = "Stopping AirPlay receiver…";
            await _receiver.StopAsync().ConfigureAwait(true);
            IsRunning = _receiver.IsRunning;
            Sessions.Clear();
            OnPropertyChanged(nameof(HasSessions));
            UpdateStatus();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to stop AirPlay receiver");
            StatusMessage = $"Failed to stop: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanStart() => !IsRunning && !IsBusy;
    private bool CanStop()  =>  IsRunning && !IsBusy;

    /// <summary>
    /// Notify WPF that the Start/Stop commands should re-evaluate their
    /// <c>CanExecute</c>. Called from the partial OnIsBusyChanged / OnIsRunningChanged.
    /// </summary>
    private void RaiseCommandsCanExecuteChanged()
    {
        StartCommand.NotifyCanExecuteChanged();
        StopCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsRunningChanged(bool value) => RaiseCommandsCanExecuteChanged();
    partial void OnIsBusyChanged(bool value)    => RaiseCommandsCanExecuteChanged();

    [RelayCommand]
    private void DismissExitBanner() => ExitBanner = null;

    /// <summary>
    /// Unsubscribe from the underlying receiver's events. Called by
    /// <see cref="MainViewModel.Dispose"/> when the window closes.
    /// </summary>
    public void Dispose()
    {
        _receiver.SessionsChanged -= OnSessionsChanged;
        _receiver.Exited          -= OnExited;
    }

    private void OnSessionsChanged(object? sender, IReadOnlyList<AirPlaySession> sessions)
    {
        // The receiver raises events from its stdout pump thread.
        _ui.Post(_ => RefreshSessions(sessions), null);
    }

    private void OnExited(object? sender, AirPlayExitEventArgs e)
    {
        _ui.Post(_ =>
        {
            IsRunning = false;
            OnPropertyChanged(nameof(HasSessions));
            Sessions.Clear();
            ExitBanner = e.ExitCode == 0
                ? "AirPlay receiver stopped."
                : $"AirPlay receiver exited with code {e.ExitCode}.";
            if (!string.IsNullOrEmpty(e.StderrTail))
            {
                ExitBanner += "\r\n" + e.StderrTail;
            }
            UpdateStatus();
        }, null);
    }

    private void RefreshSessions(IReadOnlyList<AirPlaySession> sessions)
    {
        Sessions.Clear();
        foreach (var s in sessions)
        {
            Sessions.Add(s);
        }

        LastStatusLine = _receiver.LastStatusLine;
        OnPropertyChanged(nameof(HasSessions));
        UpdateStatus();
    }

    private void UpdateStatus()
    {
        if (IsRunning)
        {
            StatusMessage = Sessions.Count == 0
                ? "AirPlay receiver is running. Waiting for a device…"
                : $"{Sessions.Count} device(s) connected.";
        }
        else if (StatusMessage.StartsWith("Starting", StringComparison.Ordinal)
              || StatusMessage.StartsWith("Stopping", StringComparison.Ordinal))
        {
            // leave the transitional message in place
        }
        else
        {
            StatusMessage = "AirPlay receiver is stopped.";
        }
    }
}
