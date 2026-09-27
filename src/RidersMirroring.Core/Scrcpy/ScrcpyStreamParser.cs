using System.Buffers.Binary;
using System.Text;

namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Stateless parser for the scrcpy video stream header. Pure, side-effect
/// free, unit-testable.
/// </summary>
/// <remarks>
/// The actual NAL unit decoding is delegated to <see cref="IScrcpyVideoDecoder"/>;
/// this type only frames the metadata that the decoder needs to allocate its
/// output buffers.
/// </remarks>
public static class ScrcpyStreamParser
{
    /// <summary>
    /// Try to read a <see cref="ScrcpyStreamHeader"/> from <paramref name="stream"/>.
    /// Returns <c>false</c> if the stream doesn't have enough bytes yet
    /// (caller should read more and try again).
    /// </summary>
    public static bool TryReadHeader(Stream stream, out ScrcpyStreamHeader? header, out int bytesConsumed)
    {
        header = null;
        bytesConsumed = 0;

        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        // 1. device_name length
        Span<byte> lenBuf = stackalloc byte[4];
        if (!TryReadExact(stream, lenBuf))
        {
            return false;
        }

        var nameLen = BinaryPrimitives.ReadUInt32LittleEndian(lenBuf);
        bytesConsumed += 4;

        if (nameLen > 1024)
        {
            throw new InvalidDataException(
                $"scrcpy device name length {nameLen} exceeds sanity limit (1024).");
        }

        // 2. device_name
        var nameBuf = new byte[nameLen];
        if (!TryReadExact(stream, nameBuf))
        {
            return false;
        }

        var deviceName = Encoding.UTF8.GetString(nameBuf);
        bytesConsumed += (int)nameLen;

        // 3. codec_id length
        if (!TryReadExact(stream, lenBuf))
        {
            return false;
        }

        var codecLen = BinaryPrimitives.ReadUInt32LittleEndian(lenBuf);
        bytesConsumed += 4;

        if (codecLen > 32)
        {
            throw new InvalidDataException(
                $"scrcpy codec id length {codecLen} exceeds sanity limit (32).");
        }

        // 4. codec_id
        var codecBuf = new byte[codecLen];
        if (!TryReadExact(stream, codecBuf))
        {
            return false;
        }

        var codecId = Encoding.UTF8.GetString(codecBuf);
        bytesConsumed += (int)codecLen;

        // 5. width / height
        Span<byte> whBuf = stackalloc byte[8];
        if (!TryReadExact(stream, whBuf))
        {
            return false;
        }

        var width  = (int)BinaryPrimitives.ReadUInt32LittleEndian(whBuf[..4]);
        var height = (int)BinaryPrimitives.ReadUInt32LittleEndian(whBuf.Slice(4, 4));
        bytesConsumed += 8;

        if (width <= 0 || height <= 0 || width > 8192 || height > 8192)
        {
            throw new InvalidDataException(
                $"scrcpy stream reports implausible dimensions: {width}x{height}.");
        }

        header = new ScrcpyStreamHeader(deviceName, codecId, width, height);
        return true;
    }

    /// <summary>
    /// Build a well-formed scrcpy stream header for tests / fakes.
    /// </summary>
    public static byte[] BuildHeader(string deviceName, string codecId, int width, int height)
    {
        var nameBytes = Encoding.UTF8.GetBytes(deviceName);
        var codecBytes = Encoding.UTF8.GetBytes(codecId);

        var buffer = new byte[4 + nameBytes.Length + 4 + codecBytes.Length + 8];
        var span = buffer.AsSpan();

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)nameBytes.Length);
        span = span[4..];
        nameBytes.CopyTo(span);
        span = span[nameBytes.Length..];

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)codecBytes.Length);
        span = span[4..];
        codecBytes.CopyTo(span);
        span = span[codecBytes.Length..];

        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)width);
        span = span[4..];
        BinaryPrimitives.WriteUInt32LittleEndian(span, (uint)height);

        return buffer;
    }

    private static bool TryReadExact(Stream stream, Span<byte> buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer[total..]);
            if (read <= 0)
            {
                return false; // EOF before header complete
            }
            total += read;
        }

        return true;
    }
}
