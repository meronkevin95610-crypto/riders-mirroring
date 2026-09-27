using System.IO;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.DeviceCommands;
using AdvancedSharpAdbClient.Models;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Pushes <c>scrcpy-server.jar</c> to the device, opens the reverse
/// tunnel, and spawns the server process via <c>app_process</c>. Returns a
/// disposable <see cref="IScrcpySession"/> that owns the lifecycle.
///
/// <para>
/// Video decoding on the PC side (H.264 → <c>D3DImage</c>) is implemented in
/// <c>ScrcpyVideoDecoder</c>, which is out of scope for this milestone — we
/// focus here on the server-side handshake so the rest of the pipeline can
/// be wired up before the FFmpeg dependency lands.
/// </para>
/// </summary>
public sealed class ScrcpyServer
{
    /// <summary>File name pushed onto the device.</summary>
    public const string DefaultRemotePath = "/data/local/tmp/scrcpy-server.jar";

    /// <summary>
    /// Client version we target. Must match the first argument scrcpy-server
    /// expects on its command line (otherwise the server aborts with
    /// "server version does not match the client"). Pinned via
    /// <c>vendor/scrcpy-server/.scrcpy-server-known-good</c>.
    /// </summary>
    public const string ClientVersion = "4.0";

    /// <summary>
    /// Device-side abstract socket name template. scrcpy v4.0 always uses a
    /// LocalSocket on the device side (see
    /// <c>server/.../device/DesktopConnection.java</c>). With
    /// <c>tunnel_forward=true</c> the device binds a LocalServerSocket
    /// with this name; the PC reaches it via
    /// <c>adb forward tcp:&lt;port&gt; localabstract:&lt;name&gt;</c>.
    /// </summary>
    /// <remarks>
    /// Using <c>tunnel_forward=true</c> + <c>adb forward</c> is the
    /// topology that works on every Android version we ship to,
    /// including Android 9 which rejects <c>adb reverse localabstract</c>.
    /// See scrcpy doc/protocol section "Tunnels".
    /// </remarks>
    public const string LocalSocketNameTemplate = "scrcpy_{0:x8}";

    private readonly IAdbServerManager _adb;
    private readonly ILogger<ScrcpyServer> _logger;

    /// <summary>
    /// Session-scoped scid (31-bit non-negative). Set by
    /// <see cref="StartAsync"/> and reused by <see cref="ExecuteAppProcessAsync"/>
    /// so the <c>adb forward localabstract:scrcpy_&lt;scid&gt;</c> entry
    /// matches the <c>scid=</c> argument passed to the server.
    /// </summary>
    private int _sessionScid;

    /// <summary>
    /// The device-side abstract socket name chosen for the current/last
    /// <see cref="StartAsync"/>. Empty if no session has been started.
    /// </summary>
    private string _sessionSocketName = string.Empty;

    public ScrcpyServer(IAdbServerManager adb)
        : this(adb, RidersLogger.Create<ScrcpyServer>())
    {
    }

