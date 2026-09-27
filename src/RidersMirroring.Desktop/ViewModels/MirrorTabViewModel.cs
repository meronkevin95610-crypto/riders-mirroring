using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.Desktop.ViewModels;

/// <summary>
/// One browser-style tab in the multi-instance mirror hub. Wraps a
/// single <see cref="ScrcpySession"/> so the user can open one tab per
/// device (or per app session on the same device).
///
/// <para>
/// Inspired by TabDesk's chrome-style tab interface — the
/// <see cref="HubViewModel"/> exposes an <c>ObservableCollection<MirrorTabViewModel></c>
/// that renders directly as a row of tabs in the top bar.
/// </para>
/// </summary>
public sealed partial class MirrorTabViewModel : ObservableObject, IDisposable
{
    private readonly ScrcpySessionViewModel _session;

    [ObservableProperty]
    private bool _isActive;

    public MirrorTabViewModel(ScrcpySessionViewModel session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        // Propagate session-level state up so the tab header can reflect it.
        _session.PropertyChanged += (_, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(ScrcpySessionViewModel.StatusLabel):
                    OnPropertyChanged(nameof(StatusLabel));
                    OnPropertyChanged(nameof(IsStreaming));
                    break;
                case nameof(ScrcpySessionViewModel.CurrentFps):
                    OnPropertyChanged(nameof(CurrentFps));
                    break;
            }
        };
    }

    /// <summary>Underlying session VM (1:1 with one mirroring stream).</summary>
    public ScrcpySessionViewModel Session => _session;

    /// <summary>ADB serial — also used as the unique tab key.</summary>
    public string Serial => _session.DeviceSerial;

    /// <summary>Tab title shown in the chrome strip. Falls back to the serial.</summary>
    public string Title
    {
        get
        {
            var serial = _session.DeviceSerial;
            return string.IsNullOrEmpty(serial) ? "Untitled" : serial;
        }
    }

    /// <summary>Streaming status forwarded from the session.</summary>
    public string StatusLabel => _session.StatusLabel;

    /// <summary>True when frames are flowing — drives the live dot indicator on the tab.</summary>
    public bool IsStreaming => string.Equals(_session.StatusLabel, "Streaming", StringComparison.OrdinalIgnoreCase);

    /// <summary>Forwarded FPS, displayed in the tab tooltip.</summary>
    public double CurrentFps => _session.CurrentFps;

    /// <summary>Tool-tipped onto the tab so users can see live metrics on hover.</summary>
    public string Tooltip => $"{Title}\n{StatusLabel}\n{CurrentFps:0.0} fps";

    /// <summary>Start the underlying mirroring session.</summary>
    [RelayCommand]
    public async Task StartAsync()
    {
        await _session.StartAsync().ConfigureAwait(false);
    }

    /// <summary>Stop the underlying mirroring session.</summary>
    [RelayCommand]
    public async Task StopAsync()
    {
        await _session.StopAsync().ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public void Dispose() => _session.Dispose();
}
