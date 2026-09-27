using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Riders.Mirroring.Core.Adb;
using Xunit;

namespace Riders.Mirroring.Core.Tests.Adb;

public class AdbDeviceWatcherTests
{
    [Fact]
    public async Task FirstSnapshot_IsPublishedSynchronously()
    {
        var fake = new FakeAdbServerManager
        {
            DevicesToReturn = new[]
            {
                new DeviceDescriptor(
                    Serial: "abc",
                    State: "Online",
                    Model: "Pixel 8",
                    Manufacturer: "Google",
                    AndroidVersion: "14",
                    Sdk: 34,
                    Transport: DeviceTransport.Usb),
            },
        };

        DeviceSnapshotEventArgs? received = null;
        var watcher = new AdbDeviceWatcher(
            fake,
            NullLogger<AdbDeviceWatcher>.Instance,
            TimeSpan.FromMilliseconds(50));

        watcher.SnapshotChanged += (_, e) => received = e;

        watcher.Start();

        // Wait up to 2 s for the first poll cycle to fire.
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (received is null && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
        }

        received.Should().NotBeNull();
        received!.Devices.Should().HaveCount(1);
        received.Devices[0].Serial.Should().Be("abc");

        await watcher.StopAsync();
        watcher.Dispose();
    }

    [Fact]
    public async Task IdenticalSnapshots_AreNotPublishedTwice()
    {
        var fake = new FakeAdbServerManager
        {
            DevicesToReturn = new[]
            {
                new DeviceDescriptor(
                    Serial: "abc", State: "Online",
                    Model: "Pixel 8", Manufacturer: "Google",
                    AndroidVersion: "14", Sdk: 34,
                    Transport: DeviceTransport.Usb),
            },
        };

        var publishCount = 0;
        var watcher = new AdbDeviceWatcher(
            fake,
            NullLogger<AdbDeviceWatcher>.Instance,
            TimeSpan.FromMilliseconds(50));

        watcher.SnapshotChanged += (_, _) => publishCount++;
        watcher.Start();

        // Wait long enough for several poll cycles.
        await Task.Delay(400);

        await watcher.StopAsync();
        watcher.Dispose();

        publishCount.Should().Be(1, "the snapshot did not change between polls");
    }
}