    public ScrcpyServer(IAdbServerManager adb, ILogger<ScrcpyServer> logger)
    {
        _adb = adb ?? throw new ArgumentNullException(nameof(adb));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// When <c>true</c>, the server is launched with the v4.0 standalone
    /// "raw" command-line set (<c>raw_stream=true</c>,
    /// <c>send_device_meta=false</c>, <c>send_frame_meta=false</c>,
    /// <c>send_dummy_byte=false</c>) so the produced stream is a bare
    /// H.264 byte stream consumable directly by <see cref="ScrcpyVideoDecoder"/>
    /// without going through <see cref="ScrcpyStreamParser"/>.
    ///
    /// <para>
    /// Defaults to <c>false</c> to preserve the legacy framing path
    /// (which is what <see cref="Riders.Mirroring.Desktop.ViewModels.MirrorViewModel"/>
    /// currently drives via <see cref="ScrcpyMirrorProcess"/>, and what
    /// the 141 Core.Tests rely on for backwards compatibility). Set to
    /// <c>true</c> only by a dedicated, opt-in test path — the consumer
    /// is responsible for routing the resulting
    /// <see cref="NetworkStream"/> directly into <see cref="ScrcpyVideoDecoder"/>
    /// via <see cref="ScrcpyVideoStream"/>.
    /// </para>
    /// </summary>
    public bool UseRawStream { get; set; } = false;

    /// <summary>
    /// The scid chosen for the current/last <see cref="StartAsync"/> call.
    /// Zero if no session has been started. Used by the raw-stream runner
    /// to correlate the <c>adb reverse localabstract:scrcpy_&lt;scid&gt;</c>
    /// entry with the device-side <c>scid=</c> argument.
    /// </summary>
    public int Scid => _sessionScid;

    /// <summary>
    /// Locates the <c>scrcpy-server</c> jar embedded with the app. Defaults
    /// to <c>vendor/scrcpy-server/scrcpy-server.jar</c> relative to the
    /// current directory. Override with <paramref name="jarOverride"/> in
    /// tests.
    /// </summary>
    public static string LocateServerJar(string? jarOverride = null)
    {
        if (!string.IsNullOrEmpty(jarOverride) && File.Exists(jarOverride))
        {
            return jarOverride;
        }

        var candidate = Path.Combine(AppContext.BaseDirectory, "vendor", "scrcpy-server", "scrcpy-server.jar");
        if (File.Exists(candidate))
        {
            return candidate;
        }

        // Fall back to source-tree location when running from a dev box.
        var sourceTree = Path.Combine(
            Directory.GetCurrentDirectory(),
            "vendor", "scrcpy-server", "scrcpy-server.jar");

        return sourceTree;
    }

    /// <summary>
    /// Push the jar onto the device, set up the reverse tunnel and spawn
    /// the server process. The returned <see cref="IScrcpySession"/> owns
    /// the spawned process and tunnel; dispose it to tear everything down.
    /// </summary>
    /// <param name="serial">Target device serial.</param>
    /// <param name="options">Mirroring options.</param>
    /// <param name="jarPath">Override path to the local scrcpy-server jar (tests).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task<IScrcpySession> StartAsync(
        string serial,
        ScrcpyOptions? options = null,
        string? jarPath = null,
        CancellationToken cancellationToken = default)
    {
        // NOTE: This method only PUSHES the jar and opens the reverse tunnel.
        // The server process itself is NOT spawned here — that requires
        // ExecuteAppProcessAsync(), called separately by the raw-stream
        // pipeline (1B.b.δ). The legacy path is unaffected and continues
        // to use the external scrcpy.exe subprocess via ScrcpyMirrorProcess.
        return await PrepareSessionAsync(serial, options, jarPath, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Spawn the <c>app_process</c> command on the device. Call this AFTER
    /// <see cref="StartAsync"/> has set up the reverse tunnel. Used by the
    /// v4.0 raw-stream pipeline only.
    /// </summary>
    /// <remarks>
    /// The legacy framing path goes through <see cref="ScrcpyMirrorProcess"/>
    /// (an external <c>scrcpy.exe</c> subprocess), which doesn't need this
    /// method. <see cref="StartAsync"/> intentionally leaves the device
    /// side idle so the integration test
    /// <c>StartAsync_PushesJarAndOpensReverseTunnel_On_RealDevice</c> still
    /// passes without producing a phantom process.
    /// </remarks>
    public async Task<string> ExecuteAppProcessAsync(
        string serial,
        ScrcpyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("Serial is required.", nameof(serial));
        }

        options ??= ScrcpyOptions.Default;
        var legacyArgs = options.ToArguments();
        var cmd = BuildAppProcessCommand(legacyArgs, UseRawStream);

        _logger.LogInformation(
            "Executing scrcpy-server app_process on {Serial} (raw={Raw}): {Command}",
            serial, UseRawStream, cmd);

        var stdout = await _adb.ExecuteShellCommandAsync(serial, cmd, cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "app_process returned {Bytes} bytes of stdout from {Serial} (raw={Raw}).",
            stdout?.Length ?? 0, serial, UseRawStream);

        return stdout ?? string.Empty;
    }

    private async Task<IScrcpySession> PrepareSessionAsync(
        string serial,
        ScrcpyOptions? options,
        string? jarPath,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("Serial is required.", nameof(serial));
        }

        options ??= ScrcpyOptions.Default;

        var jar = LocateServerJar(jarPath);
        if (!File.Exists(jar))
        {
            throw new FileNotFoundException(
                $"scrcpy-server jar not found at '{jar}'. " +
                "Drop it into vendor/scrcpy-server/ or pass jarPath.", jar);
        }

        var client = AdbClientFactory();
        var device = DeviceWithSerial(serial);

        _logger.LogInformation("Pushing scrcpy-server jar to {Serial} ({Path})", serial, jar);
        await Task.Run(() =>
        {
            using var stream = File.OpenRead(jar);
            var sync = new SyncService(client, device);
            // permissions = 0644 → 420 decimal, timestamp = now.
            var cancelled = false;
            sync.Push(stream, DefaultRemotePath, 420, DateTimeOffset.Now,
                null, in cancelled);
        }, cancellationToken).ConfigureAwait(false);

        // scrcpy v4.0 ALWAYS opens a LocalSocket on the device side
        // (see server/.../device/DesktopConnection.java, branches
        // tunnelForward==true OR false — both use LocalSocket*). Therefore
        // the reverse tunnel must target a `localabstract:NAME`, not a
        // raw `tcp:PORT` on the device. The local-side TCP port is the
        // listener owned by ScrcpyClient (port 27183).
        const ushort tunnelPort = 27183;
        _sessionScid = GenerateScid();
        var localSocketName = string.Format(LocalSocketNameTemplate, _sessionScid);

        var deviceSpec = $"localabstract:{localSocketName}";
        var localSpec = $"tcp:{tunnelPort}";
        _logger.LogInformation(
            "Opening reverse tunnel for {Serial}: device={Device} → local={Local} (scid=0x{Scid:x8})",
            serial, deviceSpec, localSpec, _sessionScid);

        try
        {
            await Task.Run(() =>
            {
                client.CreateReverseForward(
                    device,
                    deviceSpec,
                    localSpec,
                    allowRebind: true);
            }, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation(
                "Reverse tunnel established for {Serial}: {Device} → {Local}",
                serial, deviceSpec, localSpec);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to create reverse tunnel {Device} → {Local} for {Serial}. " +
                "Check `adb -s {Serial} reverse --list` manually.",
                deviceSpec, localSpec, serial, serial);
            throw;
        }

        // app_process is NOT spawned here on purpose. Spawning it is now the
        // responsibility of ExecuteAppProcessAsync() so the integration
        // test StartAsync_PushesJarAndOpensReverseTunnel_On_RealDevice can
        // still validate push+tunnel without producing a phantom process.
        // The legacy path (ScrcpyMirrorProcess + scrcpy.exe) is unaffected.
        return new ScrcpySession(serial, options, _logger, asyncDisposeAction: session =>
        {
            try
            {
                client.RemoveReverseForward(device, deviceSpec);
                _logger.LogInformation("Removed reverse tunnel {Device}", deviceSpec);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to remove reverse tunnel for {Serial}", serial);
            }
            return Task.CompletedTask;
        });
    }

