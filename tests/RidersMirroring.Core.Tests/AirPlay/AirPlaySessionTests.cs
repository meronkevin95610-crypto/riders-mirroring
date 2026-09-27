using FluentAssertions;
using Riders.Mirroring.Core.AirPlay;
using Xunit;

namespace Riders.Mirroring.Core.Tests.AirPlay;

/// <summary>
/// Unit tests for <see cref="AirPlaySession"/>.
/// </summary>
public sealed class AirPlaySessionTests
{
    [Fact]
    public void DisplayName_FallsBack_To_SessionId_When_DeviceName_IsEmpty()
    {
        var sut = new AirPlaySession(
            DeviceName: "",
            SessionId: 7,
            ConnectedAt: DateTime.UtcNow,
            RemoteEndpoint: "192.168.1.42:7000");

        sut.DisplayName.Should().Be("AirPlay session 7");
    }

    [Fact]
    public void DisplayName_FallsBack_To_SessionId_When_DeviceName_IsNull()
    {
        var sut = new AirPlaySession(
            DeviceName: null!,
            SessionId: 12,
            ConnectedAt: DateTime.UtcNow,
            RemoteEndpoint: null);

        sut.DisplayName.Should().Be("AirPlay session 12");
    }

    [Fact]
    public void DisplayName_Uses_DeviceName_When_NonEmpty()
    {
        var sut = new AirPlaySession(
            DeviceName: "Bob's iPhone",
            SessionId: 1,
            ConnectedAt: DateTime.UtcNow,
            RemoteEndpoint: null);

        sut.DisplayName.Should().Be("Bob's iPhone");
    }

    [Fact]
    public void Id_Is_Stable_For_Same_Inputs()
    {
        var first  = new AirPlaySession("iPhone", 3, DateTime.UtcNow, "1.1.1.1");
        var second = new AirPlaySession("iPhone", 3, DateTime.UtcNow, "2.2.2.2");

        first.Id.Should().Be(second.Id);
    }

    [Fact]
    public void Id_Changes_When_SessionId_Changes()
    {
        var first  = new AirPlaySession("iPhone", 1, DateTime.UtcNow, null);
        var second = new AirPlaySession("iPhone", 2, DateTime.UtcNow, null);

        first.Id.Should().NotBe(second.Id);
    }
}
