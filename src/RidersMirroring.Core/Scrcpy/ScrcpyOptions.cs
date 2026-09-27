namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Strongly-typed wrapper around the command-line flags we forward to
/// <c>scrcpy-server</c> on the device. Defaults match the values used by
/// upstream scrcpy 2.x — keep them in sync if you bump versions.
/// </summary>
/// <remarks>
/// This type is intentionally a <see langword="record"/> with simple
/// properties so it serialises cleanly to JSON for logs and is easy to
/// test (xUnit + FluentAssertions).
/// </remarks>
public sealed record ScrcpyOptions
{
    /// <summary>Maximum bitrate for the H.264 stream (default 8 Mbps).</summary>
    public int MaxBitrateMbps { get; init; } = 8;

    /// <summary>Target frame rate (default 60).</summary>
    public int MaxFps { get; init; } = 60;

    /// <summary>Initial viewport size. 0 = use device default.</summary>
    public int Width { get; init; } = 0;

    /// <summary>Initial viewport height. 0 = use device default.</summary>
    public int Height { get; init; } = 0;

    /// <summary>Encoder to request from the device (e.g. <c>"OMX.qcom.video.encoder.avc"</c>).</summary>
    public string? VideoEncoder { get; init; }

    /// <summary>Disable audio capture (default false).</summary>
    public bool DisableAudio { get; init; } = false;

    /// <summary>Show touches on the mirrored screen (default true).</summary>
    public bool ShowTouches { get; init; } = true;

    /// <summary>Keep the device awake while mirroring (default true).</summary>
    public bool StayAwake { get; init; } = true;

    /// <summary>Use the device's clipboard for shared copy/paste (default true).</summary>
    public bool Clipboard { get; init; } = true;

    /// <summary>Turn the screen of the device off while mirroring (reduces heat and battery usage).</summary>
    public bool TurnScreenOff { get; init; } = false;

    /// <summary>Always place the mirror window on top of other windows.</summary>
    public bool AlwaysOnTop { get; init; } = false;

    /// <summary>Start mirroring in fullscreen mode.</summary>
    public bool Fullscreen { get; init; } = false;

    /// <summary>Embed the scrcpy window inside the host hub (no separate top-level window).</summary>
    public bool EmbedInHost { get; init; } = true;

    /// <summary>Position of the embedded window (host client coordinates).</summary>
    public int EmbedX { get; init; } = 0;
    public int EmbedY { get; init; } = 0;
    public int EmbedWidth { get; init; } = 1024;
    public int EmbedHeight { get; init; } = 640;

    /// <summary>
    /// Serialises the options to a list of <c>scrcpy-server</c> command-line
    /// arguments. We emit one dash-dash flag per option to keep things
    /// explicit and easy to debug in the live log.
    /// </summary>
    public IReadOnlyList<string> ToArguments()
    {
        var args = new List<string>(capacity: 16)
        {
            $"--max-fps={MaxFps}",
            $"--max-size={Math.Max(Width, Height)}", // 0 == auto
            $"--bit-rate={MaxBitrateMbps}M",
            $"--show-touches={Bool(OnOff(ShowTouches))}",
            $"--stay-awake={Bool(OnOff(StayAwake))}",
            $"--no-clipboard={OnOff(!Clipboard)}",
            $"--no-audio={OnOff(DisableAudio)}",
        };

        if (!string.IsNullOrEmpty(VideoEncoder))
        {
            args.Add($"--video-codec={VideoEncoder}");
        }

        return args;
    }

    /// <summary>
    /// Builds the argument list for invoking the desktop <c>scrcpy.exe</c> client.
    /// </summary>
    /// <param name="serial">Target device serial.</param>
    /// <param name="windowTitle">Custom window title.</param>
    public IReadOnlyList<string> ToClientArguments(string serial, string? windowTitle = null)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("Serial is required.", nameof(serial));
        }

        var args = new List<string>(capacity: 20)
        {
            "-s", serial,
        };

        if (!string.IsNullOrWhiteSpace(windowTitle))
        {
            args.Add($"--window-title={windowTitle}");
        }

        if (MaxFps > 0)
        {
            args.Add($"--max-fps={MaxFps}");
        }

        if (MaxBitrateMbps > 0)
        {
            args.Add($"--video-bit-rate={MaxBitrateMbps}M");
        }

        var maxSize = Math.Max(Width, Height);
        if (maxSize > 0)
        {
            args.Add($"--max-size={maxSize}");
        }

        if (StayAwake)
        {
            args.Add("--stay-awake");
        }

        if (TurnScreenOff)
        {
            args.Add("--turn-screen-off");
        }

        if (AlwaysOnTop)
        {
            args.Add("--always-on-top");
        }

        if (Fullscreen)
        {
            args.Add("--fullscreen");
        }

        if (ShowTouches)
        {
            args.Add("--show-touches");
        }

        if (DisableAudio)
        {
            args.Add("--no-audio");
        }

        if (!Clipboard)
        {
            args.Add("--no-clipboard");
        }

        // Position the scrcpy window inside the host hub so we can DWM-thumbnail it.
        if (EmbedInHost)
        {
            args.Add($"--window-x={EmbedX}");
            args.Add($"--window-y={EmbedY}");
            args.Add($"--window-width={EmbedWidth}");
            args.Add($"--window-height={EmbedHeight}");
            args.Add("--window-borderless");
            args.Add("--always-on-top");
        }

        return args;
    }

    private static string OnOff(bool value) => value ? "true" : "false";

    private static string Bool(string v) => v;

    /// <summary>Default options used when none are supplied.</summary>
    public static ScrcpyOptions Default { get; } = new();
}