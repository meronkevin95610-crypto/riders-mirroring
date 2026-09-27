using System.IO;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace Riders.Mirroring.Core.Logging;

/// <summary>
/// Centralised Serilog setup. Writes JSON-formatted events (one per line)
/// to <c>%LOCALAPPDATA%\Riders Mirroring\riders-debug.log</c>, with daily
/// rotation kept for 14 days. Designed so the app can pass
/// <c>ILogger&lt;T&gt;</c> to consumers through
/// <see cref="Microsoft.Extensions.Logging.ILoggerFactory"/>.
/// </summary>
public static class RidersLogger
{
    private static readonly object _gate = new();
    private static ILoggerFactory? _factory;

    /// <summary>
    /// Default log file path. Resolved once on first call to
    /// <see cref="EnsureFactory"/>.
    /// </summary>
    public static string LogFilePath { get; private set; } = string.Empty;

    /// <summary>
    /// Lazily build (or return the cached) <see cref="ILoggerFactory"/>.
    /// Call this from <c>App.OnStartup</c> *before* any code asks for an
    /// <see cref="ILogger{TCategoryName}"/>.
    /// </summary>
    /// <param name="minimumLevel">Default minimum log level. Default is
    /// <see cref="LogLevel.Information"/>.</param>
    public static ILoggerFactory EnsureFactory(LogLevel minimumLevel = LogLevel.Information)
    {
        if (_factory is not null)
        {
            return _factory;
        }

        lock (_gate)
        {
            if (_factory is not null)
            {
                return _factory;
            }

            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Riders Mirroring");

            Directory.CreateDirectory(dir);

            LogFilePath = Path.Combine(dir, "riders-debug.log");

            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Is(MapLevel(minimumLevel))
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .WriteTo.File(
                    path: LogFilePath,
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 14,
                    outputTemplate:
                        "{Timestamp:yyyy-MM-ddTHH:mm:ss.fffZ} " +
                        "{Level:u3} " +
                        "{SourceContext} " +
                        "{Message:lj} " +
                        "{Properties:j}{NewLine}")
                .CreateLogger();

            _factory = LoggerFactory.Create(builder => builder.AddSerilog(dispose: false));
            return _factory;
        }
    }

    /// <summary>
    /// Convenience helper to create a typed logger. Shortcut for
    /// <c>EnsureFactory().CreateLogger&lt;T&gt;()</c>.
    /// </summary>
    public static ILogger<T> Create<T>()
        where T : class
        => EnsureFactory().CreateLogger<T>();

    /// <summary>Flush + close the underlying Serilog logger.</summary>
    public static void Shutdown()
    {
        Log.CloseAndFlush();
        _factory = null;
    }

    private static LogEventLevel MapLevel(LogLevel level) => level switch
    {
        LogLevel.Trace       => LogEventLevel.Verbose,
        LogLevel.Debug       => LogEventLevel.Debug,
        LogLevel.Information => LogEventLevel.Information,
        LogLevel.Warning     => LogEventLevel.Warning,
        LogLevel.Error       => LogEventLevel.Error,
        LogLevel.Critical    => LogEventLevel.Fatal,
        _                    => LogEventLevel.Information,
    };
}