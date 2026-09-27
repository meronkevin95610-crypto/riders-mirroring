using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using FFmpeg.AutoGen;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.RawTest;

/// <summary>
/// Manual dev tool that drives the v4.0 <c>raw_stream=true</c> pipeline
/// end-to-end on a real Android device, WITHOUT going through FFmpeg.
///
/// <para><b>Strict operation order</b> (a scrcpy-server trying to connect
/// before its PC-side listener is up gets <c>Connection refused</c> and
/// crashes immediately, so the ordering matters):</para>
/// <list type="number">
///   <item>Bind a <see cref="System.Net.Sockets.TcpListener"/> on 127.0.0.1:27183 (PC side, IPv4 explicit).</item>
///   <item>Push scrcpy-server.jar onto the device.</item>
///   <item>Open the reverse tunnel <c>localabstract:scrcpy_&lt;scid&gt; → tcp:27183</c> (v4.0 device-side LocalSocket).</item>
///   <item>Spawn <c>app_process</c> on the device (server tries to dial us back).</item>
///   <item>Accept the inbound TCP connection (server-side dial succeeds).</item>
///   <item>For 10 seconds, log every chunk of bytes received on the wire.</item>
///   <item>Best-effort: dump <c>adb logcat -d -s scrcpy</c> at the end.</item>
/// </list>
///
/// <para>Goal of this Phase-1 tool: confirm the raw stream actually flows
/// from the device to our socket. FFmpeg decoding is added in a separate
/// milestone to keep debugging tractable.</para>
/// </summary>
internal static class Program
{
    private const int RunSeconds = 10;
    private const string Tag = "[raw-test]";

    /// <summary>
    /// Fallback width/height used to Prime() the decoder before any SPS
    /// has been parsed. FFmpeg overwrites these with the real values once
    /// it sees the SPS NAL unit, so they only need to be large enough to
    /// cover the device's actual resolution.
    /// </summary>
    private const int FallbackWidth = 1920;
    private const int FallbackHeight = 1080;

    /// <summary>Where the first decoded BGRA frame is dumped as a 32-bit BMP.</summary>
    private static readonly string BmpOutputPath = Path.Combine(
        Directory.GetCurrentDirectory(), "test_frame.bmp");

