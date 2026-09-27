using FluentAssertions;
using Riders.Mirroring.Core.AirPlay;
using Xunit;

namespace Riders.Mirroring.Core.Tests.AirPlay;

public class UxPlayLogParserTests
{
    [Fact]
    public void TryExtractConnectedDevice_ParsesChromiumStyleLine()
    {
        var name = UxPlayLogParser.TryExtractConnectedDevice(
            "[1013/141026.942:INFO:CONSOLE(182)] \"VIDEO has been received from \\\"Bob's iPhone\\\"\"");

        name.Should().Be("Bob's iPhone");
    }

    [Fact]
    public void TryExtractConnectedDevice_ParsesPlainLine()
    {
        UxPlayLogParser.TryExtractConnectedDevice("VIDEO received from AlicePhone")
            .Should().Be("AlicePhone");
    }

    [Fact]
    public void TryExtractConnectedDevice_ReturnsNullOnUnrelated()
    {
        UxPlayLogParser.TryExtractConnectedDevice("Server initialized")
            .Should().BeNull();
    }

    [Fact]
    public void TryExtractDisconnectedDevice_ParsesDisconnectedLine()
    {
        UxPlayLogParser.TryExtractDisconnectedDevice("disconnected Bob's iPhone")
            .Should().Be("Bob's");
    }

    [Fact]
    public void TryExtractDisconnectedDevice_ReturnsNullOnUnrelated()
    {
        UxPlayLogParser.TryExtractDisconnectedDevice("Server stopped cleanly")
            .Should().BeNull();
    }

    [Theory]
    [InlineData("Server initialized",       true)]
    [InlineData("server Initialized ok",     true)]
    [InlineData("Listening on port 7000",   true)]
    [InlineData("listening for clients",    true)]
    [InlineData("Waiting for device",       false)]
    [InlineData("",                         false)]
    public void LooksLikeReadyLine_RecognisesUxPlayStartup(string line, bool expected)
    {
        UxPlayLogParser.LooksLikeReadyLine(line).Should().Be(expected);
    }

    [Fact]
    public void Session_DisplayName_FallsBackToIdWhenNameIsEmpty()
    {
        var s = new AirPlaySession(
            DeviceName: string.Empty,
            SessionId: 42,
            ConnectedAt: DateTime.Now,
            RemoteEndpoint: null);

        s.DisplayName.Should().Be("AirPlay session 42");
        s.Id.Should().Be("#42");
    }

    [Fact]
    public void Session_DisplayName_ShowsDeviceNameWhenPresent()
    {
        var s = new AirPlaySession(
            DeviceName: "iPhone 15 Pro",
            SessionId: 1,
            ConnectedAt: DateTime.Now,
            RemoteEndpoint: "192.168.1.10");

        s.DisplayName.Should().Be("iPhone 15 Pro");
    }
}