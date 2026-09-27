using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Audio counterpart of <see cref="ScrcpyVideoStream"/>. Reads the 4-byte
/// codec-id header off the audio socket, then forwards 12-byte framed
/// media packets into an <see cref="IScrcpyAudioDecoder"/>.
///
/// <para>
/// scrcpy v4 audio wire format (raw_stream mode):
/// <code>
///   [0..4]   codec_id ("opus", "aac", "flac", "raw")
///   repeat:
///     [0..8]    PTS (uint64 LE — high bits are flags in the VIDEO stream
///               but unused here; audio has no session packets)
///     [8..12]   packet size (uint32 LE)
///     [12..12+size]  media packet payload
/// </code>
/// </para>
/// </summary>
public sealed class ScrcpyAudioStream : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly IScrcpyAudioDecoder _decoder;
    private readonly ILogger<ScrcpyAudioStream> _logger;
    private readonly CancellationTokenSource _cts = new();

    private Task? _readerLoop;
    private ScrcpyAudioStreamHeader? _header;

    /// <summary>Raised on unrecoverable read errors. See <see cref="ScrcpyVideoStream.StreamError"/>.</summary>
    public event EventHandler<ScrcpyStreamErrorEventArgs>? StreamError;

    public ScrcpyAudioStream(Stream stream, IScrcpyAudioDecoder decoder)
        : this(stream, decoder, RidersLogger.Create<ScrcpyAudioStream>())
    {
    }

    public ScrcpyAudioStream(Stream stream, IScrcpyAudioDecoder decoder, ILogger<ScrcpyAudioStream> logger)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Stream metadata, available once the codec-id has been read.</summary>
    public ScrcpyAudioStreamHeader? Header => _header;

    /// <summary>Underlying decoder; subscribe to <see cref="IScrcpyAudioDecoder.AudioPacketDecoded"/>.</summary>
    public IScrcpyAudioDecoder Decoder => _decoder;

    /// <summary>
    /// Kick off the read loop. Returns immediately; PCM frames arrive via
    /// <see cref="IScrcpyAudioDecoder.AudioPacketDecoded"/>.
    /// </summary>
    public Task StartAsync()
    {
        if (_readerLoop is not null)
        {
            throw new InvalidOperationException("Audio stream is already running.");
        }

        _readerLoop = Task.Run(ReadLoopAsync);
        return Task.CompletedTask;
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            // 1. Read the 4-byte codec id (little-endian uint32).
            var codecBuf = new byte[4];
            if (!TryReadExact(_stream, codecBuf))
            {
                _logger.LogWarning("scrcpy audio stream closed before codec id was sent");
                StreamError?.Invoke(this, new ScrcpyStreamErrorEventArgs(
                    "Audio stream closed before codec id was sent.",
                    ScrcpyStreamErrorKind.EndOfStream));
                return;
            }

            // scrcpy sends codec ids as ASCII bytes ("opus", "aac", "flac", "raw")
            // — NOT as a uint32. Read them as a string instead.
            var codecId = System.Text.Encoding.ASCII.GetString(codecBuf).TrimEnd('\0', ' ');
            _header = new ScrcpyAudioStreamHeader(codecId);
            _logger.LogInformation(
                "scrcpy audio header parsed: codec={Codec}",
                codecId);

            if (!_header.IsSupportedCodec)
            {
                _logger.LogError("Unsupported scrcpy audio codec '{Codec}'", codecId);
                StreamError?.Invoke(this, new ScrcpyStreamErrorEventArgs(
                    $"Unsupported audio codec '{codecId}'.",
                    ScrcpyStreamErrorKind.MalformedFrame));
                return;
            }

            _decoder.Prime(_header);

            // 2. Stream body: 12-byte framed media packets.
            var frameHeader = new byte[12];
            while (!_cts.IsCancellationRequested)
            {
                if (!TryReadExact(_stream, frameHeader))
                {
                    _logger.LogInformation("scrcpy audio stream ended cleanly (EOF after last packet)");
                    break;
                }

                // PTS = first 8 bytes (uint64 LE). Audio has no session bit.
                var pts = BitConverter.ToUInt64(frameHeader, 0);
                var packetLen = (frameHeader[8] << 24)
                              | (frameHeader[9] << 16)
                              | (frameHeader[10] << 8)
                              |  frameHeader[11];

                if (packetLen <= 0 || packetLen > 16 * 1024 * 1024)
                {
                    var msg = $"scrcpy audio packet length {packetLen} out of range";
                    _logger.LogWarning("{Message}", msg);
                    StreamError?.Invoke(this, new ScrcpyStreamErrorEventArgs(
                        msg,
                        ScrcpyStreamErrorKind.MalformedFrame));
                    break;
                }

                var packet = new byte[packetLen];
                if (!TryReadExact(_stream, packet))
                {
                    var msg = $"scrcpy audio stream truncated mid-packet ({packetLen} bytes expected)";
                    _logger.LogWarning("{Message}", msg);
                    StreamError?.Invoke(this, new ScrcpyStreamErrorEventArgs(
                        msg,
                        ScrcpyStreamErrorKind.EndOfStream));
                    break;
                }

                var presentationTime = pts == ulong.MaxValue
                    ? TimeSpan.Zero
                    : TimeSpan.FromMilliseconds(pts / 1_000_000.0);
                await _decoder.PushPacketAsync(packet, presentationTime, _cts.Token)
                    .ConfigureAwait(false);
            }

            await _decoder.FlushAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "scrcpy audio stream read loop crashed");
            StreamError?.Invoke(this, new ScrcpyStreamErrorEventArgs(
                ex.Message,
                ScrcpyStreamErrorKind.DecoderFailure));
        }
    }

    private static bool TryReadExact(Stream stream, byte[] buffer)
    {
        var total = 0;
        while (total < buffer.Length)
        {
            var read = stream.Read(buffer, total, buffer.Length - total);
            if (read <= 0)
            {
                return false;
            }
            total += read;
        }
        return true;
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        try { _cts.Cancel(); } catch { /* swallow */ }
        if (_readerLoop is not null)
        {
            try { await _readerLoop.ConfigureAwait(false); }
            catch { /* swallow */ }
        }
        _cts.Dispose();
    }
}
