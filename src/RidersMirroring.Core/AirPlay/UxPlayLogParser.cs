using System.Text.RegularExpressions;

namespace Riders.Mirroring.Core.AirPlay;

/// <summary>
/// Translates UxPlay's stdout into <see cref="AirPlaySession"/> lifecycle
/// events. Pure / stateless / unit-testable.
/// </summary>
/// <remarks>
/// UxPlay doesn't print a strict machine-readable log. We look for known
/// "device connected" / "device disconnected" sentences and treat anything
/// else as a generic status line.
/// </remarks>
public static class UxPlayLogParser
{
    // UxPlay 1.72 prints lines like:
    //   [1013/141026.942:INFO:CONSOLE(182)] "VIDEO has been received from \"Bob's iPhone\""
    // Earlier versions print plain English. We accept both shapes:
    // with quotes (Chromium-style) or without (plain).
    // Two alternates:
    //   - quoted form: capture everything between the outer quotes (spaces allowed).
    //   - plain form: capture a single whitespace-free token (no spaces).
    private static readonly Regex ConnectedRegex = new(
        @"received from\s+(?:""(?<name>[^""]+)""|(?<name>\S+))",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex DisconnectedRegex = new(
        @"(?:disconnected|disconnect from)\s+(?:""(?<name>[^""]+)""|(?<name>\S+))",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Try to extract a connected-device name from <paramref name="line"/>.
    /// Returns <c>null</c> if the line doesn't match.
    /// </summary>
    public static string? TryExtractConnectedDevice(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        // Chromium-style logs escape inner quotes with a backslash. Strip them
        // before matching so "VIDEO ... from \"Bob's iPhone\"" becomes
        // "VIDEO ... from "Bob's iPhone"" which the regex can parse.
        var sanitized = line.Replace(@"\""", "\"");

        var match = ConnectedRegex.Match(sanitized);
        return match.Success ? match.Groups["name"].Value : null;
    }

    /// <summary>
    /// Try to extract a disconnected-device name from <paramref name="line"/>.
    /// Returns <c>null</c> if the line doesn't match.
    /// </summary>
    public static string? TryExtractDisconnectedDevice(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return null;
        }

        var sanitized = line.Replace(@"\""", "\"");
        var match = DisconnectedRegex.Match(sanitized);
        return match.Success ? match.Groups["name"].Value : null;
    }

    /// <summary>True when the line suggests the receiver is ready for clients.</summary>
    public static bool LooksLikeReadyLine(string line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return false;
        }

        // UxPlay: "Initialized" / "Listening for connections" / "Server initialized"
        return line.Contains("Server initialized", StringComparison.OrdinalIgnoreCase)
            || line.Contains("Initialized",       StringComparison.OrdinalIgnoreCase)
            || line.Contains("listening",         StringComparison.OrdinalIgnoreCase)
            || line.Contains("Listening",         StringComparison.OrdinalIgnoreCase);
    }
}