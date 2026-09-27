namespace Riders.Mirroring.Core.Adb;

/// <summary>
/// Abstraction over <see cref="AdbServerManager"/> for unit tests. The
/// concrete implementation talks to the local ADB server through
/// AdvancedSharpAdbClient; tests substitute a fake.
/// </summary>
public interface IAdbServerManager
{
    /// <summary>
    /// Ensures the ADB server is running. Safe to call repeatedly — the
    /// server is only started if it isn't already listening on its port.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> if the server was reachable after the call.</returns>
    Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Enumerates the devices currently visible to the local ADB server.
    /// Returns an empty list if the server isn't running.
    /// </summary>
    Task<IReadOnlyList<DeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Tells the local server to listen for ADB-over-TCP/IP connections on
    /// the default port (5555). The user still has to physically pair the
    /// device through Wi-Fi debugging — see <c>WirelessHostService</c>.
    /// </summary>
    /// <param name="port">TCP port to listen on. Default 5555.</param>
    Task EnableNetworkTcpipAsync(int port = 5555, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asks an Android device to open an ADB-over-TCP/IP socket and to
    /// connect back to <paramref name="host"/>:<paramref name="port"/>.
    /// </summary>
    Task ConnectWirelessAsync(string serial, string host, int port = 5555, CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes a shell command on the specified device and returns the command output.
    /// </summary>
    /// <param name="serial">Target device serial.</param>
    /// <param name="command">Shell command to run.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Standard output trimmed of whitespace.</returns>
    Task<string> ExecuteShellCommandAsync(string serial, string command, CancellationToken cancellationToken = default);
}