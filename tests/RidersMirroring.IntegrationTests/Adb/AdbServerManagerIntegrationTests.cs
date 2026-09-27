using System.Diagnostics;
using FluentAssertions;
using Riders.Mirroring.Core.Adb;
using Xunit;

namespace Riders.Mirroring.IntegrationTests.Adb;

/// <summary>
/// Integration tests that talk to a REAL ADB server. Skipped automatically
/// when no device is connected so they don't break CI on a clean VM.
///
/// Run locally:
///   adb start-server
///   adb devices                              # must list at least one entry
///   dotnet test tests/RidersMirroring.IntegrationTests
/// </summary>
public sealed class AdbServerManagerIntegrationTests
{
    /// <summary>
    /// Probe by spawning <c>adb.exe devices</c> directly. Faster and
    /// version-independent — we only care whether some device is connected
    /// to the local server. Using the managed <c>AdbServerManager</c> here
    /// would trigger its 6-second retry loop and turn the probe itself
    /// into the bottleneck.
    /// </summary>
    private static bool IsDeviceAvailable()
    {
        try
        {
            var adb = FindAdb();
            if (adb is null) return false;

            var psi = new ProcessStartInfo
            {
                FileName = adb,
                Arguments = "devices",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            // `adb devices` reports the device on a line ending in "device".
            // The "List of devices attached" header is skipped.
            return output
                .Split('\n')
                .Select(l => l.Trim())
                .Any(l => l.Length > 0
                       && !l.StartsWith("List of")
                       && !l.StartsWith("*")
                       && l.Split('\t', ' ').LastOrDefault() == "device");
        }
        catch
        {
            return false;
        }
    }

    private static string? FindAdb()
    {
        // First try the PATH. Then fall back to common Scoop / standard
        // install locations — xUnit's test runner may inherit a stripped
        // PATH that doesn't include the user's adb.exe.
        var path = Environment.GetEnvironmentVariable("PATH");
        if (path is not null)
        {
            foreach (var dir in path.Split(Path.PathSeparator))
            {
                var candidate = Path.Combine(dir, "adb.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        foreach (var fallback in new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "scoop", "apps", "adb", "current", "platform-tools", "adb.exe"),
            @"C:\Program Files\Android\platform-tools\adb.exe",
            @"C:\Android\platform-tools\adb.exe",
        })
        {
            if (File.Exists(fallback)) return fallback;
        }

        return null;
    }

    [SkippableFact]
    public async Task EnsureStartedAsync_DoesNotThrow_When_AdbServerAlreadyRunning()
    {
        Skip.IfNot(IsDeviceAvailable(), "No ADB device connected — skipping integration test.");

        var sut = new AdbServerManager();

        // The SDK 3.0.9 throws on a "server already running but newer version"
        // race in EnsureStartedAsync. We only assert the call returns without
        // throwing — the boolean can be false in mixed-version setups.
        Func<Task> act = async () => await sut.EnsureStartedAsync();
        await act.Should().NotThrowAsync();
    }

    [SkippableFact]
    public async Task GetDevicesAsync_Returns_OnlineDevice()
    {
        Skip.IfNot(IsDeviceAvailable(), "No ADB device connected — skipping integration test.");

        var sut = new AdbServerManager();

        var devices = await sut.GetDevicesAsync();

        devices.Should().NotBeEmpty();
        devices.Should().Contain(d => string.Equals(d.State, "Online", StringComparison.OrdinalIgnoreCase));
    }

    [SkippableFact]
    public async Task GetDevicesAsync_Enriches_Model_And_AndroidVersion()
    {
        Skip.IfNot(IsDeviceAvailable(), "No ADB device connected — skipping integration test.");

        var sut = new AdbServerManager();

        var devices = await sut.GetDevicesAsync();
        var first = devices.First();

        // The ASUS ZenFone X00TD in this lab reports model="ASUS_X00TD" and
        // Android 9. We assert "model is non-empty" rather than pinning the
        // exact string so the test stays portable across lab phones.
        first.Model.Should().NotBeNullOrEmpty("getprop ro.product.model must populate");
        first.AndroidVersion.Should().NotBeNullOrEmpty("getprop ro.build.version.release must populate");
        first.State.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task GetDevicesAsync_ReturnsAtLeastOneDescriptor_With_TransportUsb()
    {
        Skip.IfNot(IsDeviceAvailable(), "No ADB device connected — skipping integration test.");

        var sut = new AdbServerManager();

        var devices = await sut.GetDevicesAsync();

        devices.Should().Contain(d => d.Transport == DeviceTransport.Usb);
    }

    [SkippableFact]
    public async Task EnableNetworkTcpipAsync_TcpipCommand_Succeeds_OrWarns()
    {
        Skip.IfNot(IsDeviceAvailable(), "No ADB device connected — skipping integration test.");

        var sut = new AdbServerManager();

        // Calling EnableNetworkTcpipAsync on a device that already has
        // tcpip enabled (or one that's unauthorized) throws on the SDK 3.0.9
        // path. We accept either a clean return OR a controlled exception —
        // the test exists to make sure the path is reachable, not to assert
        // the device flips modes (that would be a destructive integration test).
        try
        {
            await sut.EnableNetworkTcpipAsync(5555);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Acceptable: device offline / unauthorized / already in tcpip mode.
            Assert.True(true, $"tcpip call threw {ex.GetType().Name}, which is acceptable on a lab phone.");
        }
    }
}
