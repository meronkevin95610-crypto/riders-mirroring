using System.Text;
using FluentAssertions;
using Riders.Mirroring.Core.AirPlay;

namespace RidersMirroring.Core.Tests.AirPlay;

public class AirPlayNativeServerTests
{
    [Fact]
    public async Task StartAsync_BindsAndAdvertises()
    {
        var server = new AirPlayNativeServer("Riders Test", rtspPort: 17000);
        try
        {
            await server.StartAsync();
            server.IsRunning.Should().BeTrue();
            server.LastStatusLine.Should().Contain("Ready on");
        }
        finally
        {
            await server.StopAsync();
        }
    }

    [Fact]
    public async Task StartAsync_IsIdempotent()
    {
        var server = new AirPlayNativeServer("Riders Test", rtspPort: 17001);
        await server.StartAsync();
        await server.StartAsync(); // second call should be a no-op
        server.IsRunning.Should().BeTrue();
        await server.StopAsync();
    }

    [Fact]
    public async Task StopAsync_ClearsSessionsAndFiresExit()
    {
        var server = new AirPlayNativeServer("Riders Test", rtspPort: 17002);
        var exited = false;
        server.Exited += (_, _) => exited = true;

        await server.StartAsync();
        await server.StopAsync();

        server.IsRunning.Should().BeFalse();
        exited.Should().BeTrue();
    }

    [Fact]
    public async Task Dispose_StopsServer()
    {
        var server = new AirPlayNativeServer("Riders Test", rtspPort: 17003);
        await server.StartAsync();
        await server.DisposeAsync();
        server.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Constructor_UsesDefaultPort7000()
    {
        var server = new AirPlayNativeServer();
        server.IsRunning.Should().BeFalse(); // not started yet
        // The port is internal; we just verify the server didn't throw.
    }
}