    private static async Task<int> Main(string[] args)
    {
        // Allow overriding the device serial via the command line so the
        // tool can be run unattended from CI in a separate workflow.
        var serial = args.Length > 0 ? args[0] : Environment.GetEnvironmentVariable("RIDERS_TEST_SERIAL");
        if (string.IsNullOrWhiteSpace(serial))
        {
            Console.Error.WriteLine($"{Tag} usage: riders-raw-test <serial>  (or set RIDERS_TEST_SERIAL).");
            Console.Error.WriteLine($"{Tag} hint: run `adb devices` to find the serial.");
            return 2;
        }

        Console.WriteLine($"{Tag} target device serial = {serial}");
        Console.WriteLine($"{Tag} run duration         = {RunSeconds} s");

        var adb = new AdbServerManager();

        var devices = await adb.GetDevicesAsync().ConfigureAwait(false);
        if (devices.Count == 0 || !devices.Any(d => string.Equals(d.Serial, serial, StringComparison.Ordinal)))
        {
            Console.Error.WriteLine($"{Tag} no device with serial '{serial}' found via ADB.");
            Console.Error.WriteLine($"{Tag} (run `adb devices` to confirm — start adb-server first if needed).");
            return 3;
        }

        var server = new ScrcpyServer(adb) { UseRawStream = true };

        // ============================================================
        // FFmpeg setup — must happen BEFORE instantiating the decoder
        // because FFmpeg.AutoGen resolves P/Invoke targets at allocation
        // time. The vendored LGPL DLLs are copied flat next to the
        // executable by the .csproj (see <None Include="*.dll" Link="…"/>),
        // so AppContext.BaseDirectory IS the directory that contains
        // avcodec-63.dll / avutil-61.dll / swscale-10.dll.
        //
        // Why flat? FFmpeg.AutoGen 7.x loads each library by name via
        // its internal WindowsFunctionResolver, which calls
        // LoadLibrary(Path.Combine(ffmpeg.RootPath, "avcodec-63.dll"))
        // — and Windows LoadLibrary fails when the path is a network
        // OneDrive path with diacritics ("…") even when File.Exists
        // returns true. Flat layout avoids the path-mangling entirely.
        // ============================================================
        var ffmpegBinDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        Console.WriteLine($"{Tag} ffmpeg root path: {ffmpegBinDir}");
        ffmpeg.RootPath = ffmpegBinDir;

        // Verify the critical DLLs are reachable in the flat layout.
        // FFmpeg 8.1 (FFmpeg.AutoGen 8.1.0): avcodec-62.dll, avutil-60.dll, swscale-9.dll
        foreach (var dll in new[] { "avcodec-62.dll", "avutil-60.dll", "swscale-9.dll" })
        {
            var path = Path.Combine(ffmpegBinDir, dll);
            if (!File.Exists(path))
            {
                Console.Error.WriteLine($"{Tag} FATAL: missing FFmpeg DLL: {path}");
                Console.Error.WriteLine($"{Tag} run tools/fetch-vendor-ffmpeg.ps1 to vendor it.");
                return 8;
            }
        }

        // Force FFmpeg.AutoGen to throw the real DllNotFoundException (with
        // Win32 error code) instead of the silent NotSupportedException
        // fallback. Lets us diagnose missing dependencies on the actual device.
        var throwIfMissing = typeof(FFmpeg.AutoGen.DynamicallyLoadedBindings)
            .GetField("ThrowErrorIfFunctionNotFound",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        if (throwIfMissing is not null)
        {
            Console.WriteLine($"{Tag} FFmpeg.ThrowErrorIfFunctionNotFound set to True");
            throwIfMissing.SetValue(null, true);
        }
        else
        {
            Console.Error.WriteLine($"{Tag} WARNING: couldn't find ThrowErrorIfFunctionNotFound field — diagnostic value unchanged");
        }

        // Build the decoder + subscribe to FrameDecoded before pushing any
        // packet so we don't miss the first IDR. ScrcpyVideoDecoder is
        // IAsyncDisposable — we dispose it explicitly in the finally block.
        var decoder = new ScrcpyVideoDecoder();
        var frameStats = new FrameStats();
        decoder.FrameDecoded += (_, frame) =>
        {
            frameStats.Count++;
            var firstFramePath = frameStats.Count == 1 ? BmpOutputPath : null;
            try
            {
                Console.WriteLine(
                    $"{Tag} [decode] frame #{frameStats.Count} {frame.Width}x{frame.Height} " +
                    $"stride={frame.Stride} BGRA bytes={frame.BgraPixels.Length} " +
                    $"pts={frame.PresentationTime.TotalMilliseconds:F1}ms" +
                    (firstFramePath is null ? string.Empty : $" → saving {firstFramePath}"));
                if (firstFramePath is not null)
                {
                    WriteBmp(firstFramePath, frame.Width, frame.Height, frame.Stride, frame.BgraPixels);
                    Console.WriteLine($"{Tag} [decode] first frame saved → {firstFramePath}");
                }
            }
            catch (Exception fx)
            {
                Console.Error.WriteLine($"{Tag} [decode] FrameDecoded handler threw: {fx.GetType().Name}: {fx.Message}");
            }
        };

        // Prime the decoder with a fabricated header. The header.Width/
        // Height are placeholders — FFmpeg overrides them once it parses
        // the SPS NAL. The codec is hardcoded to h264 because that's what
        // scrcpy-server v4.0 streams by default.
        var fakeHeader = new ScrcpyStreamHeader(
            DeviceName: "raw-v4.0",
            CodecId: "h264",
            Width: FallbackWidth,
            Height: FallbackHeight);
        try
        {
            decoder.Prime(fakeHeader);
            Console.WriteLine($"{Tag} [decode] decoder primed (fallback {FallbackWidth}x{FallbackHeight}, will be overridden by SPS)");
        }
        catch (Exception primeEx)
        {
            Console.Error.WriteLine($"{Tag} [decode] Prime() FAILED: {primeEx.GetType().FullName}: {primeEx.Message}");
            // Dump the full exception chain (FFmpeg.AutoGen often wraps
            // a DllNotFoundException inside NotSupportedException).
            var inner = primeEx.InnerException;
            var depth = 1;
            while (inner is not null && depth < 8)
            {
                Console.Error.WriteLine($"{Tag} [decode]   ↳ inner[{depth}] {inner.GetType().FullName}: {inner.Message}");
                inner = inner.InnerException;
                depth++;
            }
            Console.Error.WriteLine($"{Tag} [decode] full: {primeEx}");
            return 9;
        }

        IScrcpySession? session = null;
        ScrcpyClient? client = null;
        try
        {
            // ============================================================
            // STEP 0 — locate the scrcpy-server.jar (push target).
            // ============================================================
            var jar = ScrcpyServer.LocateServerJar();
            Console.WriteLine($"{Tag} [step 0] jar path: {jar}");
            if (!File.Exists(jar))
            {
                Console.Error.WriteLine($"{Tag} scrcpy-server.jar not found at expected path.");
                Console.Error.WriteLine($"{Tag} Run tools/fetch-vendor-scrcpy-server.ps1 to vendor it.");
                return 4;
            }

            // ============================================================
            // STEP 1 — bind the PC-side TcpListener FIRST.
            //
            // If the device-side scrcpy-server tries to dial 127.0.0.1:27183
            // before this listener exists, the kernel replies
            // ECONNREFUSED and the JVM server crashes with
            //   java.io.IOException: Connection refused
            // before ever sending a single byte. Order: listener → tunnel →
            // app_process → accept.
            // ============================================================
            const int port = ScrcpyClient.DefaultPort;
            Console.WriteLine($"{Tag} [step 1] binding TcpListener on 127.0.0.1:{port}…");
            client = new ScrcpyClient(port);
            client.StartListening();
            Console.WriteLine($"{Tag} [step 1] OK — listening on {port}. Ready to accept device-side dial.");

            // ============================================================
            // STEP 2 — push the jar and open the reverse tunnel.
            // Wrapped in try/catch so an ADB-level failure (e.g. multiple
            // devices, daemon hiccup) is visible.
            // ============================================================
            Console.WriteLine($"{Tag} [step 2] pushing jar and opening reverse tunnel (localabstract:scrcpy_<scid> → tcp:{port})…");
            try
            {
                session = await server.StartAsync(serial, ScrcpyOptions.Default, jar, CancellationToken.None)
                    .ConfigureAwait(false);
                Console.WriteLine($"{Tag} [step 2] OK — reverse tunnel established for {session.DeviceSerial} (scid=0x{server.Scid:x8})");
            }
            catch (Exception step2Ex)
            {
                Console.Error.WriteLine($"{Tag} [step 2] FAILED: {step2Ex.GetType().Name}: {step2Ex.Message}");
                Console.Error.WriteLine($"{Tag} [step 2] hint: confirm `adb -s {serial} reverse --list` shows the entry, and that no firewall blocks adb on Windows.");
                return 7;
            }

            // ============================================================
            // STEP 3 — spawn app_process on the device. Non-blocking: the
            // server keeps running until the device side closes the socket.
            // ============================================================
            Console.WriteLine($"{Tag} [step 3] spawning app_process on device (raw_stream=true)…");
            var spawnTask = server.ExecuteAppProcessAsync(serial, ScrcpyOptions.Default, CancellationToken.None);

            // ============================================================
            // STEP 4 — accept the inbound connection from the device.
            // The device dials its LocalSocket `scrcpy_<scid>` which is
            // forwarded by adb reverse to tcp:27183 on the PC.
            // ============================================================
            Console.WriteLine($"{Tag} [step 4] awaiting AcceptTcpClient on {IPAddress.Loopback}:{port} (IPv4)…");
            var acceptTask = client.AcceptAsync(CancellationToken.None);

            // Race the two async operations against a 10s budget.
            var done = await Task.WhenAny(acceptTask, spawnTask, Task.Delay(TimeSpan.FromSeconds(RunSeconds)))
                .ConfigureAwait(false);

            if (done == acceptTask && client.IsConnected)
            {
                var remoteEp = client.RemoteEndPoint?.ToString() ?? "<unknown>";
                Console.WriteLine($"{Tag} [step 4] OK — device connected from {remoteEp}. Reading + decoding raw bytes for up to {RunSeconds} s…");
                await ReadAndDecodeAsync(client.Stream, decoder, frameStats, TimeSpan.FromSeconds(RunSeconds)).ConfigureAwait(false);
            }
            else if (done == spawnTask)
            {
                var stdout = await spawnTask.ConfigureAwait(false);
                Console.Error.WriteLine($"{Tag} [step 4] app_process returned BEFORE a TCP connection arrived.");
                Console.Error.WriteLine($"{Tag} [step 4] stdout (truncated): {Truncate(stdout, 400)}");
                Console.Error.WriteLine($"{Tag} [step 4] Likely cause: scrcpy-server failed to start (jar push OK, tunnel OK).");
                DumpLogcat(serial);
                return 5;
            }
            else
            {
                Console.Error.WriteLine($"{Tag} [step 4] no inbound TCP connection after {RunSeconds} s.");
                Console.Error.WriteLine($"{Tag} [step 4] Likely cause: app_process didn't start, or device can't reach 127.0.0.1:27183.");
                Console.Error.WriteLine($"{Tag} [step 4] Check with: adb -s {serial} shell 'netstat -an | grep 27183'");
                DumpLogcat(serial);
                return 6;
            }

            // Drain any spawn-task output if it finished in the meantime.
            if (spawnTask.IsCompletedSuccessfully)
            {
                var stdout = await spawnTask.ConfigureAwait(false);
                Console.WriteLine($"{Tag} [after] app_process stdout (after capture): {Truncate(stdout, 400)}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{Tag} FATAL: {ex.GetType().Name}: {ex.Message}");
            Console.Error.WriteLine(ex.ToString());
            return 1;
        }
        finally
        {
            // Always try to dump logcat so we have device-side context even on success.
            DumpLogcat(serial);

            if (decoder is not null)
            {
                try
                {
                    await decoder.DisposeAsync().ConfigureAwait(false);
                }
                catch (Exception decDisposeEx)
                {
                    Console.Error.WriteLine($"{Tag} [decode] DisposeAsync threw: {decDisposeEx.GetType().Name}: {decDisposeEx.Message}");
                }
            }
            if (client is not null)
            {
                await client.DisposeAsync().ConfigureAwait(false);
            }
            if (session is not null)
            {
                await session.DisposeAsync().ConfigureAwait(false);
            }
        }

        Console.WriteLine($"{Tag} DONE.");
        return 0;
    }

    /// <summary>
    /// Read from <paramref name="stream"/> for up to <paramref name="budget"/>,
    /// forwarding each chunk to <paramref name="decoder"/>. Decoded frames
    /// surface via the <see cref="IScrcpyVideoDecoder.FrameDecoded"/> event
    /// (subscribed by <see cref="Main"/>). Stops early on EOF or socket
    /// error.
    /// </summary>
    private static async Task ReadAndDecodeAsync(
        NetworkStream stream,
        IScrcpyVideoDecoder decoder,
        FrameStats stats,
        TimeSpan budget)
    {
        var sw = Stopwatch.StartNew();
        var totalBytes = 0L;
        var chunkIndex = 0;
        var buf = new byte[64 * 1024];

        using var cts = new CancellationTokenSource(budget);
        while (!cts.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await stream.ReadAsync(buf.AsMemory(0, buf.Length), cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (IOException ioEx)
            {
                Console.WriteLine($"{Tag} read ended: {ioEx.GetType().Name}: {ioEx.Message}");
                break;
            }
            catch (SocketException sockEx)
            {
                Console.WriteLine($"{Tag} socket error: {sockEx.SocketErrorCode}");
                break;
            }

            if (read == 0)
            {
                Console.WriteLine($"{Tag} EOF on stream after {totalBytes} bytes — device side closed.");
                break;
            }

            totalBytes += read;
            chunkIndex++;
            stats.RawChunks++;

            // Verbose per-chunk log + a more digestible first-bytes hex dump.
            // The head bytes identify the NAL type: 67=SPS, 68=PPS, 65=IDR, 41=P-slice, etc.
            var firstBytes = string.Join(' ', buf.AsSpan(0, Math.Min(read, 12)).ToArray().Select(b => b.ToString("x2")));
            var nalType = DescribeNalType(buf, read);
            Console.WriteLine(
                $"{Tag} chunk #{chunkIndex,4}  +{read,7} B  total={totalBytes,9} B  t={sw.Elapsed:mm\\:ss\\.fff}  nal={nalType}  head=[{firstBytes}]");

            // Forward the raw bytes to FFmpeg. Scrcpy v4.0 sends each NAL
            // unit (or run of NALs forming one access unit) as its own
            // chunk; FFmpeg.AutoGen handles start-code framing internally.
            try
            {
                await decoder.PushPacketAsync(new ReadOnlyMemory<byte>(buf, 0, read), cts.Token).ConfigureAwait(false);
            }
            catch (Exception decEx)
            {
                Console.Error.WriteLine(
                    $"{Tag} [decode] PushPacketAsync threw {decEx.GetType().Name}: {decEx.Message}");
            }
        }

        // Flush decoder at end-of-stream so any frames held in FFmpeg's
        // reorder buffer surface as a final FrameDecoded.
        try
        {
            await decoder.FlushAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception flushEx)
        {
            Console.Error.WriteLine($"{Tag} [decode] FlushAsync threw: {flushEx.GetType().Name}: {flushEx.Message}");
        }

        sw.Stop();
        Console.WriteLine($"{Tag} === summary ===");
        Console.WriteLine($"{Tag} total bytes received : {totalBytes}");
        Console.WriteLine($"{Tag} chunks               : {chunkIndex}");
        Console.WriteLine($"{Tag} frames decoded       : {stats.Count}");
        Console.WriteLine($"{Tag} elapsed              : {sw.Elapsed}");
        Console.WriteLine($"{Tag} average rate         : {(totalBytes / Math.Max(0.001, sw.Elapsed.TotalSeconds)):F0} B/s");
        if (totalBytes == 0)
        {
            Console.Error.WriteLine($"{Tag} WARNING: 0 bytes received. Pipeline is broken — debug app_process args next.");
        }
        else if (stats.Count == 0)
        {
            Console.Error.WriteLine($"{Tag} WARNING: bytes flowed but FFmpeg produced 0 frames. Check decoder / SPS parsing.");
        }
        else
        {
            Console.WriteLine($"{Tag} OK — frames decoded end-to-end.");
        }
    }

    /// <summary>
    /// Decode the start-code + NAL header byte to a short human label
    /// (SPS / PPS / IDR / P-slice / SEI / unknown). Best-effort only —
    /// malformed buffers return "(unknown)".
    /// </summary>
    private static string DescribeNalType(byte[] buf, int length)
    {
        if (length < 5)
        {
            return "(short)";
        }

        var i = 0;
        // skip start code (00 00 00 01 or 00 00 01)
        if (buf[0] == 0 && buf[1] == 0 && buf[2] == 0 && buf[3] == 1) i = 4;
        else if (buf[0] == 0 && buf[1] == 0 && buf[2] == 1) i = 3;
        else return "(no start-code)";

        if (i >= length)
        {
            return "(empty NAL)";
        }

        var nalHeader = buf[i];
        var nalType = nalHeader & 0x1F;
        return nalType switch
        {
            1 => "non-IDR slice",
            2 => "slice-data-A",
            3 => "slice-data-B",
            4 => "slice-data-C",
            5 => "IDR slice",
            6 => "SEI",
            7 => "SPS",
            8 => "PPS",
            9 => "AUD",
            10 => "end-of-seq",
            11 => "end-of-stream",
            _ => $"unknown type {nalType}",
        };
    }

    /// <summary>
    /// Mutable counters shared between the read loop and the FrameDecoded
    /// event handler.
    /// </summary>
    private sealed class FrameStats
    {
        public int Count;
        public int RawChunks;
    }

    /// <summary>
    /// Write a 32-bit BMP (BI_RGB, top-down DIB) holding BGRA pixels. We
    /// flip rows to match BMP's bottom-up convention because FFmpeg
    /// produces top-down BGRA via sws_scale.
    /// </summary>
    private static void WriteBmp(
        string path, int width, int height, int stride, ReadOnlyMemory<byte> bgraTopDown)
    {
        // BMP row size: 4 bytes/px × width, aligned to 4 bytes.
        var rowSize = (width * 4 + 3) & ~3;
        var imageSize = rowSize * height;
        var fileSize = 14 + 40 + imageSize;

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var bw = new BinaryWriter(fs);

        // --- BITMAPFILEHEADER (14 bytes) ---
        bw.Write((byte)'B');
        bw.Write((byte)'M');
        bw.Write((uint)fileSize);
        bw.Write((ushort)0);            // reserved
        bw.Write((ushort)0);            // reserved
        bw.Write((uint)14 + 40);        // pixel data offset

        // --- BITMAPINFOHEADER (40 bytes) ---
        bw.Write((uint)40);             // header size
        bw.Write(width);                // width
        bw.Write(height);               // height (positive = bottom-up)
        bw.Write((ushort)1);            // planes
        bw.Write((ushort)32);           // bit count
        bw.Write((uint)0);              // BI_RGB (uncompressed)
        bw.Write((uint)imageSize);
        bw.Write(2835);                 // ~72 DPI horizontal (2835 ppm)
        bw.Write(2835);                 // ~72 DPI vertical
        bw.Write((uint)0);              // colors used
        bw.Write((uint)0);              // colors important

        // --- Pixel data, bottom-up ---
        // source rows: 0..height-1 (top-down), stride = `stride` (often padded)
        // dest rows:   height-1..0 (bottom-up), rowSize padded to 4 bytes.
        var span = bgraTopDown.Span;
        var paddingBytes = rowSize - (width * 4);
        var pad = paddingBytes > 0 ? new byte[paddingBytes] : Array.Empty<byte>();

        for (var srcRow = height - 1; srcRow >= 0; srcRow--)
        {
            var offset = srcRow * stride;
            bw.Write(span.Slice(offset, width * 4));
            if (paddingBytes > 0)
            {
                bw.Write(pad);
            }
        }
    }

    private static string Truncate(string s, int max) =>
        string.IsNullOrEmpty(s) ? "<empty>" : (s.Length <= max ? s : s.Substring(0, max) + "…");

    /// <summary>
    /// Best-effort <c>adb logcat -d -s scrcpy</c> dump. Used both on
    /// failure and at the end of the run so we always see what the
    /// server saw on the device. Errors are swallowed (logcat may be
    /// unavailable if the device was disconnected).
    /// </summary>
    private static void DumpLogcat(string serial)
    {
        try
        {
            Console.WriteLine($"{Tag} [logcat] dumping device-side scrcpy log…");
            var psi = new ProcessStartInfo
            {
                FileName = "adb",
                Arguments = $"-s {serial} logcat -d -s scrcpy:V",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p is null)
            {
                Console.Error.WriteLine($"{Tag} [logcat] could not start adb (PATH issue?).");
                return;
            }
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            if (!string.IsNullOrWhiteSpace(p.StandardError.ReadToEnd()))
            {
                // Already read above — keep for readability.
            }
            p.WaitForExit(3000);

            if (string.IsNullOrWhiteSpace(stdout))
            {
                Console.WriteLine($"{Tag} [logcat] (empty — no scrcpy:V lines in buffer)");
            }
            else
            {
                Console.WriteLine($"{Tag} [logcat] --- begin scrcpy:V ---");
                Console.WriteLine(stdout);
                Console.WriteLine($"{Tag} [logcat] --- end scrcpy:V ---");
            }
            if (!string.IsNullOrWhiteSpace(stderr))
            {
                Console.Error.WriteLine($"{Tag} [logcat] adb stderr: {stderr.Trim()}");
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{Tag} [logcat] failed to dump: {ex.GetType().Name}: {ex.Message}");
        }
    }
}
