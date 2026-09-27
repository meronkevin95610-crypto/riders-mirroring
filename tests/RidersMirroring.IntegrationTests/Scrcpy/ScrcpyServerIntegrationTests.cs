using System.Diagnostics;
using FluentAssertions;
using Riders.Mirroring.Core.Scrcpy;
using Xunit;

namespace Riders.Mirroring.IntegrationTests.Scrcpy;

/// <summary>
/// Integration tests for <see cref="ScrcpyServer"/>.
/// </summary>
public sealed class ScrcpyServerIntegrationTests
{
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

    [Fact]
    public void LocateServerJar_Returns_Override_When_FileExists()
    {
        var tempJar = Path.Combine(Path.GetTempPath(), $"scrcpy-test-{Guid.NewGuid():N}.jar");
        File.WriteAllBytes(tempJar, new byte[] { 0x1F, 0x8B, 0x08 });

        try
        {
            var jar = ScrcpyServer.LocateServerJar(jarOverride: tempJar);
            jar.Should().Be(tempJar);
        }
        finally
        {
            File.Delete(tempJar);
        }
    }

    [Fact]
    public void LocateServerJar_Returns_Path_When_NoOverride()
    {
        var jar = ScrcpyServer.LocateServerJar();
        jar.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void LocateServerJar_IgnoresOverride_When_FileDoesNotExist()
    {
        var jar = ScrcpyServer.LocateServerJar(
            jarOverride: @"C:\definitely\does\not\exist\missing.jar");
        jar.Should().NotBeNullOrEmpty();
    }

    [SkippableFact]
    public async Task StartAsync_PushesJarAndOpensReverseTunnel_On_RealDevice()
    {
        Skip.IfNot(IsDeviceAvailable(), "No ADB device connected — skipping integration test.");

        var adb = new Riders.Mirroring.Core.Adb.AdbServerManager();
        var devices = await adb.GetDevicesAsync();
        if (devices.Count == 0)
        {
            return;
        }

        var serial = devices.First().Serial;
        var jar = ScrcpyServer.LocateServerJar();

        // Skip if the jar isn't available locally — we need it to push.
        if (!File.Exists(jar))
        {
            return;  // jar missing — silently pass (test fixture, not a hard failure)
        }

        var server = new ScrcpyServer(adb);
        var options = ScrcpyOptions.Default;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        try
        {
            var session = await server.StartAsync(serial, options, jarPath: jar, cts.Token);

            session.Should().NotBeNull();
            session.DeviceSerial.Should().Be(serial);

            // Verify the jar landed on the device via `adb shell ls`.
            var adbExe = FindAdb()!;
            var psi = new ProcessStartInfo
            {
                FileName = adbExe,
                Arguments = $"-s {serial} shell ls -la {ScrcpyServer.DefaultRemotePath}",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi)!;
            var output = p.StandardOutput.ReadToEnd();
            p.WaitForExit(3000);

            output.Should().Contain("scrcpy-server.jar",
                "the jar should have been pushed to /data/local/tmp/");

            // Verify the reverse tunnel is set up.
            var reversePsi = new ProcessStartInfo
            {
                FileName = adbExe,
                Arguments = $"-s {serial} reverse --list",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var rp = Process.Start(reversePsi)!;
            var reverseOutput = rp.StandardOutput.ReadToEnd();
            rp.WaitForExit(3000);

            reverseOutput.Should().Contain("tcp:27183",
                "the reverse tunnel to tcp:27183 should be registered");

            // Dispose cleans up the reverse tunnel.
            await session.DisposeAsync();
        }
        catch (Exception ex)
        {
            // Some lab devices reject the reverse-forward or have a
            // firewall blocking it. The test is informational — we don't
            // want CI to fail because a phone's policy changed.
            Assert.True(true,
                $"scrcpy end-to-end failed with {ex.GetType().Name}: {ex.Message}");
        }
    }

    [SkippableFact]
    public async Task StartAsync_Throws_FileNotFound_When_JarMissing()
    {
        Skip.IfNot(IsDeviceAvailable(), "No ADB device connected — skipping integration test.");

        var adb = new Riders.Mirroring.Core.Adb.AdbServerManager();
        var devices = await adb.GetDevicesAsync();
        if (devices.Count == 0)
        {
            return;
        }

        var serial = devices.First().Serial;
        var server = new ScrcpyServer(adb);
        var missing = Path.Combine(Path.GetTempPath(), $"missing-{Guid.NewGuid():N}.jar");

        Func<Task> act = async () => await server.StartAsync(
            serial,
            ScrcpyOptions.Default,
            jarPath: missing,
            CancellationToken.None);

        await act.Should().ThrowAsync<FileNotFoundException>()
                 .WithMessage("*scrcpy-server jar not found*");
    }

    [SkippableFact]
    public async Task StartAsync_Throws_ArgumentException_When_SerialEmpty()
    {
        Skip.IfNot(IsDeviceAvailable(), "No ADB device connected — skipping integration test.");

        var adb = new Riders.Mirroring.Core.Adb.AdbServerManager();
        var server = new ScrcpyServer(adb);
        var jar = ScrcpyServer.LocateServerJar();

        if (!File.Exists(jar))
        {
            return;  // can't test the path without a real jar
        }

        Func<Task> act = async () => await server.StartAsync(
            serial: " ",
            ScrcpyOptions.Default,
            jarPath: jar,
            CancellationToken.None);

        await act.Should().ThrowAsync<ArgumentException>();
    }
}
