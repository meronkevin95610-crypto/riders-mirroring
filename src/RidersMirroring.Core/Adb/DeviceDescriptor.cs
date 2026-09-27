namespace Riders.Mirroring.Core.Adb;

/// <summary>
/// Discriminated device type reported by <c>adb devices -l</c> and exposed
/// to the UI as the icon/section in the sidebar.
/// </summary>
public enum DeviceTransport
{
    /// <summary>USB-connected Android device or emulator.</summary>
    Usb,

    /// <summary>Device connected over TCP/IP (Wireless ADB / Wi-Fi pairing).</summary>
    Network,

    /// <summary>Type could not be determined.</summary>
    Unknown,
}

/// <summary>
/// Snapshot of one Android device as reported by ADB. Immutable so it's
/// safe to pass across threads (the watcher publishes fresh instances each
/// time a device connects or disconnects).
/// </summary>
/// <param name="Serial">ADB serial (e.g. <c>"emulator-5554"</c>, <c>"R5CT70ABCD123 usb:337..."</c>).</param>
/// <param name="State">Connection state reported by ADB (<c>device</c>, <c>offline</c>, <c>unauthorized</c>).</param>
/// <param name="Model">Friendly model name if <c>getprop ro.product.model</c> succeeded.</param>
/// <param name="Manufacturer">Friendly manufacturer name.</param>
/// <param name="AndroidVersion">Android version (<c>14</c>, <c>15</c>, …) if available.</param>
/// <param name="Sdk">SDK level if available.</param>
/// <param name="Transport">How the device is connected.</param>
public sealed record DeviceDescriptor(
    string Serial,
    string State,
    string? Model,
    string? Manufacturer,
    string? AndroidVersion,
    int? Sdk,
    DeviceTransport Transport)
{
    /// <summary>Display name for the sidebar ("Pixel 8 — USB").</summary>
    public string DisplayName
    {
        get
        {
            var friendly = Model ?? Manufacturer ?? Serial;
            var suffix = Transport switch
            {
                DeviceTransport.Network => "Wi-Fi",
                DeviceTransport.Usb     => "USB",
                _                       => "unknown",
            };
            return $"{friendly} — {suffix}";
        }
    }

    /// <summary>True when the device is fully online.</summary>
    /// <remarks>
    /// We accept both the raw ADB string (<c>"device"</c>) and the enum
    /// representation (<c>"Online"</c>), since either path can populate
    /// <see cref="State"/> depending on whether the caller constructed the
    /// descriptor from an <c>adb devices -l</c> line or from
    /// <see cref="AdvancedSharpAdbClient.Models.DeviceData"/>.
    /// </remarks>
    public bool IsOnline
    {
        get
        {
            if (string.IsNullOrEmpty(State))
            {
                return false;
            }

            return State.Equals("device", StringComparison.OrdinalIgnoreCase)
                || State.Equals("online",  StringComparison.OrdinalIgnoreCase);
        }
    }
}