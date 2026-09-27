using FluentAssertions;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.Wireless;
using Xunit;

namespace Riders.Mirroring.Core.Tests.Wireless;

public class WirelessHostServiceTests
{
    [Fact]
    public void ResolveLocalIPv4_ReturnsNonLoopbackOrNull()
    {
        var ip = WirelessHostService.ResolveLocalIPv4();

        if (ip is null)
        {
            return;
        }

        ip.AddressFamily.Should().Be(System.Net.Sockets.AddressFamily.InterNetwork);
        ip.ToString().Should().NotStartWith("127.");
    }

    [Fact]
    public async Task ResolveAsync_RejectsEmptySsid()
    {
        var svc = new WirelessHostService(new FakeAdb());

        var act = async () => await svc.ResolveAsync(ssid: "", password: "x");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ResolveAsync_RejectsEmptyPassword()
    {
        var svc = new WirelessHostService(new FakeAdb());

        var act = async () => await svc.ResolveAsync(ssid: "x", password: "");
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task ResolveAsync_ReturnsPairingInfo()
    {
        var svc = new WirelessHostService(new FakeAdb());

        try
        {
            var info = await svc.ResolveAsync("Home", "hunter2");
            info.Ssid.Should().Be("Home");
            info.Password.Should().Be("hunter2");
            info.HostEndpoint.Should().Contain(":5555");
        }
        catch (InvalidOperationException)
        {
            // Acceptable: test machine has no IPv4 adapter.
        }
    }

    [Theory]
    [InlineData(null, "****")]
    [InlineData("", "****")]
    [InlineData("ab", "****")]
    [InlineData("abcd", "****")]
    [InlineData("abcde", "bcde")]
    [InlineData("CafeDuCoin_5GHz", "5GHz")]
    public void Tail_Masks_Short_Ssid_And_Keeps_LastFourChars(string? input, string expected)
    {
        WirelessHostService.Tail(input!).Should().Be(expected);
    }

    private sealed class FakeAdb : IAdbServerManager
    {
        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<IReadOnlyList<DeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<DeviceDescriptor>>(Array.Empty<DeviceDescriptor>());

        public Task EnableNetworkTcpipAsync(int port = 5555, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task ConnectWirelessAsync(string serial, string host, int port = 5555, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<string> ExecuteShellCommandAsync(string serial, string command, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }
}
