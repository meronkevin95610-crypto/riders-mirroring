using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Desktop.ViewModels;

/// <summary>
/// Bridges <see cref="Core.Adb.AdbDeviceWatcher"/> to the WPF layer: starts
/// the watcher on construction, exposes the live snapshot as an observable
/// collection, and pushes fresh entries into the global logger so they end
/// up on disk.
/// </summary>
public sealed partial class DeviceFeedService : ObservableObject, IDisposable
{
    private readonly AdbDeviceWatcher _watcher;
    private readonly ILogger<DeviceFeedService> _logger;

    [ObservableProperty]
    private string _status = "Initialising…";

    public ObservableCollection<DeviceListItem> Items { get; } = new();

    public DeviceFeedService(IAdbServerManager adb)
    {
        ArgumentNullException.ThrowIfNull(adb);
        _logger = RidersLogger.Create<DeviceFeedService>();
        _watcher = new AdbDeviceWatcher(adb);
        _watcher.SnapshotChanged += OnSnapshot;
        _watcher.Start();
        Status = "Watching for devices…";
    }

    private void OnSnapshot(object? sender, DeviceSnapshotEventArgs e)
    {
        // Marshal onto the WPF dispatcher thread before mutating Items.
        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess())
        {
            ApplySnapshot(e.Devices);
        }
        else
        {
            dispatcher.BeginInvoke(new Action(() => ApplySnapshot(e.Devices)));
        }
    }

    private void ApplySnapshot(IReadOnlyList<DeviceDescriptor> devices)
    {
        // Diff instead of Clear()+Add(): each Clear raises a Reset event and
        // every Add raises an Add event, so for N devices we triggered 1+N
        // CollectionChanged events on the UI thread, forcing WPF to relayout
        // every row. The diff raises only the events that actually changed
        // (Remove/Insert/Replace) and keeps the insertion order of unchanged
        // items so the sidebar doesn't visually jump.
        DeviceListItemDiff.Apply(Items, devices, DeviceListItem.FromDescriptor);

        // Log only newly-arrived serials so reconnect storms don't spam.
        var known = Items.Select(i => i.Serial).ToHashSet(StringComparer.Ordinal);
        foreach (var d in devices)
        {
            if (known.Add(d.Serial))
            {
                _logger.LogInformation("Device {Serial} ({State}, {Transport})", d.Serial, d.State, d.Transport);
            }
        }

        Status = devices.Count switch
        {
            0 => "No devices connected",
            1 => "1 device connected",
            _ => $"{devices.Count} devices connected",
        };
    }

    /// <summary>
    /// Pure diff helper extracted from <see cref="ApplySnapshot"/> so it can
    /// be unit-tested without spinning up a WPF dispatcher.
    /// </summary>
    public static class DeviceListItemDiff
    {
        public static void Apply(
            ObservableCollection<DeviceListItem> target,
            IReadOnlyList<DeviceDescriptor> snapshot,
            Func<DeviceDescriptor, DeviceListItem> projector)
        {
            ArgumentNullException.ThrowIfNull(target);
            ArgumentNullException.ThrowIfNull(snapshot);
            ArgumentNullException.ThrowIfNull(projector);

            // Build the desired key order from the snapshot (ADB's authoritative order).
            // Last-write-wins on duplicates — defensive coding for buggy ADB builds.
            var desired = new List<(string Key, DeviceListItem Item)>(snapshot.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var d in snapshot)
            {
                if (seen.Add(d.Serial))
                {
                    desired.Add((d.Serial, projector(d)));
                }
            }

            // Map current target serials → (item, originalIndex) so we can move
            // existing items to their new positions instead of replacing them.
            var current = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < target.Count; i++)
            {
                current[target[i].Serial] = i;
            }

            // 1) Drop items that disappeared from the snapshot. Walk backwards so
            //    each RemoveAt leaves earlier indices untouched.
            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!seen.Contains(target[i].Serial))
                {
                    target.RemoveAt(i);
                    current.Remove(target[i].Serial); // no-op (already gone), but keeps the invariant honest
                }
            }

            // Rebuild the index map now that some items were removed.
            current.Clear();
            for (int i = 0; i < target.Count; i++)
            {
                current[target[i].Serial] = i;
            }

            // 2) Walk desired in order. For each entry, ensure the matching
            //    item sits at position `i` in target. Insert if missing,
            //    Move if in the wrong slot, no-op if already correct. When
            //    the underlying fields changed, Replace the item in place so
            //    WPF rebinds the visible labels.
            for (int desiredIndex = 0; desiredIndex < desired.Count; desiredIndex++)
            {
                var (key, fresh) = desired[desiredIndex];

                if (current.TryGetValue(key, out int existingIndex))
                {
                    if (existingIndex == desiredIndex)
                    {
                        if (!ItemsEqual(target[desiredIndex], fresh))
                        {
                            target[desiredIndex] = fresh;
                            // The Replace keeps the same Serial at the same index,
                            // so the `current` map stays valid.
                        }
                    }
                    else
                    {
                        target.Move(existingIndex, desiredIndex);
                        // Move shifts everyone between desiredIndex..existingIndex
                        // by +1; rebuild the map to stay accurate.
                        RebuildIndexMap(target, current);
                        if (!ItemsEqual(target[desiredIndex], fresh))
                        {
                            target[desiredIndex] = fresh;
                        }
                    }
                }
                else
                {
                    target.Insert(desiredIndex, fresh);
                    RebuildIndexMap(target, current);
                }
            }
        }

        private static void RebuildIndexMap(
            ObservableCollection<DeviceListItem> target,
            Dictionary<string, int> map)
        {
            map.Clear();
            for (int i = 0; i < target.Count; i++)
            {
                map[target[i].Serial] = i;
            }
        }

        private static bool ItemsEqual(DeviceListItem a, DeviceListItem b)
        {
            return a.Serial == b.Serial
                && a.DisplayName == b.DisplayName
                && a.KindLabel == b.KindLabel
                && a.StateLabel == b.StateLabel
                && a.State == b.State
                && a.Transport == b.Transport;
        }
    }

    /// <summary>Force-refresh the snapshot — for testing/debug only.</summary>
    [RelayCommand]
    private void Nop()
    {
        // The polling loop fires every 2 s; this command exists so the
        // XAML toolbar can show a refresh affordance later without code changes.
        Status = "Watching for devices…";
    }

    public void Dispose()
    {
        _watcher.SnapshotChanged -= OnSnapshot;
        _watcher.Dispose();
    }
}

/// <summary>
/// One row in the sidebar device list. Holds a <see cref="DeviceDescriptor"/>
/// snapshot so the rest of the UI can read it without caring about the Core
/// type directly.
/// </summary>
public sealed class DeviceListItem
{
    public required string Serial { get; init; }
    public required string DisplayName { get; init; }
    public required string KindLabel { get; init; }
    public required string StateLabel { get; init; }
    public required DeviceState State { get; init; }
    public required DeviceTransport Transport { get; init; }

    public static DeviceListItem FromDescriptor(DeviceDescriptor d)
    {
        ArgumentNullException.ThrowIfNull(d);
        return new DeviceListItem
        {
            Serial = d.Serial,
            DisplayName = d.DisplayName,
            KindLabel = d.Transport == DeviceTransport.Network ? "Wi-Fi" : "USB",
            StateLabel = d.State,
            State = ParseState(d.State),
            Transport = d.Transport,
        };
    }

    private static DeviceState ParseState(string s) => s.ToLowerInvariant() switch
    {
        "online"   => DeviceState.Connected,
        "device"   => DeviceState.Connected,
        "offline"  => DeviceState.Idle,
        _          => DeviceState.Idle,
    };
}