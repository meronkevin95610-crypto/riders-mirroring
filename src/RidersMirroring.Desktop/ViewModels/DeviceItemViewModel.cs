using CommunityToolkit.Mvvm.ComponentModel;

namespace Riders.Mirroring.Desktop.ViewModels;

/// <summary>
/// One row in the sidebar device list. Kept very small — full device data
/// (battery, resolution, etc.) will be added at Étape 3 when ADB discovery
/// lands.
/// </summary>
public sealed partial class DeviceItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _id = string.Empty;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private DeviceKind _kind;

    [ObservableProperty]
    private DeviceState _state;
}

public enum DeviceKind
{
    Android,
    iPhone,
    WirelessHost,
}

public enum DeviceState
{
    Idle,
    Connecting,
    Connected,
    Error,
}