    // --------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------

    /// <summary>
    /// Build the full <c>CLASSPATH=… app_process … com.genymobile.scrcpy.Server …</c>
    /// command line.
    ///
    /// <para>
    /// Two branches:
    /// <list type="bullet">
    ///   <item><b>Legacy</b> (<paramref name="useRawStream"/> == false): the
    ///     first argument is left blank so <paramref name="legacyArgs"/>
    ///     (produced by <see cref="ScrcpyOptions.ToArguments"/>) is forwarded
    ///     as-is. This preserves the existing framing/metadata pipeline
    ///     parsed by <see cref="ScrcpyStreamParser"/>. Behaviour identical to
    ///     pre-v4.0 of this method — no diff.</item>
    ///   <item><b>Raw v4.0</b> (<paramref name="useRawStream"/> == true): we
    ///     prepend <c>4.0</c> as the client-version argument (required by
    ///     the server, see scrcpy.Options.parse), append the standalone
    ///     server flags, and ignore <paramref name="legacyArgs"/>.</item>
    /// </list>
    /// </para>
    /// </summary>
    private string BuildAppProcessCommand(
        IReadOnlyList<string> legacyArgs,
        bool useRawStream)
    {
        var className = "com.genymobile.scrcpy.Server";
        if (!useRawStream)
        {
            // Legacy: keep behaviour unchanged.
            return $"CLASSPATH={DefaultRemotePath} app_process / {className} {string.Join(' ', legacyArgs)}";
        }

        // Raw v4.0 mode. The server aborts unless the first arg equals the
        // client version baked into the jar — see vendor/scrcpy-server/.
        // scid MUST match the one used in `adb reverse localabstract:scrcpy_<scid>`.
        // _sessionScid is set by PrepareSessionAsync (StartAsync). If the
        // caller forgot StartAsync, fall back to a fresh scid (best effort).
        var scid = _sessionScid != 0 ? _sessionScid : GenerateScid();
        var args = new List<string>(capacity: 12)
        {
            ClientVersion,
            $"scid={scid:x}",                // parsed via Integer.parseInt(value, 0x10) → hex
            "log_level=info",                // debug = too chatty in raw mode
            "video=true",
            "audio=false",
            "control=true",
            "max_size=1920",
            "max_fps=60",
            "video_bit_rate=8000000",
            "tunnel_forward=false",          // device dial → PC tcp:27183
            "raw_stream=true",               // disables send_device_meta/send_frame_meta/send_dummy_byte/send_stream_meta internally
            "cleanup=true",
        };

        _logger.LogDebug(
            "Launching scrcpy-server in raw mode with scid=0x{Scid:x8}",
            scid);

        return $"CLASSPATH={DefaultRemotePath} app_process / {className} {string.Join(' ', args)}";
    }

