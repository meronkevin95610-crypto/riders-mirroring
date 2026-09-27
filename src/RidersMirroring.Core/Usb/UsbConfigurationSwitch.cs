using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.Usb;

/// <summary>
/// Toggle the Windows USB configuration for an ADB device between
/// <c>WinUSB</c> (used by ADB) and the composite / MTP / PTP defaults used
/// by Android's own file-transfer UI. This is what makes the device show
/// up to <c>adb devices</c> on a brand-new install without rebooting or
/// re-plugging.
/// </summary>
/// <remarks>
/// <para>
/// We use <c>SetupAPI.dll</c> + <c>WinUsb.dll</c> via P/Invoke. The full
/// implementation requires carefully crafted GUID/USB_DESCRIPTOR dance —
/// rather than re-implement that in C#, this class shells out to a small
/// command-line tool that the user can drop into <c>vendor\usb-switch\</c>.
/// </para>
/// <para>
/// If the helper executable isn't present, the API gracefully degrades to
/// <see cref="UsbConfigurationResult.NotSupported"/> so the UI can show a
/// sensible message instead of crashing.
/// </para>
/// </remarks>
public sealed class StubUsbConfigurationSwitch
{
    private readonly ILogger<StubUsbConfigurationSwitch> _logger;

    public StubUsbConfigurationSwitch()
        : this(RidersLogger.Create<StubUsbConfigurationSwitch>())
    {
    }

    public StubUsbConfigurationSwitch(ILogger<StubUsbConfigurationSwitch> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Apply <paramref name="configuration"/> to the device with the
    /// given hardware id. Returns the outcome — never throws.
    /// </summary>
    public Task<UsbConfigurationResult> ApplyAsync(
        string hardwareId,
        UsbConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hardwareId))
        {
            return Task.FromResult(UsbConfigurationResult.InvalidArguments);
        }

        _logger.LogInformation(
            "Request to switch USB configuration for {HardwareId} to {Configuration}",
            hardwareId, configuration);

        // The full SetupAPI dance lives in a separate native helper so we
        // can keep this DLL fully managed. We expose the entry points here
        // for completeness; the actual call is forwarded to the helper
        // process so misbehaving drivers don't crash Riders Mirroring.
        try
        {
            // Placeholder for the real implementation. The native helper
            // would be invoked here via Process.Start with appropriate
            // arguments, then we parse its exit code into the enum.
            return Task.FromResult(UsbConfigurationResult.NotSupported);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "USB configuration switch failed for {HardwareId}", hardwareId);
            return Task.FromResult(UsbConfigurationResult.Failed);
        }
    }

    /// <summary>
    /// Resolve the hardware id for a given ADB serial by querying the
    /// SetupAPI device tree. Currently a stub — full implementation will
    /// walk the device tree starting from the <c>USB\VID_xxxx&amp;PID_xxxx</c>
    /// node returned by ADB.
    /// </summary>
    public Task<string?> ResolveHardwareIdAsync(string serial, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(serial);
        return Task.FromResult<string?>(null);
    }

    // --------------------------------------------------------------------
    // SetupAPI bindings (kept here for documentation — not yet wired up)
    // --------------------------------------------------------------------

    private const string SetupApi = "setupapi.dll";

    // ReSharper disable UnusedMember.Local
    [DllImport(SetupApi, CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetupDiGetClassDevs(
        IntPtr classGuid,
        string? enumerator,
        IntPtr hwndParent,
        uint flags);

    [DllImport(SetupApi, SetLastError = true)]
    private static extern bool SetupDiEnumDeviceInfo(
        IntPtr deviceInfoSet,
        uint memberIndex,
        IntPtr deviceInfoData);
    // ReSharper restore UnusedMember.Local
}

/// <summary>Target USB configuration requested by the caller.</summary>
public enum UsbConfiguration
{
    /// <summary>WinUSB (ADB-friendly).</summary>
    WinUsb,

    /// <summary>Android composite (MTP + ADB).</summary>
    AndroidComposite,

    /// <summary>Charge only.</summary>
    ChargeOnly,
}

/// <summary>Outcome of a USB configuration switch attempt.</summary>
public enum UsbConfigurationResult
{
    Applied,
    Failed,
    NotSupported,
    InvalidArguments,
}