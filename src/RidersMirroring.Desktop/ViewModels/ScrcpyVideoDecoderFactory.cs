using Riders.Mirroring.Core.Scrcpy;
using IO = System.IO;

namespace Riders.Mirroring.Desktop.ViewModels;

/// <summary>
/// Concrete <see cref="IScrcpyVideoDecoderFactory"/> that creates one
/// <see cref="ScrcpyVideoDecoder"/> per session, each backed by the
/// FFmpeg 8.1 shared DLLs in <c>vendor/ffmpeg/bin/</c>.
///
/// <para>
/// Each decoder owns native memory (codec context, sws context, BGRA
/// staging buffer). They MUST be disposed via
/// <see cref="ScrcpySession.DisposeAsync"/> when the session ends — this
/// factory itself does not track the decoders it creates.
/// </para>
/// </summary>
public sealed class ScrcpyVideoDecoderFactory : IScrcpyVideoDecoderFactory
{
    /// <summary>FFmpeg DLL lookup directory. Default: <c>vendor/ffmpeg/bin/</c>.</summary>
    public string FfmpegRootPath { get; }

    public ScrcpyVideoDecoderFactory()
        : this(DefaultFfmpegRoot())
    {
    }

    public ScrcpyVideoDecoderFactory(string ffmpegRootPath)
    {
        if (string.IsNullOrWhiteSpace(ffmpegRootPath))
        {
            throw new ArgumentException(
                "FFmpeg root path is required.", nameof(ffmpegRootPath));
        }
        FfmpegRootPath = ffmpegRootPath;
        FFmpeg.AutoGen.ffmpeg.RootPath = ffmpegRootPath;
    }

    public IScrcpyVideoDecoder Create() => new ScrcpyVideoDecoder();

    private static string DefaultFfmpegRoot()
    {
        // vendor/ffmpeg/bin/ relative to the binary directory at runtime.
        // Falls back to cwd-relative location when running from the test
        // harness.
        var baseDir = AppContext.BaseDirectory.TrimEnd(
            IO.Path.DirectorySeparatorChar);
        var candidates = new[]
        {
            IO.Path.Combine(baseDir, "ffmpeg"),
            IO.Path.Combine(baseDir, "vendor", "ffmpeg", "bin"),
            IO.Path.Combine(IO.Directory.GetCurrentDirectory(), "vendor", "ffmpeg", "bin"),
        };
        foreach (var c in candidates)
        {
            if (IO.Directory.Exists(c)
                && IO.File.Exists(IO.Path.Combine(c, "avcodec-62.dll")))
            {
                return c;
            }
        }
        // Last resort: whatever AppContext hands us. FFmpeg.AutoGen will
        // throw a clear DllNotFoundException if the DLLs are missing.
        return baseDir;
    }
}
