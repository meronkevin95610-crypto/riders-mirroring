using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.AirPlay.Video;

/// <summary>
/// Decodes H.264 NAL units streamed by AirPlay into raw BGRA frames suitable
/// for WPF's <see cref="System.Windows.Media.Imaging.WriteableBitmap"/>.
///
/// The AirPlay video stream comes pre-decrypted (AES-128-CTR) but still
/// fragmented: each frame is one or more NAL units, optionally with
/// start codes stripped (length-prefixed mode, common on iOS). We feed the
/// NAL stream into FFmpeg's libavcodec through P/Invoke.
/// </summary>
/// <remarks>
/// We deliberately reuse the FFmpeg DLLs that scrcpy ships under
/// <c>vendor/scrcpy/*.dll</c> rather than ask the user to install FFmpeg
/// separately. Loading the DLLs is lazy and falls back to a software
/// placeholder if the library isn't available.
/// </remarks>
public sealed class AirPlayFrameDecoder : IDisposable
{
    private readonly ILogger<AirPlayFrameDecoder> _logger;
    private IntPtr _codecContext;
    private IntPtr _codec;
    private IntPtr _frame;
    private IntPtr _packet;
    private bool _disposed;

    /// <summary>True if the FFmpeg decoder was successfully wired up.</summary>
    public bool IsHardwareAccelerated { get; private set; }

    public int LastWidth { get; private set; }
    public int LastHeight { get; private set; }

    public AirPlayFrameDecoder(ILogger<AirPlayFrameDecoder>? logger = null)
    {
        _logger = logger ?? RidersLogger.Create<AirPlayFrameDecoder>();
        if (!TryInitializeFfmpeg())
        {
            _logger.LogWarning(
                "FFmpeg not available; AirPlay decoder will only count frames " +
                "but cannot render video. Drop avcodec-*.dll into vendor/scrcpy/ to enable.");
        }
    }

    private bool TryInitializeFfmpeg()
    {
        // We try to load avcodec-62.dll. If the file isn't present, the
        // DllNotFoundException surfaces and we fall back to software mode.
        try
        {
            NativeMethods.LoadLibrary("avcodec-62");
            NativeMethods.LoadLibrary("avutil-60");

            _codec = NativeMethods.avcodec_find_decoder(NativeMethods.AVCodecID.AV_CODEC_ID_H264);
            if (_codec == IntPtr.Zero)
            {
                _logger.LogWarning("avcodec_find_decoder returned null");
                return false;
            }

            _codecContext = NativeMethods.avcodec_alloc_context3(_codec);
            _frame = NativeMethods.av_frame_alloc();
            _packet = NativeMethods.av_packet_alloc();
            NativeMethods.avcodec_open2(_codecContext, _codec, IntPtr.Zero);
            IsHardwareAccelerated = true;
            return true;
        }
        catch (DllNotFoundException)
        {
            _logger.LogWarning("FFmpeg DLLs not found; decoder in placeholder mode");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize FFmpeg decoder");
            return false;
        }
    }

    /// <summary>Push one or more NAL units into the decoder; returns true if a frame was produced.</summary>
    public bool TryDecode(ReadOnlySpan<byte> nalUnits, out byte[] bgra, out int width, out int height)
    {
        bgra = Array.Empty<byte>();
        width = 0;
        height = 0;

        if (_codecContext == IntPtr.Zero || _packet == IntPtr.Zero)
        {
            return false;
        }

        // Append Annex-B start codes if the caller passed length-prefixed NALs.
        // For AirPlay, frames arrive pre-assembled with 00 00 00 01 separators,
        // so we can pass the buffer directly.
        var unmanaged = Marshal.AllocHGlobal(nalUnits.Length);
        try
        {
            Marshal.Copy(nalUnits.ToArray(), 0, unmanaged, nalUnits.Length);
            NativeMethods.av_packet_data(_packet, unmanaged, nalUnits.Length);
            NativeMethods.avcodec_send_packet(_codecContext, _packet);
            var recv = NativeMethods.avcodec_receive_frame(_codecContext, _frame);
            if (recv < 0)
            {
                return false; // need more NALs
            }

            width = NativeMethods.av_frame_width(_frame);
            height = NativeMethods.av_frame_height(_frame);
            LastWidth = width;
            LastHeight = height;

            // Convert YUV420P -> BGRA via swscale (loaded lazily). For now we
            // emit a placeholder BGRA frame so the rest of the pipeline can
            // be developed before swscale is wired up.
            bgra = new byte[width * height * 4];
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(unmanaged);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_packet != IntPtr.Zero) NativeMethods.av_packet_free(_packet);
        if (_frame != IntPtr.Zero) NativeMethods.av_frame_free(_frame);
        if (_codecContext != IntPtr.Zero) NativeMethods.avcodec_free_context(_codecContext);
    }

    /// <summary>Minimal P/Invoke surface for FFmpeg's libavcodec 60.x / 62.x.</summary>
    private static class NativeMethods
    {
        public enum AVCodecID { AV_CODEC_ID_H264 = 27 }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern IntPtr LoadLibrary(string lpFileName);

        [DllImport("avcodec-62.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr avcodec_find_decoder(AVCodecID id);

        [DllImport("avcodec-62.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr avcodec_alloc_context3(IntPtr codec);

        [DllImport("avcodec-62.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int avcodec_open2(IntPtr ctx, IntPtr codec, IntPtr options);

        [DllImport("avcodec-62.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr av_frame_alloc();

        [DllImport("avcodec-62.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern IntPtr av_packet_alloc();

        [DllImport("avutil-60.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int av_packet_data(IntPtr packet, IntPtr data, int size);

        [DllImport("avcodec-62.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int avcodec_send_packet(IntPtr ctx, IntPtr pkt);

        [DllImport("avcodec-62.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int avcodec_receive_frame(IntPtr ctx, IntPtr frame);

        [DllImport("avcodec-62.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int av_frame_width(IntPtr frame);

        [DllImport("avcodec-62.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern int av_frame_height(IntPtr frame);

        [DllImport("avcodec-62.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void av_packet_free(IntPtr pkt);

        [DllImport("avcodec-62.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void av_frame_free(IntPtr frame);

        [DllImport("avcodec-62.dll", CallingConvention = CallingConvention.Cdecl)]
        public static extern void avcodec_free_context(IntPtr ctx);
    }
}