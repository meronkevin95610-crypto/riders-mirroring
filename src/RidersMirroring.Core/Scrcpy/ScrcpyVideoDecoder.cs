using System.Runtime.InteropServices;
using FFmpeg.AutoGen;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Production <see cref="IScrcpyVideoDecoder"/> backed by FFmpeg via the
/// <see href="https://github.com/Ruslan-B/FFmpeg.AutoGen">FFmpeg.AutoGen</see>
/// P/Invoke bindings.
///
/// <para>
/// Threading: all FFmpeg state is owned by a single thread — the one that
/// invokes <see cref="PushPacketAsync"/>. Callers must serialise calls
/// (this is satisfied by <see cref="ScrcpyVideoStream"/> which already
/// reads on a single background thread). The implementation does NOT
/// lock internally — concurrent access would corrupt the FFmpeg context.
/// </para>
/// </summary>
/// <remarks>
/// Pinned to FFmpeg.AutoGen 8.1.0 (FFmpeg 8.1.x). The redistributed DLLs
/// in <c>vendor/ffmpeg/bin/</c> (pinned via
/// <c>vendor/ffmpeg/.known-good</c>) MUST match this major; otherwise
/// P/Invoke signatures change and the runtime crashes during the first
/// frame.
/// </remarks>
public sealed unsafe class ScrcpyVideoDecoder : IScrcpyVideoDecoder
{
    private readonly ILogger<ScrcpyVideoDecoder> _logger;

    // FFmpeg state. Mutable only on the decoder thread.
    private AVCodecContext* _codecContext;
    private SwsContext* _swsContext;
    private AVFrame* _frame;
    private AVPacket* _packet;

    /// <summary>
    /// Monotonic clock started at the first successful <see cref="Prime"/>.
    /// Used as a fallback PTS when <c>_frame-&gt;pts</c> is
    /// <c>AV_NOPTS_VALUE</c> (typical for raw H.264 streams without
    /// container timestamps). Keeps subscriber code from having to
    /// special-case "no PTS".
    /// </summary>
    private readonly System.Diagnostics.Stopwatch _wallClock =
        new System.Diagnostics.Stopwatch();

    // Reusable BGRA staging buffer. Allocated in Prime(); reused across frames.
    private byte* _bgraBuffer;
    private int _bgraStride;
    private int _bgraBufferSize;

    // Pre-allocated native arrays that sws_scale 7.x consumes directly:
    // it expects `byte** srcData` and `int* srcStride` (raw pointers),
    // plus a single destination pair. We allocate these in Prime()
    // and mutate their contents per-frame — this avoids any per-frame
    // managed allocations or GCHandle pinning.
    private byte** _srcDataPtrs;
    private int* _srcStridePtrs;
    private byte** _dstDataPtrs;
    private int* _dstStridePtrs;

    private bool _disposed;

    /// <inheritdoc/>
    public bool IsReady => _codecContext is not null && !_disposed;

    /// <inheritdoc/>
    public ScrcpyStreamHeader? Header { get; private set; }

    /// <inheritdoc/>
    public event EventHandler<DecodedVideoFrame>? FrameDecoded;

    public ScrcpyVideoDecoder()
        : this(RidersLogger.Create<ScrcpyVideoDecoder>())
    {
    }

