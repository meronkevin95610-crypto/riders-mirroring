using Riders.Mirroring.Core.Adb;

namespace Riders.Mirroring.Core.Tests.Adb;

/// <summary>
/// Test double for <see cref="IAdbServerManager"/>. Lets each test pre-load
/// the device list and short-circuit <see cref="EnsureStartedAsync"/>.
/// </summary>
internal sealed class FakeAdbServerManager : IAdbServerManager
{
    public bool WasStartCalled { get; private set; }
    public IReadOnlyList<DeviceDescriptor> DevicesToReturn { get; init; } = Array.Empty<DeviceDescriptor>();
    public int GetDevicesCallCount { get; private set; }

    public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
    {
        WasStartCalled = true;
        return Task.FromResult(true);
    }

    public Task<IReadOnlyList<DeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default)
    {
        GetDevicesCallCount++;
        return Task.FromResult(DevicesToReturn);
    }

    public Task EnableNetworkTcpipAsync(int port = 5555, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ConnectWirelessAsync(string serial, string host, int port = 5555, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public List<string> ExecutedCommands { get; } = new();

    public Task<string> ExecuteShellCommandAsync(string serial, string command, CancellationToken cancellationToken = default)
    {
        ExecutedCommands.Add($"{serial}:{command}");
        return Task.FromResult(string.Empty);
    }
}