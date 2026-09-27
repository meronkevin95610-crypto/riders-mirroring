using System.Text;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using AdvancedSharpAdbClient.Receivers;
using Microsoft.Extensions.Logging;
using Riders.Mirroring.Core.Logging;

namespace Riders.Mirroring.Core.Adb;

/// <summary>
/// Default <see cref="IAdbServerManager"/> implementation backed by
/// AdvancedSharpAdbClient. Talks to the local ADB server (no
/// <c>adb.exe</c> process of our own) and exposes a few niceties:
/// <list type="bullet">
///   <item>Retry with backoff for the first connection after the server starts.</item>
///   <item>Enriches the raw <c>DeviceData</c> with model / manufacturer / Android version.</item>
/// </list>
/// </summary>
public sealed class AdbServerManager : IAdbServerManager
{
    private static readonly AdbServer DefaultServer = new();

    private readonly ILogger<AdbServerManager> _logger;
    private readonly Func<IAdbClient> _clientFactory;

    /// <summary>
    /// Production constructor — uses <see cref="AdbClient"/> backed by
    /// AdvancedSharpAdbClient's default endpoints (localhost:5037).
    /// </summary>
    public AdbServerManager()
        : this(() => new AdbClient(), RidersLogger.Create<AdbServerManager>())
    {
    }

    /// <summary>Test-friendly constructor.</summary>
    public AdbServerManager(Func<IAdbClient> clientFactory, ILogger<AdbServerManager> logger)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 12;          // ~6 s at 500 ms
        const int delayMs = 500;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var result = DefaultServer.StartServer(
                    adbPath: string.Empty,                // SDK adb or bundled
                    restartServerIfNewer: true);

                _logger.LogInformation(
                    "ADB server start attempt {Attempt} -> {Result}",
                    attempt, result);

                if (result == StartServerResult.Started
                    || result == StartServerResult.AlreadyRunning)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogDebug(ex, "ADB server start attempt {Attempt} failed", attempt);
            }

            try
            {
                await Task.Delay(delayMs, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
        }

        _logger.LogWarning("Could not confirm the ADB server is running after {Max} attempts", maxAttempts);
        return false;
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<DeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        var client = _clientFactory();

        try
        {
            var devices = await Task.Run(() => client.GetDevices().ToList(), cancellationToken).ConfigureAwait(false);

            var results = new List<DeviceDescriptor>(devices.Count);
            foreach (var data in devices)
            {
                cancellationToken.ThrowIfCancellationRequested();
                results.Add(await EnrichAsync(client, data, cancellationToken).ConfigureAwait(false));
            }

            return results;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to enumerate ADB devices");
            return Array.Empty<DeviceDescriptor>();
        }
    }

    /// <inheritdoc />
    public async Task EnableNetworkTcpipAsync(int port = 5555, CancellationToken cancellationToken = default)
    {
        var client = _clientFactory();
        await Task.Run(() =>
        {
            var devices = client.GetDevices().ToList();
            var target = devices.FirstOrDefault(d => d.State == DeviceState.Online);

            if (target is null || IsEmpty(target))
            {
                _logger.LogWarning("adb tcpip {Port}: no online device to target", port);
                return;
            }

            var receiver = new ConsoleOutputReceiver();
            client.ExecuteRemoteCommand($"tcpip {port}", target, receiver, Encoding.UTF8);
            _logger.LogInformation("Sent 'tcpip {Port}' to {Serial}", port, target.Serial);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task ConnectWirelessAsync(string serial, string host, int port = 5555, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("Serial is required.", nameof(serial));
        }

        ArgumentNullException.ThrowIfNull(host);

        var client = _clientFactory();
        await Task.Run(() =>
        {
            // Wireless ADB connect is a host-side operation: we ask the
            // server to dial host:port and report success.
            var response = client.Connect(host, port);
            _logger.LogInformation(
                "Connect to {Host}:{Port} for {Serial} -> {Response}",
                host, port, serial, response);
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<string> ExecuteShellCommandAsync(string serial, string command, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(serial))
        {
            throw new ArgumentException("Serial is required.", nameof(serial));
        }

        ArgumentNullException.ThrowIfNull(command);

        var client = _clientFactory();
        return await Task.Run(() =>
        {
            var device = new DeviceData { Serial = serial };
            var receiver = new ConsoleOutputReceiver();
            client.ExecuteRemoteCommand(command, device, receiver, Encoding.UTF8);
            var output = receiver.ToString().Trim();
            _logger.LogInformation("Shell command on {Serial} ('{Command}') -> '{Output}'", serial, command, output);
            return output;
        }, cancellationToken).ConfigureAwait(false);
    }

    // --------------------------------------------------------------------
    // Helpers
    // --------------------------------------------------------------------

    private async Task<DeviceDescriptor> EnrichAsync(IAdbClient client, DeviceData data, CancellationToken cancellationToken)
    {
        var transport = data.Serial.Contains(':')
            ? DeviceTransport.Network
            : DeviceTransport.Usb;

        string? model = null, manufacturer = null, androidVersion = null;
        int? sdk = null;

        // Prefer the model that ADB already parsed out of `adb devices -l`
        // — it avoids a round-trip per device. Fall back to getprop for the
        // other props.
        if (!string.IsNullOrEmpty(data.Model))
        {
            model = data.Model;
        }

        if (data.State == DeviceState.Online)
        {
            try
            {
                manufacturer   = await GetPropAsync(client, data.Serial, "ro.product.manufacturer", cancellationToken).ConfigureAwait(false);
                androidVersion = await GetPropAsync(client, data.Serial, "ro.build.version.release", cancellationToken).ConfigureAwait(false);
                var sdkRaw     = await GetPropAsync(client, data.Serial, "ro.build.version.sdk",    cancellationToken).ConfigureAwait(false);
                if (int.TryParse(sdkRaw, out var parsed))
                {
                    sdk = parsed;
                }

                if (model is null)
                {
                    model = await GetPropAsync(client, data.Serial, "ro.product.model", cancellationToken).ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Enrich failed for {Serial}", data.Serial);
            }
        }

        return new DeviceDescriptor(
            Serial: data.Serial,
            State: data.State.ToString(),
            Model: model,
            Manufacturer: manufacturer,
            AndroidVersion: androidVersion,
            Sdk: sdk,
            Transport: transport);
    }

    private async Task<string?> GetPropAsync(IAdbClient client, string serial, string prop, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            var device = new DeviceData { Serial = serial };
            var receiver = new ConsoleOutputReceiver();
            client.ExecuteRemoteCommand($"getprop {prop}", device, receiver, Encoding.UTF8);
            var value = receiver.ToString().Trim();
            return string.IsNullOrEmpty(value) ? null : value;
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 3.0.9 doesn't expose <c>DeviceData.IsEmpty</c> as a property; emulate
    /// it via the public serial/transport-id fields.
    /// </summary>
    private static bool IsEmpty(DeviceData d) =>
        string.IsNullOrEmpty(d.Serial) && string.IsNullOrEmpty(d.TransportId);
}