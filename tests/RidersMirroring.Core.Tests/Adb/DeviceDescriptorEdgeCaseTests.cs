using FluentAssertions;
using Riders.Mirroring.Core.Adb;

namespace Riders.Mirroring.Core.Tests.Adb;

public class DeviceDescriptorEdgeCaseTests
{
    [Fact]
    public void DisplayName_FallsBackToSerial_WhenNoModelOrManufacturer()
    {
        var d = new DeviceDescriptor(
            Serial: "emulator-5554",
            State: "device",
            Model: null,
            Manufacturer: null,
            AndroidVersion: null,
            Sdk: null,
            Transport: DeviceTransport.Usb);

        d.DisplayName.Should().Be("emulator-5554 — USB");
    }

    [Fact]
    public void DisplayName_PrefersModelOverManufacturer()
    {
        var d = new DeviceDescriptor(
            Serial: "ABC",
            State: "device",
            Model: "Pixel 8",
            Manufacturer: "Google",
            AndroidVersion: "14",
            Sdk: 34,
            Transport: DeviceTransport.Network);

        d.DisplayName.Should().Be("Pixel 8 — Wi-Fi");
    }

    [Fact]
    public void IsOnline_AcceptsLowerCase_DeviceString()
    {
        var d = new DeviceDescriptor("X", "device", null, null, null, null, DeviceTransport.Usb);
        d.IsOnline.Should().BeTrue();
    }

    [Fact]
    public void IsOnline_AcceptsUpperCase_Online()
    {
        var d = new DeviceDescriptor("X", "Online", null, null, null, null, DeviceTransport.Usb);
        d.IsOnline.Should().BeTrue();
    }

    [Fact]
    public void IsOnline_RejectsOffline()
    {
        var d = new DeviceDescriptor("X", "offline", null, null, null, null, DeviceTransport.Usb);
        d.IsOnline.Should().BeFalse();
    }

    [Fact]
    public void IsOnline_RejectsUnauthorized()
    {
        var d = new DeviceDescriptor("X", "unauthorized", null, null, null, null, DeviceTransport.Usb);
        d.IsOnline.Should().BeFalse();
    }

    [Fact]
    public void IsOnline_RejectsEmpty()
    {
        var d = new DeviceDescriptor("X", "", null, null, null, null, DeviceTransport.Usb);
        d.IsOnline.Should().BeFalse();
    }

    [Fact]
    public void Transport_Network_ProducesWifiSuffix()
    {
        var d = new DeviceDescriptor("X", "device", "Pixel", null, null, null, DeviceTransport.Network);
        d.DisplayName.Should().EndWith("Wi-Fi");
    }

    [Fact]
    public void Transport_Unknown_ProducesUnknownSuffix()
    {
        var d = new DeviceDescriptor("X", "device", "Pixel", null, null, null, DeviceTransport.Unknown);
        d.DisplayName.Should().EndWith("unknown");
    }
}