    /// <summary>Test-friendly constructor.</summary>
    public ScrcpyVideoDecoder(ILogger<ScrcpyVideoDecoder> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public void Prime(ScrcpyStreamHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);
        if (_codecContext is not null)
        {
            _logger.LogDebug(
                "FFmpeg decoder already primed; ignoring re-prime ({Codec} {Width}x{Height})",
                header.CodecId, header.Width, header.Height);
            return;
        }

        Header = header;

        // Map scrcpy's codec_id to FFmpeg's codec id. We wire up H.264 here
        // — h265/av1 may share a path in the future, but the primary
        // deliverable today is H.264.
        AVCodecID ffmpegCodecId = header.CodecId.ToLowerInvariant() switch
        {
            "h264" => AVCodecID.AV_CODEC_ID_H264,
            "h265" => AVCodecID.AV_CODEC_ID_HEVC,
            "av1"  => AVCodecID.AV_CODEC_ID_AV1,
            _      => AVCodecID.AV_CODEC_ID_NONE,
        };
        if (ffmpegCodecId == AVCodecID.AV_CODEC_ID_NONE)
        {
            throw new NotSupportedException(
                $"scrcpy reported codec '{header.CodecId}' which is not handled by the FFmpeg-based decoder.");
        }

        var codec = ffmpeg.avcodec_find_decoder(ffmpegCodecId);
        if (codec is null)
        {
            throw new InvalidOperationException(
                $"FFmpeg could not locate a decoder for codec id {ffmpegCodecId}.");
        }

        var ctx = ffmpeg.avcodec_alloc_context3(codec);
        if (ctx is null)
        {
            throw new InvalidOperationException("avcodec_alloc_context3 returned NULL.");
        }
        _codecContext = ctx;

        // Surface pixel dimensions to the decoder so it can wire up
        // optimised paths internally.
        _codecContext->width  = header.Width;
        _codecContext->height = header.Height;
        _codecContext->pix_fmt = AVPixelFormat.AV_PIX_FMT_YUV420P;

        var openResult = ffmpeg.avcodec_open2(_codecContext, codec, null);
        if (openResult < 0)
        {
            // avcodec_free_context takes a ** pointer; capture in a local
            // and null out the field afterwards.
            var toFree = _codecContext;
            _codecContext = null;
            ffmpeg.avcodec_free_context(&toFree);
            throw new InvalidOperationException(
                $"avcodec_open2 failed with code {openResult} for codec {header.CodecId}.");
        }

        _frame = ffmpeg.av_frame_alloc();
        if (_frame is null)
        {
            throw new InvalidOperationException("av_frame_alloc returned NULL.");
        }

        _packet = ffmpeg.av_packet_alloc();
        if (_packet is null)
        {
            throw new InvalidOperationException("av_packet_alloc returned NULL.");
        }

        // sws_scale context: YUV420P (what scrcpy sends) -> BGRA (what WPF wants).
        // FFmpeg.AutoGen 8.x moved the sws flags to a typed [Flags] enum
        // (SwsFlags.SWS_BILINEAR) — the bare `ffmpeg.SWS_BILINEAR` constant
        // that existed in 7.x was removed.
        _swsContext = ffmpeg.sws_getContext(
            header.Width, header.Height, AVPixelFormat.AV_PIX_FMT_YUV420P,
            header.Width, header.Height, AVPixelFormat.AV_PIX_FMT_BGRA,
            (int)SwsFlags.SWS_BILINEAR, null, null, null);
        if (_swsContext is null)
        {
            throw new InvalidOperationException("sws_getContext returned NULL.");
        }

        // BGRA staging buffer: 4 bytes per pixel × width, stride-aligned to
        // 64 bytes (helps sws_scale keep its aligned-write fast paths).
        _bgraStride = header.Width * 4;
        _bgraStride = (_bgraStride + 63) & ~63;
        _bgraBufferSize = _bgraStride * header.Height;
        _bgraBuffer = (byte*)NativeMemory.AlignedAlloc((nuint)_bgraBufferSize, 64);
        if (_bgraBuffer is null)
        {
            throw new InvalidOperationException("Failed to allocate BGRA staging buffer.");
        }
        NativeMemory.Clear(_bgraBuffer, (nuint)_bgraBufferSize);

        // Native scratch arrays for sws_scale. 4 source planes for
        // YUV420P (Y, U, V, alpha), 1 destination. sws_scale reads the
        // pointer table once per call without yielding, so a stable
        // native buffer is sufficient — no GC pinning required.
        _srcDataPtrs = (byte**)NativeMemory.AlignedAlloc(
            (nuint)(sizeof(byte*) * 4), (nuint)sizeof(byte*));
        _srcStridePtrs = (int*)NativeMemory.AlignedAlloc(
            (nuint)(sizeof(int) * 4), (nuint)sizeof(int));
        _dstDataPtrs = (byte**)NativeMemory.AlignedAlloc(
            (nuint)(sizeof(byte*) * 1), (nuint)sizeof(byte*));
        _dstStridePtrs = (int*)NativeMemory.AlignedAlloc(
            (nuint)(sizeof(int) * 1), (nuint)sizeof(int));
        if (_srcDataPtrs is null || _srcStridePtrs is null
            || _dstDataPtrs is null || _dstStridePtrs is null)
        {
            throw new InvalidOperationException("Failed to allocate sws_scale scratch buffers.");
        }

        // Pre-set the destination plane (the BGRA buffer never moves).
        _dstDataPtrs[0] = _bgraBuffer;
        _dstStridePtrs[0] = _bgraStride;

        _logger.LogInformation(
            "FFmpeg decoder primed: codec={Codec} {Width}x{Height} BGRA stride={Stride}",
            header.CodecId, header.Width, header.Height, _bgraStride);

        // Start the wall-clock the first time Prime() succeeds. Subsequent
        // Prime() calls (after Dispose/reinit) restart it so PTS stays
        // contiguous within each decode session.
        _wallClock.Restart();
    }

