using FluentAssertions;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.Scrcpy;
using Riders.Mirroring.Desktop.ViewModels;
using Xunit;

namespace Riders.Mirroring.Desktop.Tests.ViewModels;

public class HubViewModelTests
{
    [Fact]
    public void Constructor_HasNoTabs_AndHasTabsIsFalse()
    {
        using var sut = new HubViewModel();

        sut.Tabs.Should().BeEmpty();
        sut.SelectedTab.Should().BeNull();
        sut.HasTabs.Should().BeFalse();
    }

    [Fact]
    public void OpenTab_AddsTab_AndSelectsIt()
    {
        using var sut = new HubViewModel();
        var session = new FakeScrcpySession("SERIAL_A");

        var tab = sut.OpenTab(session);

        sut.Tabs.Should().HaveCount(1);
        sut.Tabs[0].Should().BeSameAs(tab);
        sut.SelectedTab.Should().BeSameAs(tab);
        sut.HasTabs.Should().BeTrue();
        tab.IsActive.Should().BeTrue();
    }

    [Fact]
    public void OpenTab_TwiceForSameSerial_ReturnsExistingTab()
    {
        using var sut = new HubViewModel();
        var first = sut.OpenTab(new FakeScrcpySession("SERIAL_A"));

        // Second open with the SAME serial should re-use the first tab.
        var second = sut.OpenTab(new FakeScrcpySession("SERIAL_A"));

        second.Should().BeSameAs(first);
        sut.Tabs.Should().HaveCount(1);
    }

    [Fact]
    public void OpenTab_DifferentSerials_CreatesIndependentTabs()
    {
        using var sut = new HubViewModel();

        sut.OpenTab(new FakeScrcpySession("SERIAL_A"));
        sut.OpenTab(new FakeScrcpySession("SERIAL_B"));
        sut.OpenTab(new FakeScrcpySession("SERIAL_C"));

        sut.Tabs.Should().HaveCount(3);
        sut.SelectedTab!.Serial.Should().Be("SERIAL_C");
    }

    [Fact]
    public void CloseTab_RemovesTab_AndSelectsNeighbour()
    {
        using var sut = new HubViewModel();
        sut.OpenTab(new FakeScrcpySession("SERIAL_A"));
        sut.OpenTab(new FakeScrcpySession("SERIAL_B"));
        sut.OpenTab(new FakeScrcpySession("SERIAL_C"));

        // Selected = C (last opened). Closing it should select B (the neighbour).
        sut.CloseTab(sut.SelectedTab!);

        sut.Tabs.Should().HaveCount(2);
        sut.SelectedTab!.Serial.Should().Be("SERIAL_B");
    }

    [Fact]
    public void CloseTab_UnknownSerial_IsNoOp()
    {
        using var sut = new HubViewModel();
        sut.OpenTab(new FakeScrcpySession("SERIAL_A"));

        sut.CloseTab("DOES_NOT_EXIST");

        sut.Tabs.Should().HaveCount(1);
    }

    [Fact]
    public void CloseAllTabs_SelectedTabBecomesNull()
    {
        using var sut = new HubViewModel();
        sut.OpenTab(new FakeScrcpySession("SERIAL_A"));

        sut.CloseTab(sut.SelectedTab!);

        sut.Tabs.Should().BeEmpty();
        sut.SelectedTab.Should().BeNull();
        sut.HasTabs.Should().BeFalse();
    }

    [Fact]
    public void Dispose_RemovesAllTabs()
    {
        var sut = new HubViewModel();
        sut.OpenTab(new FakeScrcpySession("SERIAL_A"));
        sut.OpenTab(new FakeScrcpySession("SERIAL_B"));

        sut.Dispose();

        sut.Tabs.Should().BeEmpty();
    }

    [Fact]
    public void SelectingTab_UpdatesIsActiveFlagOnAllTabs()
    {
        using var sut = new HubViewModel();
        sut.OpenTab(new FakeScrcpySession("SERIAL_A"));
        sut.OpenTab(new FakeScrcpySession("SERIAL_B"));

        // Initially: only B (last opened) is active.
        sut.Tabs[0].IsActive.Should().BeFalse();
        sut.Tabs[1].IsActive.Should().BeTrue();

        // Selecting A flips the flags.
        sut.SelectedTab = sut.Tabs[0];
        sut.Tabs[0].IsActive.Should().BeTrue();
        sut.Tabs[1].IsActive.Should().BeFalse();
    }

    private sealed class FakeScrcpySession : ScrcpySession
    {
        public FakeScrcpySession(string serial)
            : base(new NoopAdbServerManager(), serial, new NoopDecoderFactory())
        {
        }

        public override Task StartAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class NoopAdbServerManager : IAdbServerManager
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
