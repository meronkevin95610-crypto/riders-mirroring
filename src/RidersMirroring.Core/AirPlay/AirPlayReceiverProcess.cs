using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.AirPlay;

/// <summary>
/// Default <see cref="IAirPlayReceiver"/> implementation: spawns UxPlay
/// (or any compatible binary) as a subprocess, parses its stdout to
/// maintain the list of active sessions, and surfaces lifecycle events to
/// the UI.
/// </summary>
/// <remarks>
/// <para>
/// UxPlay is GPLv3 — we deliberately treat it as a separate process and do
/// not link its library. The combination with our MIT code is an
/// "aggregation", which avoids the GPL "derivative work" trigger.
/// </para>
/// <para>
/// The wrapper looks for UxPlay under:
/// <list type="bullet">
///   <item><c>%LOCALAPPDATA%\Riders Mirroring\bin\uxplay\uxplay.exe</c></item>
///   <item><c>vendor\uxplay\uxplay.exe</c> (bundled next to the app)</item>
///   <item><c>PATH</c> (last resort)</item>
/// </list>
/// </para>
/// </remarks>
public sealed class AirPlayReceiverProcess : IAirPlayReceiver
{
    private readonly ILogger<AirPlayReceiverProcess> _logger;
    private readonly Func<Process> _processFactory;
    private readonly Func<string?> _binaryLocator;

    private readonly object _lock = new();
    private readonly List<AirPlaySession> _sessions = new();
    private int _nextSessionId = 1;
    private Process? _process;
    private string _lastStatusLine = string.Empty;
    private readonly ConcurrentQueue<string> _stderrTail = new();

    public AirPlayReceiverProcess()
        : this(
            logger: RidersLogger.Create<AirPlayReceiverProcess>(),
            binaryLocator: DefaultBinaryLocator,
            processFactory: () => new Process { EnableRaisingEvents = true })
    {
    }

    /// <summary>Test-friendly constructor.</summary>
    public AirPlayReceiverProcess(
        ILogger<AirPlayReceiverProcess> logger,
        Func<string?> binaryLocator,
        Func<Process> processFactory)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _binaryLocator = binaryLocator ?? throw new ArgumentNullException(nameof(binaryLocator));
        _processFactory = processFactory ?? throw new ArgumentNullException(nameof(processFactory));
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
    public string LastStatusLine
    {
        get { lock (_lock) return _lastStatusLine; }
    }

    /// <inheritdoc />
    public IReadOnlyList<AirPlaySession> ActiveSessions
    {
        get { lock (_lock) return _sessions.ToArray(); }
    }

    /// <inheritdoc />
    public event EventHandler<IReadOnlyList<AirPlaySession>>? SessionsChanged;

    /// <inheritdoc />
    public event EventHandler<AirPlayExitEventArgs>? Exited;

