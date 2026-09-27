namespace Riders.Mirroring.Core.AirPlay;

/// <summary>
/// One iPhone / iPad currently mirroring its screen to the local
/// AirPlay receiver. Built from the lines UxPlay emits on stdout.
/// </summary>
/// <remarks>
/// UxPlay's output is intentionally lossy — it doesn't surface a stable
/// device id. We use the iPhone's reported <c>name</c> as the display
/// label, and a monotonic counter to disambiguate multiple devices with
/// the same name.
/// </remarks>
public sealed record AirPlaySession(
    string DeviceName,
    int SessionId,
    DateTime ConnectedAt,
    string? RemoteEndpoint)
{
    /// <summary>
    /// Stable identifier for the UI (also stable across
    /// reconnect-with-the-same-name thanks to the <see cref="SessionId"/>).
    /// </summary>
    public string Id => $"{DeviceName}#{SessionId}";

    /// <summary>Display label for the sidebar.</summary>
    public string DisplayName =>
        string.IsNullOrEmpty(DeviceName) ? $"AirPlay session {SessionId}" : DeviceName;
}