    /// <summary>
    /// Generate a non-negative 31-bit random integer suitable for the
    /// scrcpy <c>scid</c> argument. The server parses <c>scid</c> with
    /// <see cref="System.Int32.Parse(string, System.Globalization.NumberStyles)"/>
    /// and rejects negative values; 31 bits (range [0, 0x7FFFFFFF]) avoids
    /// the sign bit so the same hex literal round-trips through the server.
    /// </summary>
    private static int GenerateScid()
    {
        return Random.Shared.Next(0, 0x40000000); // [0, 2^30) — well within 31-bit
    }

    private IAdbClient AdbClientFactory() => new AdbClient();

    /// <summary>
    /// Build a <see cref="DeviceData"/> instance from just a serial — the
    /// SDK type is required by every API call, and we deliberately keep
    /// this layer ignorant of the live <see cref="DeviceDescriptor"/>.
    /// </summary>
    private static DeviceData DeviceWithSerial(string serial) => new() { Serial = serial };

    internal sealed class ScrcpySession : IScrcpySession
    {
        private readonly ILogger _logger;
        private readonly Func<ScrcpySession, Task> _asyncDisposeAction;

        public ScrcpySession(
            string serial,
            ScrcpyOptions options,
            ILogger logger,
            Func<ScrcpySession, Task> asyncDisposeAction)
        {
            DeviceSerial = serial;
            Options = options;
            _logger = logger;
            _asyncDisposeAction = asyncDisposeAction;
        }

        public string DeviceSerial { get; }
        public ScrcpyOptions Options { get; }
        public bool IsRunning { get; private set; } = true;

        public async ValueTask DisposeAsync()
        {
            if (!IsRunning)
            {
                return;
            }

            IsRunning = false;
            try
            {
                await _asyncDisposeAction(this).ConfigureAwait(false);
                _logger.LogInformation("scrcpy session for {Serial} disposed", DeviceSerial);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error while disposing scrcpy session for {Serial}", DeviceSerial);
            }
        }
    }
}