    /// <inheritdoc />
    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            _logger.LogInformation("UxPlay already running; StartAsync is a no-op");
            return Task.CompletedTask;
        }

        var path = _binaryLocator();
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            throw new FileNotFoundException(
                "UxPlay executable not found. Drop uxplay.exe into " +
                "vendor/uxplay/ or %LOCALAPPDATA%\\Riders Mirroring\\bin\\uxplay\\, " +
                "or install it on PATH.", path ?? "(null)");
        }

        var process = _processFactory();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = path,
            // -nc tells UxPlay not to clear the screen on exit (nice for
            // keeping stdout readable in our debug log).
            Arguments = "-nc",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(path) ?? Environment.CurrentDirectory,
        };
        process.OutputDataReceived += OnStdout;
        process.ErrorDataReceived += OnStderr;
        process.Exited += (_, _) =>
        {
            int code = 0;
            try { code = process.ExitCode; } catch { /* may race */ }

            Exited?.Invoke(this, new AirPlayExitEventArgs(code, TakeStderrTail()));
            _logger.LogWarning("UxPlay exited with code {Code}", code);
        };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start UxPlay at '{path}'");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        lock (_lock)
        {
            _process = process;
        }

        _logger.LogInformation("UxPlay started (pid={Pid}) at {Path}", process.Id, path);
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public async Task StopAsync()
    {
        Process? toKill;
        lock (_lock)
        {
            toKill = _process;
            _process = null;
        }

        if (toKill is null)
        {
            return;
        }

        try
        {
            if (!toKill.HasExited)
            {
                toKill.Kill(entireProcessTree: true);
                await toKill.WaitForExitAsync().ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error while stopping UxPlay");
        }
        finally
        {
            toKill.Dispose();
            lock (_lock)
            {
                _sessions.Clear();
                _lastStatusLine = string.Empty;
            }
            SessionsChanged?.Invoke(this, Array.Empty<AirPlaySession>());
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync() => await StopAsync().ConfigureAwait(false);

    // --------------------------------------------------------------------
    // stdout / stderr handling
    // --------------------------------------------------------------------

    private void OnStdout(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
        {
            return;
        }

        var line = e.Data;
        lock (_lock) _lastStatusLine = line;

        var connected = UxPlayLogParser.TryExtractConnectedDevice(line);
        var disconnected = UxPlayLogParser.TryExtractDisconnectedDevice(line);

        if (connected is not null)
        {
            lock (_lock)
            {
                // Reconnect handling: if the device already exists, refresh
                // its timestamp rather than creating a duplicate.
                var existing = _sessions.FirstOrDefault(s =>
                    string.Equals(s.DeviceName, connected, StringComparison.Ordinal));
                if (existing is not null)
                {
                    _sessions.Remove(existing);
                    _sessions.Add(existing with { ConnectedAt = DateTime.Now });
                }
                else
                {
                    _sessions.Add(new AirPlaySession(
                        DeviceName: connected,
                        SessionId: _nextSessionId++,
                        ConnectedAt: DateTime.Now,
                        RemoteEndpoint: null));
                }
            }
            _logger.LogInformation("AirPlay connected: {Device}", connected);
            SessionsChanged?.Invoke(this, ActiveSessions);
        }
        else if (disconnected is not null)
        {
            lock (_lock)
            {
                _sessions.RemoveAll(s =>
                    string.Equals(s.DeviceName, disconnected, StringComparison.Ordinal));
            }
            _logger.LogInformation("AirPlay disconnected: {Device}", disconnected);
            SessionsChanged?.Invoke(this, ActiveSessions);
        }
        else if (UxPlayLogParser.LooksLikeReadyLine(line))
        {
            _logger.LogInformation("UxPlay ready: {Line}", line);
        }
    }

    private void OnStderr(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is null)
        {
            return;
        }

        _stderrTail.Enqueue(e.Data);
        // Cap the queue to ~1 KB so a chatty receiver doesn't grow forever.
        while (_stderrTail.Count > 16)
        {
            _stderrTail.TryDequeue(out _);
        }

        _logger.LogDebug("uxplay stderr: {Line}", e.Data);
    }

    private string? TakeStderrTail()
    {
        if (_stderrTail.IsEmpty)
        {
            return null;
        }

        return string.Join(Environment.NewLine, _stderrTail);
    }

    /// <summary>Default binary locator — looks in known places.</summary>
    private static string? DefaultBinaryLocator()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var candidates = new[]
        {
            Path.Combine(localAppData, "Riders Mirroring", "bin", "uxplay", "uxplay.exe"),
            Path.Combine(AppContext.BaseDirectory, "vendor", "uxplay", "uxplay.exe"),
            // Last resort: ask PATH.
            "uxplay.exe",
            "uxplay",
        };

        foreach (var path in candidates)
        {
            if (Path.IsPathRooted(path) && File.Exists(path))
            {
                return path;
            }
        }

        // For unrooted candidates (PATH lookup), return the first and let the
        // caller try Process.Start — the OS will resolve it.
        return candidates[^2];
    }
}