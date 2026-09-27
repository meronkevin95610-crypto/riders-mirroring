using FluentAssertions;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Core.Scrcpy;
using Riders.Mirroring.Desktop.ViewModels;
using Xunit;

namespace Riders.Mirroring.Desktop.Tests.ViewModels;

public class MirrorViewModelTests
{
    private readonly FakeScrcpyMirrorService _fakeMirror = new();
    private readonly FakeAdbServer _fakeAdb = new();

    private MirrorViewModel CreateSut() => new(_fakeMirror, _fakeAdb);

    [Fact]
    public void InitialState_HasNoDevice_And_NotRunning()
    {
        var vm = CreateSut();

        vm.HasDevice.Should().BeFalse();
        vm.IsRunning.Should().BeFalse();
        vm.SelectedDevice.Should().BeNull();
        vm.StartMirrorCommand.CanExecute(null).Should().BeFalse();
        vm.StopMirrorCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public void SettingSelectedDevice_EnablesStartCommand_AndUpdatesStatus()
    {
        var vm = CreateSut();
        var device = new DeviceListItem
        {
            Serial = "TEST_SERIAL_1",
            DisplayName = "Pixel 8 — USB",
            KindLabel = "USB",
            StateLabel = "online",
            State = DeviceState.Connected,
            Transport = DeviceTransport.Usb,
        };

        vm.SelectedDevice = device;

        vm.HasDevice.Should().BeTrue();
        vm.StartMirrorCommand.CanExecute(null).Should().BeTrue();
        vm.StatusMessage.Should().Contain("Pixel 8");
    }

    [Fact]
    public async Task StartMirrorAsync_InvokesService_AndUpdatesRunningState()
    {
        var vm = CreateSut();
        vm.SelectedDevice = new DeviceListItem
        {
            Serial = "DEV123",
            DisplayName = "Galaxy S23 — USB",
            KindLabel = "USB",
            StateLabel = "online",
            State = DeviceState.Connected,
            Transport = DeviceTransport.Usb,
        };

        await vm.StartMirrorAsync();

        vm.IsRunning.Should().BeTrue();
        _fakeMirror.IsRunning.Should().BeTrue();
        _fakeMirror.CurrentDeviceSerial.Should().Be("DEV123");
        vm.StopMirrorCommand.CanExecute(null).Should().BeTrue();
        vm.StartMirrorCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task StopMirrorAsync_StopsService_AndUpdatesRunningState()
    {
        var vm = CreateSut();
        vm.SelectedDevice = new DeviceListItem
        {
            Serial = "DEV123",
            DisplayName = "Device",
            KindLabel = "USB",
            StateLabel = "online",
            State = DeviceState.Connected,
            Transport = DeviceTransport.Usb,
        };

        await vm.StartMirrorAsync();
        await vm.StopMirrorAsync();

        vm.IsRunning.Should().BeFalse();
        _fakeMirror.IsRunning.Should().BeFalse();
        vm.StartMirrorCommand.CanExecute(null).Should().BeTrue();
        vm.StopMirrorCommand.CanExecute(null).Should().BeFalse();
    }

    [Fact]
    public async Task SendKeyAsync_ExecutesAdbCommand()
    {
        var vm = CreateSut();
        vm.SelectedDevice = new DeviceListItem
        {
            Serial = "PHONE_007",
            DisplayName = "Test Phone",
            KindLabel = "USB",
            StateLabel = "online",
            State = DeviceState.Connected,
            Transport = DeviceTransport.Usb,
        };

        await vm.SendKeyAsync("home");

        _fakeAdb.LastCommand.Should().Be("input keyevent 3");
        _fakeAdb.LastSerial.Should().Be("PHONE_007");
    }

    private sealed class FakeScrcpyMirrorService : IScrcpyMirrorService
    {
        public bool IsRunning { get; set; }
        public string? CurrentDeviceSerial { get; set; }
        public string? CurrentDeviceDisplayName { get; set; }

        public event EventHandler<string>? SessionStarted;
        public event EventHandler<MirrorExitEventArgs>? SessionExited;
        public event EventHandler<string>? LogOutputReceived;

        public string? LocateScrcpyBinary() => "scrcpy.exe";

        public Task StartAsync(string serial, string? deviceDisplayName = null, ScrcpyOptions? options = null, CancellationToken cancellationToken = default)
        {
            IsRunning = true;
            CurrentDeviceSerial = serial;
            CurrentDeviceDisplayName = deviceDisplayName;
            SessionStarted?.Invoke(this, serial);
            LogOutputReceived?.Invoke(this, "Session started");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            IsRunning = false;
            SessionExited?.Invoke(this, new MirrorExitEventArgs(0));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAdbServer : IAdbServerManager
    {
        public string? LastSerial { get; private set; }
        public string? LastCommand { get; private set; }

        public Task<bool> EnsureStartedAsync(CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<IReadOnlyList<DeviceDescriptor>> GetDevicesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<DeviceDescriptor>>(Array.Empty<DeviceDescriptor>());
        public Task EnableNetworkTcpipAsync(int port = 5555, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ConnectWirelessAsync(string serial, string host, int port = 5555, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string> ExecuteShellCommandAsync(string serial, string command, CancellationToken cancellationToken = default)
        {
            LastSerial = serial;
            LastCommand = command;
            return Task.FromResult(string.Empty);
        }
    }
}
