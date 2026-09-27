using System.Text;

namespace Riders.Mirroring.Core.AirPlay.Rtsp;

/// <summary>
/// Tiny RTSP request/response parser scoped to the subset of RFC 2326 + the
/// AirPlay-specific extensions actually used by iOS 14+.
/// </summary>
/// <remarks>
/// We deliberately ignore SDP/RTP body parsing for the moment — AirPlay
/// negotiates session parameters entirely through response headers and
/// embeds them in binary fields at the start of each RTP packet. Real clients
/// use ANNOUNCE, SETUP, RECORD, plus the unusual TEARDOWN-with-feedback
/// variant; we focus on SETUP, RECORD, TEARDOWN, GET_PARAMETER.
/// </remarks>
public static class RtspParser
{
    /// <summary>Parse an incoming RTSP request from the wire.</summary>
    public static bool TryParseRequest(ReadOnlySpan<byte> buffer, out RtspRequest request, out int consumedBytes)
    {
        request = default!;
        consumedBytes = 0;

        // Find end of header block (\r\n\r\n).
        var headerEnd = IndexOfDoubleCrlf(buffer);
        if (headerEnd < 0)
        {
            return false; // need more bytes
        }

        var headerSpan = buffer[..headerEnd];
        var lines = SplitLines(headerSpan);

        if (lines.Length == 0)
        {
            return false;
        }

        // Request line: "METHOD URI RTSP/1.0\r\n"
        var firstLine = lines[0];
        var firstLineText = Encoding.ASCII.GetString(firstLine);
        var parts = firstLineText.Split(' ', 3);
        if (parts.Length < 3 || !parts[2].StartsWith("RTSP/", StringComparison.Ordinal))
        {
            return false;
        }

        var method = parts[0];
        var uri = parts[1];

        // Headers
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            var colon = lines[i].IndexOf((byte)':');
            if (colon < 0) continue;
            var name = Encoding.ASCII.GetString(lines[i][..colon]).Trim();
            var value = Encoding.ASCII.GetString(lines[i][(colon + 1)..]).Trim();
            headers[name] = value;
        }

        // Body (after the \r\n\r\n)
        var bodyLength = headers.TryGetValue("Content-Length", out var cl) && int.TryParse(cl, out var l) ? l : 0;
        if (buffer.Length < headerEnd + 4 + bodyLength)
        {
            return false; // body not yet received
        }

        request = new RtspRequest(method, uri, headers,
 bodyLength > 0 ? buffer.Slice(headerEnd + 4, bodyLength).ToArray() : Array.Empty<byte>());
        consumedBytes = headerEnd + 4 + bodyLength;
        return true;
    }

    /// <summary>Build an RTSP response with the given status code + optional headers.</summary>
    public static byte[] BuildResponse(
        int statusCode,
        string reason,
        IReadOnlyDictionary<string, string>? headers = null,
        ReadOnlyMemory<byte>? body = null)
    {
        var sb = new StringBuilder(256);
        sb.Append("RTSP/1.0 ").Append(statusCode).Append(' ').Append(reason).Append("\r\n");
        sb.Append("CSeq: ").Append(headers?.TryGetValue("CSeq", out var c) == true ? c : "0").Append("\r\n");
        sb.Append("Server: AirTunes/760.6.5\r\n");
        if (headers is not null)
        {
            foreach (var (k, v) in headers)
            {
                if (k.Equals("CSeq", StringComparison.OrdinalIgnoreCase)) continue;
                sb.Append(k).Append(": ").Append(v).Append("\r\n");
            }
        }
        if (body.HasValue && body.Value.Length > 0)
        {
            sb.Append("Content-Length: ").Append(body.Value.Length).Append("\r\n");
        }
        sb.Append("\r\n");

        var headBytes = Encoding.ASCII.GetBytes(sb.ToString());
        if (body is null || body.Value.Length == 0)
        {
            return headBytes;
        }
        var combined = new byte[headBytes.Length + body.Value.Length];
        Buffer.BlockCopy(headBytes, 0, combined, 0, headBytes.Length);
        body.Value.Span.CopyTo(combined.AsSpan(headBytes.Length));
        return combined;
    }

    /// <summary>Extract the value of a single header (case-insensitive).</summary>
    public static string? GetHeader(this IReadOnlyDictionary<string, string> headers, string name)
    {
        return headers.TryGetValue(name, out var v) ? v : null;
    }

    /// <summary>Get the CSeq header as an integer, or 0 if missing.</summary>
    public static int GetCSeq(this IReadOnlyDictionary<string, string> headers)
    {
        return headers.TryGetValue("CSeq", out var s) && int.TryParse(s, out var n) ? n : 0;
    }

    private static int IndexOfDoubleCrlf(ReadOnlySpan<byte> buffer)
    {
        for (int i = 0; i < buffer.Length - 3; i++)
        {
            if (buffer[i] == (byte)'\r' && buffer[i + 1] == (byte)'\n'
                && buffer[i + 2] == (byte)'\r' && buffer[i + 3] == (byte)'\n')
            {
                return i;
            }
        }
        return -1;
    }

    private static byte[][] SplitLines(ReadOnlySpan<byte> headerBlock)
    {
        var lines = new List<byte[]>();
        int start = 0;
        for (int i = 0; i < headerBlock.Length - 1; i++)
        {
            if (headerBlock[i] == (byte)'\r' && headerBlock[i + 1] == (byte)'\n')
            {
                lines.Add(headerBlock.Slice(start, i - start).ToArray());
                start = i + 2;
                i++; // skip \n
            }
        }
        if (start < headerBlock.Length)
        {
            lines.Add(headerBlock.Slice(start).ToArray());
        }
        return lines.ToArray();
    }
}

/// <summary>Parsed RTSP request from an iPhone.</summary>
public sealed record RtspRequest(string Method, string Uri, IReadOnlyDictionary<string, string> Headers, byte[] Body);