    /// <inheritdoc/>
    public Task PushPacketAsync(
        ReadOnlyMemory<byte> packet,
        CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.CompletedTask;
        if (_codecContext is null)
        {
            throw new InvalidOperationException(
                "ScrcpyVideoDecoder.PushPacketAsync called before Prime().");
        }

        if (packet.IsEmpty)
        {
            // Treat empty as a flush trigger — same as a null AVPacket.
            return DrainFramesAsync(isFlush: true, cancellationToken);
        }

        var length = packet.Length;
        var unmanaged = (byte*)NativeMemory.AlignedAlloc((nuint)length, 64);
        if (unmanaged is null)
        {
            throw new InvalidOperationException(
                "Failed to allocate packet staging buffer.");
        }

        try
        {
            packet.Span.CopyTo(new Span<byte>(unmanaged, length));

            // av_packet_from_data(AVPacket*, byte*, int) takes ownership of
            // `unmanaged` — it will call av_free() on it. After this call
            // we must NOT free locally.
            ffmpeg.av_packet_from_data(_packet, unmanaged, length);
            unmanaged = null; // ownership transferred to AVPacket

            var sendResult = ffmpeg.avcodec_send_packet(_codecContext, _packet);
            if (sendResult < 0)
            {
                _logger.LogWarning(
                    "avcodec_send_packet failed: code={Code} ({PacketLen} bytes)",
                    sendResult, length);
                return DrainFramesAsync(isFlush: false, cancellationToken);
            }

            return DrainFramesAsync(isFlush: false, cancellationToken);
        }
        finally
        {
            // If we retained ownership (av_packet_from_data was not
            // reached because of a throw above), free what we allocated.
            if (unmanaged is not null)
            {
                NativeMemory.AlignedFree(unmanaged);
            }
        }
    }

    /// <inheritdoc/>
    public Task FlushAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return Task.CompletedTask;
        if (_codecContext is null) return Task.CompletedTask;

        ffmpeg.avcodec_send_packet(_codecContext, null); // null packet = drain
        return DrainFramesAsync(isFlush: true, cancellationToken);
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (_disposed) return ValueTask.CompletedTask;
        _disposed = true;

        // Each *_free variant takes a ** pointer. We capture the field
        // into a local, free via the wrapper, then null the field so
        // any concurrent observer sees the cleared state.
        if (_packet is not null)
        {
            var p = _packet;
            ffmpeg.av_packet_free(&p);
            _packet = null;
        }
        if (_frame is not null)
        {
            var f = _frame;
            ffmpeg.av_frame_free(&f);
            _frame = null;
        }
        if (_swsContext is not null)
        {
            ffmpeg.sws_freeContext(_swsContext);
            _swsContext = null;
        }
        if (_codecContext is not null)
        {
            var c = _codecContext;
            ffmpeg.avcodec_free_context(&c);
            _codecContext = null;
        }
        if (_bgraBuffer is not null)
        {
            NativeMemory.AlignedFree(_bgraBuffer);
            _bgraBuffer = null;
        }
        if (_srcDataPtrs is not null)
        {
            NativeMemory.AlignedFree(_srcDataPtrs);
            _srcDataPtrs = null;
        }
        if (_srcStridePtrs is not null)
        {
            NativeMemory.AlignedFree(_srcStridePtrs);
            _srcStridePtrs = null;
        }
        if (_dstDataPtrs is not null)
        {
            NativeMemory.AlignedFree(_dstDataPtrs);
            _dstDataPtrs = null;
        }
        if (_dstStridePtrs is not null)
        {
            NativeMemory.AlignedFree(_dstStridePtrs);
            _dstStridePtrs = null;
        }

