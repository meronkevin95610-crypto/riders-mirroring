using FluentAssertions;
using Riders.Mirroring.Core.Usb;

namespace Riders.Mirroring.Core.Tests.Usb;

public class StubUsbConfigurationSwitchTests
{
    [Fact]
    public async Task ApplyAsync_ReturnsNotSupported_ForAnyInput()
    {
        var sut = new StubUsbConfigurationSwitch();
        var result = await sut.ApplyAsync("USB\\VID_18D1&PID_4EE7", UsbConfiguration.WinUsb);
        result.Should().Be(UsbConfigurationResult.NotSupported);
    }

    [Fact]
    public async Task ApplyAsync_ReturnsInvalidArguments_OnEmptyHardwareId()
    {
        var sut = new StubUsbConfigurationSwitch();
        var result = await sut.ApplyAsync("", UsbConfiguration.AndroidComposite);
        result.Should().Be(UsbConfigurationResult.InvalidArguments);
    }

    [Fact]
    public async Task ApplyAsync_ReturnsInvalidArguments_OnWhitespace()
    {
        var sut = new StubUsbConfigurationSwitch();
        var result = await sut.ApplyAsync("   ", UsbConfiguration.ChargeOnly);
        result.Should().Be(UsbConfigurationResult.InvalidArguments);
    }

    [Fact]
    public async Task ResolveHardwareIdAsync_ReturnsNull()
    {
        var sut = new StubUsbConfigurationSwitch();
        var result = await sut.ResolveHardwareIdAsync("emulator-5554");
        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveHardwareIdAsync_Throws_OnEmptySerial()
    {
        var sut = new StubUsbConfigurationSwitch();
        Func<Task> act = async () => await sut.ResolveHardwareIdAsync("");
        await act.Should().ThrowAsync<ArgumentException>();
    }
}
