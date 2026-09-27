using QRCoder;

namespace Riders.Mirroring.Core.Wireless;

/// <summary>
/// Thin wrapper around QRCoder so the rest of the app doesn't have to know
/// which QR library we picked. Returns a PNG byte array — easy to consume
/// from a WPF <see cref="System.Windows.Media.Imaging.BitmapImage"/>.
/// </summary>
public sealed class WirelessQrEncoder
{
    /// <summary>PNG payload size in pixels per side.</summary>
    public int PixelsPerModule { get; }

    public WirelessQrEncoder(int pixelsPerModule = 20)
    {
        if (pixelsPerModule < 4 || pixelsPerModule > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(pixelsPerModule),
                "Should be between 4 and 100 — below that the code is unreadable, above that it's huge.");
        }

        PixelsPerModule = pixelsPerModule;
    }

    /// <summary>
    /// Render the payload into a PNG byte array. Throws if the payload is
    /// empty or if QRCoder can't encode it (usually: too long for the
    /// default error-correction level).
    /// </summary>
    public byte[] EncodePng(string payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            throw new ArgumentException("Payload is required.", nameof(payload));
        }

        using var generator = new QRCodeGenerator();
        using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M);
        using var png = new PngByteQRCode(data);
        return png.GetGraphic(PixelsPerModule);
    }
}