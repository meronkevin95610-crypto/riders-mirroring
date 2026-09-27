using FluentAssertions;
using Riders.Mirroring.Core.Wireless;
using Xunit;

namespace Riders.Mirroring.Core.Tests.Wireless;

public class WirelessQrEncoderTests
{
    [Fact]
    public void EncodePng_ReturnsNonEmptyPng()
    {
        var encoder = new WirelessQrEncoder();
        var png = encoder.EncodePng("WIFI:T:WPA;S:test;P:test;H:false;;");

        png.Should().NotBeEmpty();
        // PNG magic bytes: 89 50 4E 47 0D 0A 1A 0A
        png[0].Should().Be(0x89);
        png[1].Should().Be(0x50);
        png[2].Should().Be(0x4E);
        png[3].Should().Be(0x47);
    }

    [Fact]
    public void EncodePng_ThrowsOnEmptyPayload()
    {
        var encoder = new WirelessQrEncoder();

        var act = () => encoder.EncodePng(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_RejectsTinyPixelsPerModule()
    {
        var act = () => new WirelessQrEncoder(pixelsPerModule: 1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void EncodePng_ScalesWithPixelsPerModule()
    {
        var small = new WirelessQrEncoder(pixelsPerModule: 4).EncodePng("hello");
        var big   = new WirelessQrEncoder(pixelsPerModule: 40).EncodePng("hello");

        big.Length.Should().BeGreaterThan(small.Length);
    }
}