using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.Scrcpy;

/// <summary>
/// Manages the lifecycle of an external scrcpy client process for real-time
/// Android screen mirroring and interaction.
/// </summary>
public sealed class ScrcpyMirrorProcess : IScrcpyMirrorService, IDisposable
{
    private readonly ILogger<ScrcpyMirrorProcess> _logger;
    private readonly Func<string?> _binaryLocator;
    private readonly Func<Process> _processFactory;

    private readonly object _lock = new();
    private Process? _process;
    private readonly ConcurrentQueue<string> _stderrTail = new();
    private const int MaxStderrLines = 20;

    public ScrcpyMirrorProcess()
        : this(
            logger: RidersLogger.Create<ScrcpyMirrorProcess>(),
            binaryLocator: DefaultBinaryLocator,
            processFactory: () => new Process { EnableRaisingEvents = true })
    {
    }

    public ScrcpyMirrorProcess(
        ILogger<ScrcpyMirrorProcess>? logger = null,
        Func<string?>? binaryLocator = null,
        Func<Process>? processFactory = null)
    {
        _logger = logger ?? RidersLogger.Create<ScrcpyMirrorProcess>();
        _binaryLocator = binaryLocator ?? DefaultBinaryLocator;
        _processFactory = processFactory ?? (() => new Process { EnableRaisingEvents = true });
    }

    /// <inheritdoc />
    public bool IsRunning
    {
        get
        {
            lock (_lock)
            {
                return _process is { HasExited: false };
            }
        }
    }

    /// <inheritdoc />
    public string? CurrentDeviceSerial { get; private set; }

    /// <inheritdoc />
    public string? CurrentDeviceDisplayName { get; private set; }

    /// <inheritdoc />
    public event EventHandler<string>? SessionStarted;

    /// <inheritdoc />
    public event EventHandler<MirrorExitEventArgs>? SessionExited;

    /// <inheritdoc />
    public event EventHandler<string>? LogOutputReceived;

    /// <inheritdoc />
    public string? LocateScrcpyBinary() => _binaryLocator();

