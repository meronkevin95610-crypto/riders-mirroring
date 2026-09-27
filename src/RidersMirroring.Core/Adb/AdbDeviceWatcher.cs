using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.Adb;

/// <summary>
/// Polls the local ADB server for connected devices on a fixed interval
/// and publishes the latest snapshot through <see cref="SnapshotChanged"/>.
/// The first snapshot is published synchronously so the UI can render
/// without waiting for a full poll cycle.
/// </summary>
/// <remarks>
/// Polling (rather than the SDK's <c>DeviceWatcher</c> events) is preferred
/// because it is portable across versions of AdvancedSharpAdbClient and
/// behaves well when the ADB server itself is restarted.
/// </remarks>
public sealed class AdbDeviceWatcher : IDisposable
{
    private readonly IAdbServerManager _adb;
    private readonly ILogger<AdbDeviceWatcher> _logger;
    private readonly TimeSpan _interval;

    private CancellationTokenSource? _cts;
    private Task? _loop;
    private IReadOnlyList<DeviceDescriptor> _lastSnapshot = Array.Empty<DeviceDescriptor>();

    /// <summary>Raised whenever the device list changes (and on the first poll).</summary>
    public event EventHandler<DeviceSnapshotEventArgs>? SnapshotChanged;

    /// <summary>Last snapshot published by this watcher.</summary>
    public IReadOnlyList<DeviceDescriptor> Current => _lastSnapshot;

    public AdbDeviceWatcher(IAdbServerManager adb)
        : this(adb, RidersLogger.Create<AdbDeviceWatcher>(), TimeSpan.FromSeconds(2))
    {
    }

    /// <summary>Test-friendly constructor.</summary>
    public AdbDeviceWatcher(IAdbServerManager adb, ILogger<AdbDeviceWatcher> logger, TimeSpan interval)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (interval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(interval), "Interval must be > 0.");
        }

        _interval = interval;
    }

    /// <summary>Start the background polling loop.</summary>
    public void Start()
    {
        if (_loop is not null)
        {
            return;
        }

        _cts = new CancellationTokenSource();
        _loop = Task.Run(() => RunAsync(_cts.Token));
    }

    /// <summary>Stop the polling loop. Safe to call multiple times.</summary>
    public async Task StopAsync()
    {
        if (_cts is null)
        {
            return;
        }

        _cts.Cancel();

        if (_loop is not null)
        {
            try { await _loop.ConfigureAwait(false); }
            catch (OperationCanceledException) { /* expected */ }
        }

        _cts.Dispose();
        _cts = null;
        _loop = null;
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            await _adb.EnsureStartedAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        while (!cancellationToken.IsCancellationRequested)
        {
            IReadOnlyList<DeviceDescriptor> snapshot;
            try
            {
                snapshot = await _adb.GetDevicesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Device poll failed");
                snapshot = Array.Empty<DeviceDescriptor>();
            }

            PublishIfChanged(snapshot);

            try
            {
                await Task.Delay(_interval, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private void PublishIfChanged(IReadOnlyList<DeviceDescriptor> next)
    {
        if (SnapshotsEqual(_lastSnapshot, next))
        {
            return;
        }

        _lastSnapshot = next;
        SnapshotChanged?.Invoke(this, new DeviceSnapshotEventArgs(next));
    }

    private static bool SnapshotsEqual(
        IReadOnlyList<DeviceDescriptor> a,
        IReadOnlyList<DeviceDescriptor> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (var i = 0; i < a.Count; i++)
        {
            if (!DeviceEquals(a[i], b[i]))
            {
                return false;
            }
        }

        return true;
    }

    private static bool DeviceEquals(DeviceDescriptor x, DeviceDescriptor y)
        => x.Serial == y.Serial
           && x.State == y.State
           && x.Model == y.Model
           && x.Transport == y.Transport
           && x.AndroidVersion == y.AndroidVersion;

    public void Dispose() => StopAsync().GetAwaiter().GetResult();
}

/// <summary>Payload for <see cref="AdbDeviceWatcher.SnapshotChanged"/>.</summary>
public sealed class DeviceSnapshotEventArgs : EventArgs
{
    public DeviceSnapshotEventArgs(IReadOnlyList<DeviceDescriptor> devices)
    {
        Devices = devices;
    }

    public IReadOnlyList<DeviceDescriptor> Devices { get; }
}