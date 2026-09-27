using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Riders.Mirroring.Core.Scrcpy;
using Riders.Mirroring.Core.Wireless;

namespace Riders.Mirroring.Benchmarks;

/// <summary>
/// Entry point. Run with:
///   dotnet run -c Release --project tests/RidersMirroring.Benchmarks/
///
/// Optional positional argument: a class name (substring) to run only that
/// class. Otherwise all benchmark classes are discovered.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        // Use BenchmarkRunner.Run<T> explicitly so we control the discovery
        // and so we can pass an optional class-name filter via the first arg.
        // Without an arg, run all classes.
        var config = ManualConfig.CreateMinimumViable()
            .AddJob(Job.Default.WithWarmupCount(2).WithIterationCount(3)
                .WithInvocationCount(1).WithUnrollFactor(1));

        var classes = new[]
        {
            typeof(ScrcpyStreamParserBenchmarks),
            typeof(ScrcpyOptionsBenchmarks),
            typeof(WirelessPairingInfoBenchmarks),
        };

        var filter = args.Length > 0 ? args[0] : null;
        var selected = filter is null
            ? classes
            : classes.Where(c => c.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToArray();

        if (selected.Length == 0)
        {
            Console.Error.WriteLine($"No benchmark class matches filter '{filter}'.");
            return 1;
        }

        foreach (var cls in selected)
        {
            BenchmarkRunner.Run(cls, config);
        }

        return 0;
    }
}

[MemoryDiagnoser]
public class ScrcpyStreamParserBenchmarks
{
    /// <summary>
    /// Synthetic stream payload: device_name = "ASUS X00TD" (9 bytes),
    /// codec_id = "h264" (4 bytes). 17 bytes of body + 2× 4-byte length
    /// prefixes = 25 bytes total.
    /// </summary>
    private byte[] _payload = default!;

    private MemoryStream _stream = default!;

    [GlobalSetup]
    public void Setup()
    {
        var nameLen = 9u;
        var codecLen = 4u;
        _payload = new byte[4 + nameLen + 4 + codecLen];
        var span = _payload.AsSpan();
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span, nameLen);
        "ASUS X00TD"u8.CopyTo(span[4..]);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(span[(4 + (int)nameLen)..], codecLen);
        "h264"u8.CopyTo(span[(8 + (int)nameLen)..]);
        _stream = new MemoryStream(_payload);
    }

    /// <summary>
    /// Cold-stream Parse: the parser reads from a fresh stream every call
    /// (mirrors the real "one header per session" usage).
    /// </summary>
    [Benchmark(Description = "TryReadHeader (one-off, fresh stream)")]
    public ScrcpyStreamHeader? ParseCold()
    {
        using var s = new MemoryStream(_payload);
        ScrcpyStreamParser.TryReadHeader(s, out var header, out _);
        return header;
    }

    /// <summary>
    /// Hot-stream Parse: rewind the same stream. This is a synthetic
    /// micro-benchmark — the parser allocates a new byte[] for the
    /// device name on every call, so it's a useful regression detector
    /// if anyone optimises that allocation away.
    /// </summary>
    [Benchmark(Description = "TryReadHeader (rewound stream, allocation tracking)")]
    public ScrcpyStreamHeader? ParseRewound()
    {
        _stream.Position = 0;
        ScrcpyStreamParser.TryReadHeader(_stream, out var header, out _);
        return header;
    }
}

[MemoryDiagnoser]
public class ScrcpyOptionsBenchmarks
{
    private ScrcpyOptions _default = default!;
    private ScrcpyOptions _withEncoder = default!;

    [GlobalSetup]
    public void Setup()
    {
        _default = ScrcpyOptions.Default;
        _withEncoder = new ScrcpyOptions { VideoEncoder = "OMX.qcom.video.encoder.avc" };
    }

    [Benchmark(Baseline = true, Description = "ToArguments() — defaults only")]
    public IReadOnlyList<string> ToArgumentsDefault() => _default.ToArguments();

    [Benchmark(Description = "ToArguments() — with custom VideoEncoder")]
    public IReadOnlyList<string> ToArgumentsWithEncoder() => _withEncoder.ToArguments();

    [Benchmark(Description = "ToClientArguments(serial, windowTitle)")]
    public IReadOnlyList<string> ToClientArguments() =>
        _default.ToClientArguments("emulator-5554", "Pixel 8");
}

[MemoryDiagnoser]
public class WirelessPairingInfoBenchmarks
{
    private WirelessPairingInfo _simple = default!;
    private WirelessPairingInfo _withSpecials = default!;
    private WirelessPairingInfo _longPwd = default!;

    [GlobalSetup]
    public void Setup()
    {
        _simple = new WirelessPairingInfo("HomeNetwork", "secret123", "192.168.1.10:5555");
        _withSpecials = new WirelessPairingInfo(@"My\SSID;", "p;,:ass\"word'", "10.0.0.1:5555");
        _longPwd = new WirelessPairingInfo(
            Ssid: "CorpWiFi",
            Password: new string('a', 63), // max WPA2-PSK length
            HostEndpoint: "192.168.0.42:5555");
    }

    [Benchmark(Baseline = true, Description = "ToQrPayload() — simple")]
    public string ToQrPayloadSimple() => _simple.ToQrPayload();

    [Benchmark(Description = "ToQrPayload() — specials requiring escape")]
    public string ToQrPayloadSpecials() => _withSpecials.ToQrPayload();

    [Benchmark(Description = "ToQrPayload() — 63-char password")]
    public string ToQrPayloadLongPwd() => _longPwd.ToQrPayload();
}