    /// <summary>
    /// Default search strategy for locating <c>scrcpy.exe</c>.
    /// </summary>
    public static string? DefaultBinaryLocator()
    {
        var candidates = new List<string>();

        // 1. Next to the app binary in vendor/scrcpy/
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "vendor", "scrcpy", "scrcpy.exe"));

        // 2. Source-tree vendor/scrcpy/ during development
        candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), "vendor", "scrcpy", "scrcpy.exe"));

        // 3. User local appdata
        var localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(localApp))
        {
            candidates.Add(Path.Combine(localApp, "Riders Mirroring", "bin", "scrcpy", "scrcpy.exe"));

            // 4. WinGet package directory
            var wingetRoot = Path.Combine(localApp, "Microsoft", "WinGet", "Packages");
            if (Directory.Exists(wingetRoot))
            {
                try
                {
                    var matches = Directory.GetFiles(wingetRoot, "scrcpy.exe", SearchOption.AllDirectories);
                    if (matches.Length > 0)
                    {
                        candidates.AddRange(matches);
                    }
                }
                catch
                {
                    // Ignore access errors in scanning
                }
            }
        }

        // 5. Scoop shims and apps
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrEmpty(userProfile))
        {
            candidates.Add(Path.Combine(userProfile, "scoop", "apps", "scrcpy", "current", "scrcpy.exe"));
            candidates.Add(Path.Combine(userProfile, "scoop", "shims", "scrcpy.exe"));
        }

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                return c;
            }
        }

        // 6. System PATH lookup
        return ProbePath("scrcpy.exe");
    }

    private static string? ProbePath(string binaryName)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        foreach (var dir in pathEnv.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), binaryName);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }
            catch
            {
                // skip malformed entries
            }
        }

        return null;
    }

    /// <inheritdoc />
    public async Task StartAsync(
        string serial,
        string? deviceDisplayName = null,
        ScrcpyOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("Device serial is required.", nameof(serial));
        }

        lock (_lock)
        {
            if (_process is { HasExited: false })
            {
                if (string.Equals(CurrentDeviceSerial, serial, StringComparison.Ordinal))
                {
                    _logger.LogInformation("scrcpy is already mirroring {Serial}", serial);
                    return;
                }
            }
        }

        // If another session was running, stop it first
        if (IsRunning)
        {
            await StopAsync(cancellationToken).ConfigureAwait(false);
        }

        var binary = _binaryLocator();
        if (string.IsNullOrEmpty(binary) || !File.Exists(binary))
        {
            throw new FileNotFoundException(
                "scrcpy.exe was not found. Please place scrcpy in 'vendor/scrcpy/' or ensure it is installed on PATH.");
        }

        options ??= ScrcpyOptions.Default;
        var title = $"Riders Mirroring — {deviceDisplayName ?? serial}";
        var args = options.ToClientArguments(serial, title);

        _logger.LogInformation("Starting scrcpy for {Serial} with binary '{Binary}'", serial, binary);

        var proc = _processFactory();
        proc.StartInfo.FileName = binary;
        proc.StartInfo.UseShellExecute = false;
        proc.StartInfo.RedirectStandardOutput = true;
        proc.StartInfo.RedirectStandardError = true;
        proc.StartInfo.CreateNoWindow = true;

        // Set working directory to scrcpy binary directory so companion DLLs and scrcpy-server are found
        var binaryDir = Path.GetDirectoryName(binary);
        if (!string.IsNullOrEmpty(binaryDir) && Directory.Exists(binaryDir))
        {
            proc.StartInfo.WorkingDirectory = binaryDir;
        }

        foreach (var arg in args)
        {
            proc.StartInfo.ArgumentList.Add(arg);
        }

        _stderrTail.Clear();

        proc.OutputDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            _logger.LogDebug("[scrcpy {Serial}] {Data}", serial, e.Data);
            LogOutputReceived?.Invoke(this, e.Data);
        };

        proc.ErrorDataReceived += (_, e) =>
        {
            if (string.IsNullOrEmpty(e.Data)) return;
            _logger.LogInformation("[scrcpy {Serial}] {Data}", serial, e.Data);
            _stderrTail.Enqueue(e.Data);
            while (_stderrTail.Count > MaxStderrLines)
            {
                _stderrTail.TryDequeue(out var _);
            }
            LogOutputReceived?.Invoke(this, e.Data);
        };

        proc.Exited += (_, _) =>
        {
            var code = proc.ExitCode;
            var tail = string.Join("\r\n", _stderrTail);
            _logger.LogInformation("scrcpy for {Serial} exited with code {Code}", serial, code);

            lock (_lock)
            {
                _process = null;
                CurrentDeviceSerial = null;
                CurrentDeviceDisplayName = null;
            }

            SessionExited?.Invoke(this, new MirrorExitEventArgs(code, tail));
        };

        if (!proc.Start())
        {
            throw new InvalidOperationException($"Failed to launch scrcpy process from '{binary}'.");
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        lock (_lock)
        {
            _process = proc;
            CurrentDeviceSerial = serial;
            CurrentDeviceDisplayName = deviceDisplayName ?? serial;
        }

        SessionStarted?.Invoke(this, serial);
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        Process? procToStop;
        lock (_lock)
        {
            procToStop = _process;
        }

        if (procToStop is null || procToStop.HasExited)
        {
            lock (_lock)
            {
                _process = null;
                CurrentDeviceSerial = null;
                CurrentDeviceDisplayName = null;
            }
            return;
        }

        _logger.LogInformation("Stopping scrcpy for {Serial}", CurrentDeviceSerial);

        await Task.Run(() =>
        {
            try
            {
                procToStop.CloseMainWindow();
                if (!procToStop.WaitForExit(1500))
                {
                    procToStop.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Error while stopping scrcpy process");
            }
            finally
            {
                lock (_lock)
                {
                    _process = null;
                    CurrentDeviceSerial = null;
                    CurrentDeviceDisplayName = null;
                }
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    public void Dispose()
    {
        try
        {
            StopAsync().GetAwaiter().GetResult();
        }
        catch
        {
            // Suppress dispose exceptions
        }
    }
}
