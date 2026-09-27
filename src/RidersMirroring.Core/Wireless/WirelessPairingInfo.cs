namespace Riders.Mirroring.Core.Wireless;

/// <summary>
/// Everything we need to display a Wi-Fi pairing card and render the QR
/// code the user will scan on their Android device.
/// </summary>
/// <remarks>
/// The payload format follows Android's "Wi-Fi network QR code" scheme:
/// <c>WIFI:T:WPA;S:&lt;ssid&gt;;P:&lt;password&gt;;H:false;;</c>. See
/// <see href="https://github.com/zxing/zxing/wiki/QR-Code-Encoding">
/// zxing's reference</see>.
/// </remarks>
/// <param name="Ssid">Wi-Fi SSID to connect to (typically the home network).</param>
/// <param name="Password">Pre-shared key (clear-text — the QR code is the secure channel).</param>
/// <param name="HostEndpoint">Endpoint (<c>host:port</c>) the device should dial
/// after joining the network.</param>
/// <param name="IsHidden">Whether the SSID is hidden (rare — defaults to false).</param>
public sealed record WirelessPairingInfo(
    string Ssid,
    string Password,
    string HostEndpoint,
    bool IsHidden = false)
{
    /// <summary>
    /// Build the text payload that will be encoded into the QR code.
    /// Special characters in the SSID / password are escaped per the
    /// <c>WIFI:</c> URI scheme.
    /// </summary>
    public string ToQrPayload()
    {
        // Escape: backslash, semicolon, comma, single quote, colon, double quote.
        static string Escape(string s) => s
            .Replace(@"\", @"\\")
            .Replace(";",  @"\;")
            .Replace(",",  @"\,")
            .Replace(":",  @"\:")
            .Replace("\"", "\\\"")
            .Replace("'",  @"\'");

        return $"WIFI:T:WPA;S:{Escape(Ssid)};P:{Escape(Password)};H:{(IsHidden ? "true" : "false")};;";
    }
}