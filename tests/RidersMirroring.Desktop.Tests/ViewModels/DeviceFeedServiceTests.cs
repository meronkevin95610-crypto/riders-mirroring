using System.Collections.ObjectModel;
using System.Collections.Specialized;
using FluentAssertions;
using Riders.Mirroring.Core.Adb;
using Riders.Mirroring.Desktop.ViewModels;

namespace RidersMirroring.Desktop.Tests.ViewModels;

/// <summary>
/// Unit tests for <see cref="DeviceFeedService.DeviceListItemDiff"/> and the
/// surrounding <see cref="DeviceListItem"/> projector. These run without a
/// WPF dispatcher because the diff is a pure function of
/// <see cref="ObservableCollection{T}"/> and the snapshot list.
/// </summary>
public sealed class DeviceFeedServiceTests
{
    private static DeviceDescriptor MakeUsb(string serial, string state = "device") =>
        new(serial, state, "Pixel 8", "Google", "14", 34, DeviceTransport.Usb);

    private static DeviceDescriptor MakeWifi(string serial, string state = "online") =>
        new(serial, state, "Galaxy S24", "Samsung", "14", 34, DeviceTransport.Network);

    public sealed class DiffApply
    {
        [Fact]
        public void Empty_snapshot_into_empty_target_leaves_empty()
        {
            var target = new ObservableCollection<DeviceListItem>();

            DeviceFeedService.DeviceListItemDiff.Apply(target, Array.Empty<DeviceDescriptor>(), DeviceListItem.FromDescriptor);

            target.Should().BeEmpty();
        }

        [Fact]
        public void Non_empty_snapshot_into_empty_target_appends_in_order()
        {
            var target = new ObservableCollection<DeviceListItem>();
            var snapshot = new[] { MakeUsb("S1"), MakeWifi("S2"), MakeUsb("S3") };
            var changes = Subscribe(target);

            DeviceFeedService.DeviceListItemDiff.Apply(target, snapshot, DeviceListItem.FromDescriptor);

            target.Select(i => i.Serial).Should().Equal("S1", "S2", "S3");
            changes.OfType<NotifyCollectionChangedEventArgs>().Should()
                .OnlyContain(c => c.Action == NotifyCollectionChangedAction.Add);
        }

        [Fact]
        public void Identical_second_snapshot_emits_no_events()
        {
            var snapshot = new[] { MakeUsb("S1"), MakeWifi("S2") };
            var target = new ObservableCollection<DeviceListItem>();
            DeviceFeedService.DeviceListItemDiff.Apply(target, snapshot, DeviceListItem.FromDescriptor);

            var changes = Subscribe(target);
            DeviceFeedService.DeviceListItemDiff.Apply(target, snapshot, DeviceListItem.FromDescriptor);

            changes.Should().BeEmpty("unchanged snapshots should not mutate the collection");
        }

        [Fact]
        public void Removed_device_is_dropped_from_collection()
        {
            var first = new[] { MakeUsb("S1"), MakeWifi("S2"), MakeUsb("S3") };
            var second = new[] { MakeUsb("S1"), MakeUsb("S3") };
            var target = new ObservableCollection<DeviceListItem>();
            DeviceFeedService.DeviceListItemDiff.Apply(target, first, DeviceListItem.FromDescriptor);

            var changes = Subscribe(target);
            DeviceFeedService.DeviceListItemDiff.Apply(target, second, DeviceListItem.FromDescriptor);

            target.Select(i => i.Serial).Should().Equal("S1", "S3");
            changes.Should().ContainSingle(c => c.Action == NotifyCollectionChangedAction.Remove);
        }

        [Fact]
        public void Added_device_is_appended_after_kept_items()
        {
            var first = new[] { MakeUsb("S1"), MakeUsb("S3") };
            var second = new[] { MakeUsb("S1"), MakeWifi("S2"), MakeUsb("S3") };
            var target = new ObservableCollection<DeviceListItem>();
            DeviceFeedService.DeviceListItemDiff.Apply(target, first, DeviceListItem.FromDescriptor);

            var changes = Subscribe(target);
            DeviceFeedService.DeviceListItemDiff.Apply(target, second, DeviceListItem.FromDescriptor);

            target.Select(i => i.Serial).Should().Equal("S1", "S2", "S3");
            changes.Should().ContainSingle(c => c.Action == NotifyCollectionChangedAction.Add);
        }

