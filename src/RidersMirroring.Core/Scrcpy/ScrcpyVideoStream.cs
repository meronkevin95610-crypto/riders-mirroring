using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Glue between the scrcpy video <see cref="Stream"/> and an
/// <see cref="IScrcpyVideoDecoder"/>. Reads the header off the wire,
/// feeds NAL packets into the decoder, and surfaces decoded frames to the UI.
/// </summary>
/// <remarks>
/// We deliberately keep the network/decoder boundary async so the WPF UI
/// thread never blocks on a read. The reader runs on the thread pool; the
/// decoder implementation is responsible for marshalling its
/// <see cref="IScrcpyVideoDecoder.FrameDecoded"/> events to the UI thread
/// if needed.
/// </remarks>
public sealed class ScrcpyVideoStream : IAsyncDisposable
{
    private readonly Stream _stream;
    private readonly IScrcpyVideoDecoder _decoder;
    private readonly ILogger<ScrcpyVideoStream> _logger;
    private readonly CancellationTokenSource _cts = new();

    private Task? _readerLoop;
    private ScrcpyStreamHeader? _header;

    /// <summary>
    /// Raised when the reader encounters an unrecoverable error (EOF mid-packet,
    /// malformed length, decoder exception). Subscribers should stop the
    /// mirroring session and surface a user-facing message.
    /// </summary>
    public event EventHandler<ScrcpyStreamErrorEventArgs>? StreamError;

    /// <summary>
    /// When <c>true</c>, <see cref="ReadLoopAsync"/> skips the scrcpy
    /// metadata-header read and forwards raw bytes (post-frame-meta framing)
    /// directly to <see cref="IScrcpyVideoDecoder"/>. This is the v4.0
    /// <c>raw_stream=true</c> mode — the consumer must pre-Prime the
    /// decoder with a fabricated <see cref="ScrcpyStreamHeader"/> because
    /// the server no longer sends codec/width/height on the wire.
    /// </summary>
    /// <remarks>
    /// Defaults to <c>false</c> so <see cref="Riders.Mirroring.Desktop.ViewModels.MirrorViewModel"/>
    /// (which uses the legacy <see cref="ScrcpyMirrorProcess"/> path via
    /// <see cref="ScrcpyStreamParser"/>) is unaffected. The 141 Core.Tests
    /// rely on this default.
    /// </remarks>
    public bool UseRawStream { get; set; } = false;

    public ScrcpyVideoStream(Stream stream, IScrcpyVideoDecoder decoder)
        : this(stream, decoder, RidersLogger.Create<ScrcpyVideoStream>())
    {
    }

    public ScrcpyVideoStream(Stream stream, IScrcpyVideoDecoder decoder, ILogger<ScrcpyVideoStream> logger)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        _decoder = decoder ?? throw new ArgumentNullException(nameof(decoder));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Stream metadata, available once the header has been parsed.</summary>
    public ScrcpyStreamHeader? Header => _header;

    /// <summary>Underlying decoder; subscribe to <see cref="IScrcpyVideoDecoder.FrameDecoded"/>.</summary>
    public IScrcpyVideoDecoder Decoder => _decoder;

    /// <summary>
    /// Kick off the read loop. Returns immediately; frames arrive via
    /// <see cref="IScrcpyVideoDecoder.FrameDecoded"/>.
    /// </summary>
    public Task StartAsync()
    {
        if (_readerLoop is not null)
        {
            throw new InvalidOperationException("Stream is already running.");
        }

        _readerLoop = Task.Run(ReadLoopAsync);
        return Task.CompletedTask;
    }

    private async Task ReadLoopAsync()
    {
        try
        {
            if (!UseRawStream)
            {
                // 1. Legacy path: Read header off the wire.
                if (!ScrcpyStreamParser.TryReadHeader(_stream, out var header, out _))
                {
                    _logger.LogWarning("scrcpy stream closed before header was complete");
                    StreamError?.Invoke(this, new ScrcpyStreamErrorEventArgs(
                        "Stream closed before the scrcpy header could be read.",
                        ScrcpyStreamErrorKind.EndOfStream));
                    return;
                }

                _header = header ?? throw new InvalidDataException("scrcpy header parser returned null header.");
                _logger.LogInformation(
                    "scrcpy header parsed: device={Device} codec={Codec} {Width}x{Height}",
                    _header.DeviceName, _header.CodecId, _header.Width, _header.Height);

                if (!_header.IsSupportedCodec)
                {
                    _logger.LogError("Unsupported scrcpy codec '{Codec}'", _header.CodecId);
                    return;
                }

                // Prime the decoder with metadata so it can allocate output buffers.
                _decoder.Prime(_header);
            }
            else
            {
                // Raw v4.0 path: NO device-name / codec-id / width / height
                // on the wire. The consumer is expected to have already
                // called _decoder.Prime(...) with a fabricated header derived
                // from the server's --video-* options. We just forward bytes.
                _logger.LogInformation("scrcpy stream in RAW mode — skipping header parse, forwarding bytes verbatim");
            }

            // 2. Stream body. The scrcpy protocol frames NAL units by
            // prefixing each chunk with a 4-byte big-endian length.
            var lengthBuf = new byte[4];
            while (!_cts.IsCancellationRequested)
            {
                if (!TryReadExact(_stream, lengthBuf))
                {
                    // EOF is normal at end-of-stream but should be visible —
                    // a USB unplug mid-mirror looks identical and we want the
                    // operator to see that in the log.
                    _logger.LogWarning("scrcpy stream closed unexpectedly (EOF before next packet length)");
                    StreamError?.Invoke(this, new ScrcpyStreamErrorEventArgs(
                        "Stream closed unexpectedly.",
                        ScrcpyStreamErrorKind.EndOfStream));
                    break;
                }

                var packetLen = (lengthBuf[0] << 24)
                              | (lengthBuf[1] << 16)
                              | (lengthBuf[2] << 8)
                              |  lengthBuf[3];

                if (packetLen <= 0 || packetLen > 16 * 1024 * 1024)
                {
                    var msg = $"scrcpy packet length {packetLen} out of range";
                    _logger.LogWarning("{Message}", msg);
                    StreamError?.Invoke(this, new ScrcpyStreamErrorEventArgs(
                        msg,
                        ScrcpyStreamErrorKind.MalformedFrame));
                    break;
                }

                var packet = new byte[packetLen];
                if (!TryReadExact(_stream, packet))
                {
                    var msg = $"scrcpy stream truncated mid-packet ({packetLen} bytes expected)";
                    _logger.LogWarning("{Message}", msg);
                    StreamError?.Invoke(this, new ScrcpyStreamErrorEventArgs(
                        msg,
                        ScrcpyStreamErrorKind.EndOfStream));
                    break;
                }

                await _decoder.PushPacketAsync(packet, _cts.Token).ConfigureAwait(false);
            }

            await _decoder.FlushAsync(_cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "scrcpy stream read loop crashed");
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
        _cts.Cancel();
        if (_readerLoop is not null)
        {
            try
            {
                await _readerLoop.ConfigureAwait(false);
            }
            catch
            {
                // swallow — disposal must not throw
            }
        }

        await _decoder.DisposeAsync().ConfigureAwait(false);
        _cts.Dispose();
    }
}