        _logger.LogDebug("ScrcpyVideoDecoder disposed");
        return ValueTask.CompletedTask;
    }

    private Task DrainFramesAsync(bool isFlush, CancellationToken cancellationToken)
    {
        if (_frame is null || _bgraBuffer is null || Header is null
            || _swsContext is null
            || _srcDataPtrs is null || _srcStridePtrs is null
            || _dstDataPtrs is null || _dstStridePtrs is null)
        {
            return Task.CompletedTask;
        }

        // FFmpeg's pattern: after avcodec_send_packet, loop
        // avcodec_receive_frame until it returns EAGAIN (more packets
        // needed) or EOF. Each iteration that returns a real frame
        // publishes a DecodedVideoFrame.
        while (!cancellationToken.IsCancellationRequested)
        {
            int recvResult = ffmpeg.avcodec_receive_frame(_codecContext, _frame);
            if (recvResult == ffmpeg.AVERROR(ffmpeg.EAGAIN))
            {
                return Task.CompletedTask;
            }
            if (recvResult == ffmpeg.AVERROR(ffmpeg.AVERROR_EOF))
            {
                return Task.CompletedTask;
            }
            if (recvResult < 0)
            {
                _logger.LogWarning(
                    "avcodec_receive_frame failed: code={Code} (flush={IsFlush})",
                    recvResult, isFlush);
                return Task.CompletedTask;
            }

            // Convert YUV420P -> BGRA into our pinned staging buffer.
            // FFmpeg.AutoGen 7.1.x exposes sws_scale with managed array
            // signatures (byte*[] srcSlice / int[] srcStride). We marshal
            // our native frame pointers into short-lived arrays each call.
            // sws_scale runs synchronously and does not yield, so a GC
            // compaction race is unlikely; we still copy the small array
            // every frame to keep ownership simple and the lifetime of
            // these arrays scoped to DrainFramesAsync.
            var srcSlice = new byte*[4];
            var srcStride = new int[4];
            srcSlice[0] = _srcDataPtrs[0] = _frame->data[0];
            srcSlice[1] = _srcDataPtrs[1] = _frame->data[1];
            srcSlice[2] = _srcDataPtrs[2] = _frame->data[2];
            srcSlice[3] = _srcDataPtrs[3] = _frame->data[3];
            srcStride[0] = _srcStridePtrs[0] = _frame->linesize[0];
            srcStride[1] = _srcStridePtrs[1] = _frame->linesize[1];
            srcStride[2] = _srcStridePtrs[2] = _frame->linesize[2];
            srcStride[3] = _srcStridePtrs[3] = _frame->linesize[3];

            // Destination stays BGRA -> _bgraBuffer, pre-wired at Prime().
            var dstSlice = new byte*[1] { _dstDataPtrs[0] };
            var dstStride = new int[1] { _dstStridePtrs[0] };

            int scaleResult = ffmpeg.sws_scale(
                _swsContext,
                srcSlice, srcStride,
                0, Header.Height,
                dstSlice, dstStride);

            if (scaleResult <= 0)
            {
                _logger.LogWarning(
                    "sws_scale returned {Result} for {Width}x{Height} frame",
                    scaleResult, _frame->width, _frame->height);
                ffmpeg.av_frame_unref(_frame);
                continue;
            }

            // Hand the buffer to subscribers. We allocate a fresh
            // managed array per frame — that's a copy, but it gives
            // subscribers (WPF UI thread) ownership and isolates the
            // unmanaged backing-store lifetime. For maximum throughput
            // this could be reused with an ImageBuffer pool, but that
            // complicates ownership and the Core layer currently has
            // no such pool.
            var managed = new byte[scaleResult * _bgraStride];
            Marshal.Copy((IntPtr)_bgraBuffer, managed, 0, managed.Length);

            // Prefer the decoded frame's pts if it's a real timestamp
            // (AV_NOPTS_VALUE is int.MinValue / (long)0x8000000000000000).
            // scrcpy's raw stream doesn't carry timestamps on every
            // access unit, so we fall back to a monotonic wall-clock that
            // started at Prime().
            var pts = _frame->pts;
            TimeSpan presentationTime;
            const long AV_NOPTS_VALUE_L = long.MinValue; // 0x8000000000000000
            if (pts == AV_NOPTS_VALUE_L)
            {
                presentationTime = _wallClock.Elapsed;
            }
            else
            {
                // time_base is AVRational { num, den }. Convert to ms.
                var tb = _codecContext->pkt_timebase;
                if (tb.den > 0 && tb.num > 0)
                {
                    // pts (in tb units) * num / den = seconds
                    var seconds = pts * (double)tb.num / tb.den;
                    presentationTime = TimeSpan.FromSeconds(seconds);
                }
                else
                {
                    // Time_base not set (raw H.264 stream): assume 1/1000 ms.
                    presentationTime = TimeSpan.FromMilliseconds(pts);
                }
            }

            var decoded = new DecodedVideoFrame(
                Width: Header.Width,
                Height: Header.Height,
                Stride: _bgraStride,
                BgraPixels: managed,
                PresentationTime: presentationTime);

            try
            {
                FrameDecoded?.Invoke(this, decoded);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "FrameDecoded subscriber threw");
            }

            ffmpeg.av_frame_unref(_frame);
        }

        return Task.CompletedTask;
    }
}