        [Fact]
        public void Reordered_snapshot_moves_existing_items_without_replacing_them()
        {
            // ADB can re-order serials between polls. The diff should Move items
            // instead of Replace-ing them, so the WPF item containers are reused
            // and selection/scroll state isn't lost.
            var first = new[] { MakeUsb("S1"), MakeWifi("S2"), MakeUsb("S3") };
            var second = new[] { MakeUsb("S3"), MakeUsb("S1"), MakeWifi("S2") };
            var target = new ObservableCollection<DeviceListItem>();
            DeviceFeedService.DeviceListItemDiff.Apply(target, first, DeviceListItem.FromDescriptor);

            // Capture references before re-applying — same instances must survive the Move.
            var s1Ref = target.Single(i => i.Serial == "S1");
            var s2Ref = target.Single(i => i.Serial == "S2");
            var s3Ref = target.Single(i => i.Serial == "S3");

            var changes = Subscribe(target);
            DeviceFeedService.DeviceListItemDiff.Apply(target, second, DeviceListItem.FromDescriptor);

            target.Select(i => i.Serial).Should().Equal("S3", "S1", "S2");
            target[0].Should().BeSameAs(s3Ref, "S3 should be the original instance (Move, not Replace)");
            target[1].Should().BeSameAs(s1Ref);
            target[2].Should().BeSameAs(s2Ref);
            changes.Should().OnlyContain(c => c.Action == NotifyCollectionChangedAction.Move,
                "no field changed so the items must not be replaced");
        }

        [Fact]
        public void Field_change_on_existing_device_triggers_Replace_event()
        {
            var first = new[] { MakeUsb("S1") };
            var second = new[] { new DeviceDescriptor("S1", "offline", "Pixel 8", "Google", "14", 34, DeviceTransport.Usb) };
            var target = new ObservableCollection<DeviceListItem>();
            DeviceFeedService.DeviceListItemDiff.Apply(target, first, DeviceListItem.FromDescriptor);

            var changes = Subscribe(target);
            DeviceFeedService.DeviceListItemDiff.Apply(target, second, DeviceListItem.FromDescriptor);

            target.Single().StateLabel.Should().Be("offline");
            changes.Should().ContainSingle(c => c.Action == NotifyCollectionChangedAction.Replace);
        }

        [Fact]
        public void Duplicate_serials_in_snapshot_are_deduplicated()
        {
            // `adb devices -l` shouldn't return duplicates, but defensive coding
            // pays off if a buggy ADB build ever does.
            var snapshot = new[] { MakeUsb("S1"), MakeUsb("S1"), MakeWifi("S2") };
            var target = new ObservableCollection<DeviceListItem>();

            DeviceFeedService.DeviceListItemDiff.Apply(target, snapshot, DeviceListItem.FromDescriptor);

            target.Select(i => i.Serial).Should().Equal("S1", "S2");
        }

        [Fact]
        public void Null_arguments_throw()
        {
            var target = new ObservableCollection<DeviceListItem>();
            var snapshot = Array.Empty<DeviceDescriptor>();

            FluentActions.Invoking(() => DeviceFeedService.DeviceListItemDiff.Apply(null!, snapshot, DeviceListItem.FromDescriptor))
                .Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => DeviceFeedService.DeviceListItemDiff.Apply(target, null!, DeviceListItem.FromDescriptor))
                .Should().Throw<ArgumentNullException>();
            FluentActions.Invoking(() => DeviceFeedService.DeviceListItemDiff.Apply(target, snapshot, null!))
                .Should().Throw<ArgumentNullException>();
        }
    }

    public sealed class DeviceListItemFromDescriptor
    {
        [Fact]
        public void Usb_transport_maps_to_USB_kind_label()
        {
            var item = DeviceListItem.FromDescriptor(MakeUsb("S1"));

            item.KindLabel.Should().Be("USB");
            item.Transport.Should().Be(DeviceTransport.Usb);
        }

        [Fact]
        public void Network_transport_maps_to_WiFi_kind_label()
        {
            var item = DeviceListItem.FromDescriptor(MakeWifi("S1"));

            item.KindLabel.Should().Be("Wi-Fi");
            item.Transport.Should().Be(DeviceTransport.Network);
        }

        [Theory]
        [InlineData("online", DeviceState.Connected)]
        [InlineData("device", DeviceState.Connected)]
        [InlineData("offline", DeviceState.Idle)]
        [InlineData("unauthorized", DeviceState.Idle)]
        [InlineData("", DeviceState.Idle)]
        public void State_strings_are_parsed_case_insensitively(string raw, DeviceState expected)
        {
            var descriptor = new DeviceDescriptor("S1", raw, null, null, null, null, DeviceTransport.Usb);

            var item = DeviceListItem.FromDescriptor(descriptor);

            item.State.Should().Be(expected);
            item.StateLabel.Should().Be(raw);
        }

        [Fact]
        public void Null_descriptor_throws()
        {
            FluentActions.Invoking(() => DeviceListItem.FromDescriptor(null!))
                .Should().Throw<ArgumentNullException>();
        }
    }

    private static IReadOnlyList<NotifyCollectionChangedEventArgs> Subscribe(
        ObservableCollection<DeviceListItem> target)
    {
        var bag = new List<NotifyCollectionChangedEventArgs>();
        target.CollectionChanged += (_, e) => bag.Add(e);
        return bag;
    }
}