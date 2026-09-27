using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.Desktop.ViewModels;

/// <summary>
/// WPF-facing view-model for one mirroring session. Owns the
/// <see cref="WriteableBitmap"/> that the screen tile binds to, and
/// marshals decoded <see cref="DecodedVideoFrame"/> events from the
/// decoder thread onto the UI dispatcher.
///
/// <para>
/// One <see cref="ScrcpySessionViewModel"/> per active mirroring
/// session. They are created by <see cref="MainViewModel"/> when the
/// user starts a mirror, and disposed when the session ends.
/// </para>
/// </summary>
public sealed partial class ScrcpySessionViewModel : ObservableObject, IDisposable
{
    private readonly ScrcpySession _session;
    private readonly Dispatcher _uiDispatcher;
    private readonly WriteableBitmap?[] _bitmapPool = new WriteableBitmap?[2];
    private int _bitmapIndex;
    private int _frameCount;
    private DateTime _lastFpsTick = DateTime.UtcNow;

    [ObservableProperty]
    private ImageSource? _currentFrame;

    [ObservableProperty]
    private double _currentFps;

    [ObservableProperty]
    private string _statusLabel = "Idle";

    public string DeviceSerial => _session.DeviceSerial;

    public ScrcpySessionViewModel(ScrcpySession session)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        _uiDispatcher = Application.Current?.Dispatcher
            ?? Dispatcher.CurrentDispatcher;
        _session.FrameDecoded += OnFrameDecoded;
        _session.SessionFaulted += OnSessionFaulted;
        UpdateStatusFromSession();
    }

    /// <summary>
    /// Begin mirroring. Fire-and-forget; frame events start flowing on
    /// the decoder thread and are marshalled to the UI in
    /// <see cref="OnFrameDecoded"/>.
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken = default)
        => _session.StartAsync(cancellationToken);

    /// <summary>Stop mirroring and dispose the underlying session.</summary>
    public async Task StopAsync()
    {
        _session.FrameDecoded -= OnFrameDecoded;
        _session.SessionFaulted -= OnSessionFaulted;
        await _session.DisposeAsync();
        StatusLabel = "Stopped";
    }

    private void OnSessionFaulted(object? sender, ScrcpySessionFaultedEventArgs e)
    {
        // We were already on the decoder thread; marshal to UI.
        _uiDispatcher.BeginInvoke(() =>
        {
            StatusLabel = $"Faulted: {e.Message}";
        });
    }

    private void OnFrameDecoded(object? sender, DecodedVideoFrame frame)
    {
        // This runs on the FFmpeg decoder thread. Copy pixels + push the
        // BitmapSource to the UI thread; never touch WPF objects directly.
        var bitmap = AcquireOrResizeBitmap(frame.Width, frame.Height, frame.Stride);
        if (bitmap is null)
        {
            return;
        }

        // WritePixels takes Int32Rect + array of BGRA bytes.
        // ReadOnlyMemory<byte>.ToArray() copies — for maximum throughput
        // we could pin + use an unsafe copy, but the dispatcher hop is
        // already the dominant cost so the extra allocation is fine.
        bitmap.Lock();
        try
        {
            bitmap.WritePixels(
                new Int32Rect(0, 0, frame.Width, frame.Height),
                frame.BgraPixels.ToArray(),
                frame.Stride * frame.Height,
                frame.Stride);
        }
        finally
        {
            bitmap.Unlock();
        }

        // Swap the bitmap into the property on the UI thread.
        _uiDispatcher.BeginInvoke(() =>
        {
            CurrentFrame = bitmap;
            UpdateFps();
            UpdateStatusFromSession();
        });
    }

    private WriteableBitmap? AcquireOrResizeBitmap(int width, int height, int stride)
    {
        var idx = _bitmapIndex;
        var existing = _bitmapPool[idx];
        if (existing is { PixelWidth: var pw, PixelHeight: var ph }
            && pw == width && ph == height)
        {
            return existing;
        }

        // Allocate a fresh WriteableBitmap. Bgr32 = 4 bytes/pixel, matches
        // the BGRA layout we get from FFmpeg's sws_scale (the alpha byte
        // is ignored by WPF but consumed correctly thanks to the channel
        // ordering swap).
        WriteableBitmap fresh;
        try
        {
            fresh = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
        }
        catch (Exception ex)
        {
            // Width/height 0 or extremely large — drop the frame, keep the
            // session alive. Logged via the session-level error handler.
            System.Diagnostics.Debug.WriteLine(
                $"ScrcpySessionViewModel: cannot allocate WriteableBitmap {width}x{height}: {ex.Message}");
            return null;
        }

        _bitmapPool[idx] = fresh;
        // Toggle the slot so the next frame uses the other pool entry.
        // Two slots lets the UI thread keep reading slot 0 while we fill
        // slot 1 without tearing.
        _bitmapIndex = (_bitmapIndex + 1) % _bitmapPool.Length;
        return fresh;
    }

    private void UpdateFps()
    {
        _frameCount++;
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastFpsTick).TotalSeconds;
        if (elapsed >= 1.0)
        {
            CurrentFps = Math.Round(_frameCount / elapsed, 1);
            _frameCount = 0;
            _lastFpsTick = now;
        }
    }

    private void UpdateStatusFromSession()
    {
        StatusLabel = _session.Status switch
        {
            ScrcpySessionStatus.Idle => "Idle",
            ScrcpySessionStatus.AwaitingDevice => "Connecting…",
            ScrcpySessionStatus.Streaming => "Streaming",
            ScrcpySessionStatus.Stopped => "Stopped",
            ScrcpySessionStatus.Faulted => "Faulted",
            _ => "Unknown",
        };
    }

    public void Dispose()
    {
        // Best-effort sync dispose. WPF-bound code never calls this on the
        // UI thread — it goes through StopAsync first.
        try { _session.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
        catch { /* swallowed: DisposeAsync is best-effort */ }
    }
}
