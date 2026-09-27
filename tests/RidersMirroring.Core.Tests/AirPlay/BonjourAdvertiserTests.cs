using System.Text;
using FluentAssertions;
using Riders.Mirroring.Core.AirPlay.Bonjour;

namespace RidersMirroring.Core.Tests.AirPlay;

public class BonjourAdvertiserTests
{
    [Fact]
    public void EncodeDnsName_EncodesEachLabelWithLength()
    {
        var encoded = BonjourAdvertiser.EncodeDnsName("foo.example.com");
        encoded[0].Should().Be(3);
        encoded[1].Should().Be((byte)'f');
        encoded[2].Should().Be((byte)'o');
        encoded[3].Should().Be((byte)'o');
        encoded[4].Should().Be(7);
        encoded[5].Should().Be((byte)'e');
    }

    [Fact]
    public void EncodeDnsName_HandlesTrailingDot()
    {
        var encoded = BonjourAdvertiser.EncodeDnsName("_airplay._tcp.local.");
        encoded[^1].Should().Be(0); // terminator
    }

    [Fact]
    public void EncodeDnsName_ThrowsOnLabelOver63Bytes()
    {
        var longLabel = new string('x', 64);
        Action act = () => BonjourAdvertiser.EncodeDnsName(longLabel + ".local");
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ParseMac_ReturnsSixBytes()
    {
        var bytes = BonjourAdvertiser.ParseMac("AA:BB:CC:DD:EE:FF");
        bytes.Should().HaveCount(6);
        bytes[0].Should().Be(0xAA);
        bytes[5].Should().Be(0xFF);
    }

    [Fact]
    public void GenerateSyntheticMac_IsLocallyAdministered()
    {
        var mac = BonjourAdvertiser.GenerateSyntheticMac();
        var bytes = BonjourAdvertiser.ParseMac(mac);
        bytes[0].Should().Be(0x02); // locally administered bit set
        bytes.Should().HaveCount(6);
    }

    [Fact]
    public void BuildAnnouncementPacketRaw_HasFourAnswers()
    {
        var packet = InvokeRawBuild("Riders Mirroring", 7000, "RidersMirroring", "02:00:00:00:00:01");
        packet.Length.Should().BeGreaterThan(50);
        // DNS header ANCOUNT is big-endian at bytes 6-7
        var anCount = (packet[6] << 8) | packet[7];
        anCount.Should().Be(4);
    }

    private static byte[] InvokeRawBuild(string name, int port, string model, string mac)
    {
        var method = typeof(BonjourAdvertiser).GetMethod(
            "BuildAnnouncementPacketRaw",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        return (byte[])method!.Invoke(null, new object[] { name, port, model, mac, 4500 })!;
    }
}