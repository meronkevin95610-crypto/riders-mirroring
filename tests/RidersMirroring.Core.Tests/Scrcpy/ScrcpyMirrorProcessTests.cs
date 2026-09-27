using FluentAssertions;
using Riders.Mirroring.Core.Scrcpy;
using Xunit;

namespace Riders.Mirroring.Core.Tests.Scrcpy;

public class ScrcpyMirrorProcessTests
{
    [Fact]
    public void DefaultBinaryLocator_ReturnsStringOrNull_WithoutThrowing()
    {
        var locator = ScrcpyMirrorProcess.DefaultBinaryLocator;
        var act = () => locator();
        act.Should().NotThrow();
    }

    [Fact]
    public async Task StartAsync_ThrowsOnMissingSerial()
    {
        var process = new ScrcpyMirrorProcess(binaryLocator: () => "fake.exe");
        var act = () => process.StartAsync(string.Empty);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task StartAsync_ThrowsFileNotFound_WhenBinaryMissing()
    {
        var process = new ScrcpyMirrorProcess(binaryLocator: () => "non_existent_binary_xyz.exe");
        var act = () => process.StartAsync("SERIAL123");
        await act.Should().ThrowAsync<FileNotFoundException>();
    }

    [Fact]
    public async Task StopAsync_IsSafeWhenNotRunning()
    {
        var process = new ScrcpyMirrorProcess();
        var act = () => process.StopAsync();
        await act.Should().NotThrowAsync();
        process.IsRunning.Should().BeFalse();
    }
}
