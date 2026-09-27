using FluentAssertions;
using Riders.Mirroring.Core.Scrcpy;
using Riders.Mirroring.Desktop.ViewModels;
using Xunit;

namespace Riders.Mirroring.Desktop.Tests.ViewModels;

public class ScrcpySessionViewModelTests
{
    [Fact]
    public void Constructor_ExposesInitialState()
    {
        var session = new FakeScrcpySession("SERIAL_A");
        using var sut = new ScrcpySessionViewModel(session);

        sut.DeviceSerial.Should().Be("SERIAL_A");
        sut.StatusLabel.Should().Be("Idle");
        sut.CurrentFrame.Should().BeNull();
        sut.CurrentFps.Should().Be(0d);
    }

    [Fact]
    public void Dispose_IsIdempotent_AndDoesNotThrow()
    {
        var session = new FakeScrcpySession("SERIAL_C");
        var sut = new ScrcpySessionViewModel(session);

        sut.Dispose();
        sut.Invoking(s => s.Dispose()).Should().NotThrow();
    }

    [Fact]
    public void StartAsync_DelegatesToUnderlyingSession()
    {
        var session = new FakeScrcpySession("SERIAL_D");
        using var sut = new ScrcpySessionViewModel(session);

        sut.StartAsync().GetAwaiter().GetResult();

        session.StartCallCount.Should().Be(1);
    }

    private sealed class FakeScrcpySession : ScrcpySession
    {
        public FakeScrcpySession(string serial)
            : base(new NoopAdbServerManager(), serial, new NoopDecoderFactory())
        {
        }

        public int StartCallCount { get; private set; }

        // StartAsync is virtual — override to avoid touching ADB / jar in unit tests.
        public override Task StartAsync(CancellationToken cancellationToken = default)
        {
            StartCallCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class NoopAdbServerManager : Riders.Mirroring.Core.Adb.IAdbServerManager
    {
        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(true);
        public Task<IReadOnlyList<Riders.Mirroring.Core.Adb.DeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Riders.Mirroring.Core.Adb.DeviceDescriptor>>(Array.Empty<Riders.Mirroring.Core.Adb.DeviceDescriptor>());
        public Task EnableNetworkTcpipAsync(int port = 5555, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task ConnectWirelessAsync(string serial, string host, int port = 5555, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task<string> ExecuteShellCommandAsync(string serial, string command, CancellationToken cancellationToken = default)
            => Task.FromResult(string.Empty);
    }

    private sealed class NoopDecoderFactory : IScrcpyVideoDecoderFactory
    {
        public IScrcpyVideoDecoder Create() => new NoopDecoder();
    }

    private sealed class NoopDecoder : IScrcpyVideoDecoder
    {
        public bool IsReady => false;
        public ScrcpyStreamHeader? Header => null;
        public event EventHandler<DecodedVideoFrame>? FrameDecoded
        {
            add { }
            remove { }
        }
        public void Prime(ScrcpyStreamHeader header) { }
        public Task PushPacketAsync(ReadOnlyMemory<byte> packet, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public Task FlushAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
