using System.IO;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.Logging;
using Riders.Mirroring.Core.Wireless;

namespace Riders.Mirroring.Desktop.ViewModels;

/// <summary>
/// State and behaviour of the "Wireless Host" pane: lets the user type the
/// Wi-Fi SSID / password, generates a QR code, and shows the local IP and
/// ADB port the device should dial.
/// </summary>
public sealed partial class WirelessHostViewModel : ObservableObject
{
    private readonly WirelessHostService _service;
    private readonly ILogger<WirelessHostViewModel> _logger;

    [ObservableProperty]
    private string _ssid = string.Empty;

    // Wi-Fi pre-shared key. Held in a private field (not [ObservableProperty])
    // so it never surfaces through INotifyPropertyChanged — debug Live Property
    // Watchers, dump tools, and PropertyChanged event listeners won't see it.
    // Read access goes through the Password property; writes go through
    // SetPassword(string) which is called by the PasswordBox change handler
    // in WirelessHostView.xaml.cs.
    private string? _password;

    /// <summary>
    /// Read-only view of the current password. The setter is intentionally
    /// absent — use <see cref="SetPassword(string)"/> instead. This property
    /// is *not* decorated with <c>[ObservableProperty]</c>, so changes do not
    /// raise <c>PropertyChanged</c>; the password is never broadcast to
    /// listeners or visible in any UI binding target.
    /// </summary>
    public string Password => _password ?? string.Empty;

    /// <summary>
    /// Update the cached password. Clears the previous value to limit the
    /// window of exposure. Does not raise <c>PropertyChanged</c>.
    /// </summary>
    internal void SetPassword(string value)
    {
        _password = value ?? string.Empty;
        GenerateCommand.NotifyCanExecuteChanged();
    }

    [ObservableProperty]
    private string _hostEndpoint = "—";

    [ObservableProperty]
    private BitmapSource? _qrImage;

    [ObservableProperty]
    private string _statusMessage = "Enter your Wi-Fi details, then press Generate.";

    [ObservableProperty]
    private bool _isBusy;

    public WirelessHostViewModel(IAdbServerManager adb)
    {
        ArgumentNullException.ThrowIfNull(adb);
        _service = new WirelessHostService(adb);
        _logger = RidersLogger.Create<WirelessHostViewModel>();
    }

    /// <summary>
    /// Resolve pairing info and render the QR code. Reports errors in
    /// <see cref="StatusMessage"/> instead of throwing — the user has to
    /// be able to read what went wrong.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanGenerate))]
    private async Task GenerateAsync()
    {
        try
        {
            IsBusy = true;
            StatusMessage = "Looking for the local network…";

            var info = await _service.ResolveAsync(Ssid, Password).ConfigureAwait(true);

            // Best-effort scrub of the cached password after it's been turned
            // into the QR payload. The local reference is the last trace; the
            // bytes have already left the VM inside `info` (and out to the
            // log via `info.Ssid` / `info.HostEndpoint` which never include
            // the password).
            SetPassword(string.Empty);
            HostEndpoint = info.HostEndpoint;
            StatusMessage = $"Scan the QR code with your Android device, then connect to {info.HostEndpoint}.";

            var png = new WirelessQrEncoder(pixelsPerModule: 12).EncodePng(info.ToQrPayload());
            QrImage = LoadBitmap(png);
            _logger.LogInformation(
                "Generated pairing QR for {Ssid} -> {Endpoint}",
                info.Ssid, info.HostEndpoint);
        }
        catch (ArgumentException ex)
        {
            StatusMessage = ex.Message;
            QrImage = null;
        }
        catch (InvalidOperationException ex)
        {
            StatusMessage = ex.Message;
            QrImage = null;
        }
        catch (QRCoder.Exceptions.DataTooLongException ex)
        {
            // QR code version can't hold the encoded payload. WPA3 with a
            // very long password or a 32-char unicode SSID will hit this.
            _logger.LogWarning(ex, "QR payload too long for SSID=***{Tail}", WirelessHostService.Tail(Ssid));
            StatusMessage = "SSID or password is too long for a QR code. Shorten them and retry.";
            QrImage = null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate pairing QR");
            StatusMessage = "Failed to generate QR code. See the log for details.";
            QrImage = null;
        }
        finally
        {
            IsBusy = false;
            GenerateCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanGenerate() => !IsBusy
                                  && !string.IsNullOrWhiteSpace(Ssid)
                                  && !string.IsNullOrWhiteSpace(_password);

    partial void OnSsidChanged(string value)        => GenerateCommand.NotifyCanExecuteChanged();

    /// <summary>
    /// Decode the PNG bytes into a frozen <see cref="BitmapSource"/> that
    /// WPF can bind to directly. Freezing makes the image shareable across
    /// threads without cloning.
    /// </summary>
    private static BitmapSource LoadBitmap(byte[] png)
    {
        using var stream = new MemoryStream(png);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }
}