using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.Core.Tests.Scrcpy;

/// <summary>
/// Unit tests for <see cref="ScrcpyServer"/> covering the testable surface
/// without spinning up a real ADB server or device.
///
/// <para>
/// The full handshake (push jar + reverse tunnel + spawn app_process) is
/// covered by <c>tests/RidersMirroring.IntegrationTests/Scrcpy/ScrcpyServerIntegrationTests.cs</c>
/// which exercises the code path against a real Android device. Here we
/// stick to invariants: jar resolution, null/empty guards, missing-file
/// errors, and the <see cref="IScrcpySession"/> lifecycle.
/// </para>
/// </summary>
public sealed class ScrcpyServerTests
{
    [Fact]
    public void Constructor_NullAdb_Throws()
    {
        var act = () => new ScrcpyServer(adb: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("adb");
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        var act = () => new ScrcpyServer(new StubAdbServerManager(), logger: null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    [Fact]
    public async Task StartAsync_NullOrEmptySerial_Throws()
    {
        var server = new ScrcpyServer(new StubAdbServerManager(), NullLogger<ScrcpyServer>.Instance);

        await FluentActions.Invoking(() => server.StartAsync(string.Empty))
            .Should().ThrowAsync<ArgumentException>().WithParameterName("serial");
        await FluentActions.Invoking(() => server.StartAsync("   "))
            .Should().ThrowAsync<ArgumentException>().WithParameterName("serial");
        await FluentActions.Invoking(() => server.StartAsync(null!))
            .Should().ThrowAsync<ArgumentException>().WithParameterName("serial");
    }

    [Fact]
    public async Task StartAsync_MissingJar_ThrowsFileNotFound()
    {
        var server = new ScrcpyServer(new StubAdbServerManager(), NullLogger<ScrcpyServer>.Instance);
        var missingJar = Path.Combine(
            Path.GetTempPath(),
            $"does-not-exist-{Guid.NewGuid():N}.jar");

        var act = () => server.StartAsync("SERIAL123", jarPath: missingJar);

        // We don't assert ex.FileName == missingJar because LocateServerJar
        // falls back to the base-dir / source-tree location when the override
        // doesn't exist; the thrown exception refers to whatever path it ended
        // up trying. Just verify the error type and that *something* jar-shaped
        // is reported.
        await act.Should().ThrowAsync<FileNotFoundException>()
            .Where(ex => ex.FileName!.EndsWith("scrcpy-server.jar", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void LocateServerJar_OverrideWins_WhenFileExists()
    {
        // Create a temp file and point at it.
        var tempJar = Path.Combine(Path.GetTempPath(), $"scrcpy-server-{Guid.NewGuid():N}.jar");
        File.WriteAllBytes(tempJar, [0x50, 0x4B, 0x03, 0x04]); // ZIP magic

        try
        {
            var resolved = ScrcpyServer.LocateServerJar(tempJar);

            resolved.Should().Be(tempJar);
        }
        finally
        {
            File.Delete(tempJar);
        }
    }

    [Fact]
    public void LocateServerJar_OverrideIgnored_WhenFileMissing()
    {
        // Override path that doesn't exist should fall through to the
        // default locations rather than returning a non-existent path.
        var missing = Path.Combine(Path.GetTempPath(), $"nope-{Guid.NewGuid():N}.jar");

        var resolved = ScrcpyServer.LocateServerJar(missing);

        // Either the bin/Debug/vendor fallback or the source-tree fallback.
        // Just verify it isn't the bogus override we passed in.
        resolved.Should().NotBe(missing);
    }

    [Fact]
    public async Task ScrcpySession_DisposeAsync_CallsDisposeActionOnce()
    {
        var calls = 0;
        var session = new ScrcpyServer.ScrcpySession(
            serial: "S123",
            options: ScrcpyOptions.Default,
            logger: NullLogger.Instance,
            asyncDisposeAction: _ =>
            {
                calls++;
                return Task.CompletedTask;
            });

        await session.DisposeAsync();
        await session.DisposeAsync();
        await session.DisposeAsync();

        calls.Should().Be(1, "DisposeAsync must be idempotent — no tunnel to remove after the first call");
        session.IsRunning.Should().BeFalse();
    }

    [Fact]
    public async Task ScrcpySession_DisposeAsync_SwallowsExceptions_FromDisposeAction()
    {
        var session = new ScrcpyServer.ScrcpySession(
            serial: "S123",
            options: ScrcpyOptions.Default,
            logger: NullLogger.Instance,
            asyncDisposeAction: _ => throw new InvalidOperationException("simulated"));

        var act = async () => await session.DisposeAsync();

        await act.Should().NotThrowAsync("exceptions from the dispose hook are logged and swallowed");
        session.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void ScrcpySession_ExposesSerialAndOptions()
    {
        var options = new ScrcpyOptions { MaxFps = 30, MaxBitrateMbps = 12 };
        var session = new ScrcpyServer.ScrcpySession(
            serial: "S123",
            options: options,
            logger: NullLogger.Instance,
            asyncDisposeAction: _ => Task.CompletedTask);

        session.DeviceSerial.Should().Be("S123");
        session.Options.Should().BeSameAs(options);
        session.IsRunning.Should().BeTrue();
    }

    /// <summary>
    /// Minimal <see cref="IAdbServerManager"/> stub. None of its methods are
    /// exercised by these tests because every test that reaches ADB throws
    /// earlier in the pipeline (invalid serial, missing jar file).
    /// </summary>
    private sealed class StubAdbServerManager : IAdbServerManager
    {
        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<IReadOnlyList<DeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DeviceDescriptor>>(Array.Empty<DeviceDescriptor>());
        public Task EnableNetworkTcpipAsync(int port = 5555, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ConnectWirelessAsync(string serial, string host, int port = 5555, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<string> ExecuteShellCommandAsync(string serial, string command, CancellationToken cancellationToken = default) => Task.FromResult(string.Empty);
    }
}