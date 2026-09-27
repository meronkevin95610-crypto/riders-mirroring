using FluentAssertions;
using Riders.Mirroring.Core.Wireless;
using Xunit;

namespace Riders.Mirroring.Core.Tests.Wireless;

public class WirelessPairingInfoTests
{
    [Fact]
    public void ToQrPayload_BuildsValidWifiUri()
    {
        var info = new WirelessPairingInfo(
            Ssid: "HomeNet",
            Password: "secret",
            HostEndpoint: "192.168.1.42:5555");

        info.ToQrPayload().Should().Be("WIFI:T:WPA;S:HomeNet;P:secret;H:false;;");
    }

    [Fact]
    public void ToQrPayload_EscapesSpecialCharacters()
    {
        // Backslash, semicolon, comma, colon, single quote, double quote
        // must each be escaped with a leading backslash.
        var info = new WirelessPairingInfo(
            Ssid: @"My;Net\Work,",
            Password: "p:ass\"w'ord",
            HostEndpoint: "10.0.0.1:5555");

        // The escaped form: backslash -> \\, ; -> \;, , -> \,, : -> \:, " -> \", ' -> \'
        info.ToQrPayload().Should().Be(
            @"WIFI:T:WPA;S:My\;Net\\Work\,;P:p\:ass\""w\'ord;H:false;;");
    }

    [Fact]
    public void ToQrPayload_RespectsHiddenFlag()
    {
        var info = new WirelessPairingInfo(
            Ssid: "HiddenNet",
            Password: "secret",
            HostEndpoint: "10.0.0.1:5555",
            IsHidden: true);

        info.ToQrPayload().Should().Contain("H:true;");
    }
}