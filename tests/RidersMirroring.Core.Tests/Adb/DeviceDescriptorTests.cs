using FluentAssertions;
using Riders.Mirroring.Core.Adb;
using Xunit;

namespace Riders.Mirroring.Core.Tests.Adb;

public class DeviceDescriptorTests
{
    [Fact]
    public void DisplayName_PrefersModelOverSerial()
    {
        var d = new DeviceDescriptor(
            Serial: "R5CT70ABCD123",
            State: "Online",
            Model: "Pixel 8",
            Manufacturer: "Google",
            AndroidVersion: "14",
            Sdk: 34,
            Transport: DeviceTransport.Usb);

        d.DisplayName.Should().Be("Pixel 8 — USB");
    }

    [Fact]
    public void DisplayName_FallsBackToSerialWhenModelMissing()
    {
        var d = new DeviceDescriptor(
            Serial: "emulator-5554",
            State: "Online",
            Model: null,
            Manufacturer: null,
            AndroidVersion: null,
            Sdk: null,
            Transport: DeviceTransport.Network);

        d.DisplayName.Should().Be("emulator-5554 — Wi-Fi");
    }

    [Fact]
    public void IsOnline_OnlyWhenStateIsOnline()
    {
        var online = new DeviceDescriptor("s", "Online", null, null, null, null, DeviceTransport.Usb);
        var offline = new DeviceDescriptor("s", "Offline", null, null, null, null, DeviceTransport.Usb);
        var device = new DeviceDescriptor("s", "device", null, null, null, null, DeviceTransport.Usb);

        online.IsOnline.Should().BeTrue();
        offline.IsOnline.Should().BeFalse();
        device.IsOnline.Should().BeTrue("scrcpy returns lowercase 'device' for online");
    }
}