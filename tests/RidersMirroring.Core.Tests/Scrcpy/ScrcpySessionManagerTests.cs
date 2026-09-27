using FluentAssertions;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.Scrcpy;

namespace Riders.Mirroring.Core.Tests.Scrcpy;

/// <summary>
/// Unit tests for <see cref="ScrcpySessionManager"/>. The decoder
/// factory returns a fake decoder so we never touch FFmpeg and never
/// open a real socket — these tests run on every CI agent.
/// </summary>
public class ScrcpySessionManagerTests
{
    [Fact]
    public async Task Constructor_StoresInitialState()
    {
        await using var manager = NewManager();

        manager.Sessions.Should().BeEmpty();
        manager.Count.Should().Be(0);
    }

    [Fact]
    public async Task StartSessionAsync_TwiceForSameSerial_ReturnsSameSession()
    {
        await using var manager = NewManager();

        var first = await manager.StartSessionAsync("serial-1");
        var second = await manager.StartSessionAsync("serial-1");

        first.Should().BeSameAs(second);
        manager.Sessions.Should().HaveCount(1);
        manager.Count.Should().Be(1);
    }

    [Fact]
    public async Task StartSessionAsync_DifferentSerials_CreatesIndependentSessions()
    {
        await using var manager = NewManager();

        var s1 = await manager.StartSessionAsync("serial-1");
        var s2 = await manager.StartSessionAsync("serial-2");
        var s3 = await manager.StartSessionAsync("serial-3");

        s1.Should().NotBeSameAs(s2);
        s2.Should().NotBeSameAs(s3);
        manager.Sessions.Should().HaveCount(3);
        manager.Sessions["serial-1"].Should().BeSameAs(s1);
        manager.Sessions["serial-2"].Should().BeSameAs(s2);
        manager.Sessions["serial-3"].Should().BeSameAs(s3);
    }

    [Fact]
    public async Task StartSessionAsync_RaisesSessionsChanged_OnAdd()
    {
        await using var manager = NewManager();

        var events = new List<SessionsChangedEventArgs>();
        manager.SessionsChanged += (_, e) => events.Add(e);

        await manager.StartSessionAsync("serial-A");

        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(SessionsChangedKind.Added);
        events[0].DeviceSerial.Should().Be("serial-A");
    }

    [Fact]
    public async Task StopSessionAsync_RemovesSessionAndRaisesEvent()
    {
        await using var manager = NewManager();

        await manager.StartSessionAsync("serial-X");

        var events = new List<SessionsChangedEventArgs>();
        manager.SessionsChanged += (_, e) => events.Add(e);

        await manager.StopSessionAsync("serial-X");

        manager.Sessions.Should().BeEmpty();
        manager.Count.Should().Be(0);
        events.Should().HaveCount(1);
        events[0].Kind.Should().Be(SessionsChangedKind.Removed);
        events[0].DeviceSerial.Should().Be("serial-X");
    }

    [Fact]
    public async Task StopSessionAsync_UnknownSerial_IsNoOp()
    {
        await using var manager = NewManager();

        // Should not throw.
        await manager.StopSessionAsync("never-started");
        manager.Sessions.Should().BeEmpty();
    }

    [Fact]
    public async Task DisposeAsync_RemovesAllSessions()
    {
        await using var manager = NewManager();

        await manager.StartSessionAsync("a");
        await manager.StartSessionAsync("b");
        await manager.StartSessionAsync("c");

        manager.Count.Should().Be(3);

        await manager.DisposeAsync();

        manager.Count.Should().Be(0);
    }

    private static ScrcpySessionManager NewManager() => new(
        new FakeAdbServerManager(),
        new FakeDecoderFactory(),
        serial => new FakeScrcpySession(serial));
}

/// <summary>
/// In-memory <see cref="IAdbServerManager"/> that returns success for any
/// call without touching real hardware. We need this because constructing
/// a <see cref="ScrcpySession"/> requires the manager (it's passed into
/// <see cref="ScrcpyServer"/>). We never invoke the manager in these tests
/// — they cover the manager's dictionary/lifecycle plumbing only.
/// </summary>
internal sealed class FakeAdbServerManager : IAdbServerManager
{
    public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(true);

    public Task<IReadOnlyList<DeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<DeviceDescriptor>>(Array.Empty<DeviceDescriptor>());

    public Task EnableNetworkTcpipAsync(int port = 5555, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ConnectWirelessAsync(string serial, string host, int port = 5555, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<string> ExecuteShellCommandAsync(string serial, string command, CancellationToken cancellationToken = default)
        => Task.FromResult(string.Empty);
}

/// <summary>
/// Returns a <see cref="FakeScrcpyVideoDecoder"/> on every
/// <see cref="IScrcpyVideoDecoderFactory.Create"/> call. Lets unit tests
/// exercise the session lifecycle without ever loading FFmpeg.
/// </summary>
internal sealed class FakeDecoderFactory : IScrcpyVideoDecoderFactory
{
    public IScrcpyVideoDecoder Create() => new FakeScrcpyVideoDecoder();
}

/// <summary>
/// <see cref="ScrcpySession"/> subclass that overrides the ADB/network
/// plumbing so we can test <see cref="ScrcpySessionManager"/>'s
/// dictionary bookkeeping without ever touching the real server.
/// </summary>
internal sealed class FakeScrcpySession : ScrcpySession
{
    public FakeScrcpySession(string serial)
        : base(new FakeAdbServerManager(), serial, new FakeDecoderFactory())
    {
    }

    /// <summary>
    /// Override <see cref="ScrcpySession.StartAsync"/> to a no-op so the
    /// manager's bookkeeping (Add / Remove from
    /// <see cref="ScrcpySessionManager.Sessions"/>, raising
    /// <see cref="ScrcpySessionManager.SessionsChanged"/>) is the only
    /// thing under test. The base method is marked <c>virtual</c> so the
    /// dispatcher binding in <see cref="ScrcpySessionManager"/> picks
    /// this override at runtime.
    /// </summary>
    public override Task StartAsync(CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
