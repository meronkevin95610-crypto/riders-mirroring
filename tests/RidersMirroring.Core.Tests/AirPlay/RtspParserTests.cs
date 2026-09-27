using System.Text;
using FluentAssertions;
using Riders.Mirroring.Core.AirPlay.Rtsp;

namespace RidersMirroring.Core.Tests.AirPlay;

public class RtspParserTests
{
    [Fact]
    public void TryParseRequest_ParsesSimpleOptions()
    {
        var raw = "OPTIONS rtsp://192.168.1.1/ RTSP/1.0\r\nCSeq: 1\r\nUser-Agent: Test\r\n\r\n"u8.ToArray();
        var ok = RtspParser.TryParseRequest(raw, out var req, out var consumed);

        ok.Should().BeTrue();
        consumed.Should().Be(raw.Length);
        req.Method.Should().Be("OPTIONS");
        req.Headers.GetCSeq().Should().Be(1);
        req.Headers.GetHeader("User-Agent").Should().Be("Test");
    }

    [Fact]
    public void TryParseRequest_ParsesBody()
    {
        var body = "v=0\r\no=- 1 1 IN IP4 127.0.0.1\r\n";
        var raw = Encoding.ASCII.GetBytes($"SETUP rtsp://x RTSP/1.0\r\nCSeq: 2\r\nContent-Length: {body.Length}\r\n\r\n{body}");
        var ok = RtspParser.TryParseRequest(raw, out var req, out _);

        ok.Should().BeTrue();
        req.Method.Should().Be("SETUP");
        Encoding.ASCII.GetString(req.Body).Should().Contain("v=0");
    }

    [Fact]
    public void TryParseRequest_ReturnsFalseWhenIncomplete()
    {
        var raw = Encoding.ASCII.GetBytes("OPTIONS rtsp://x RTSP/1.0\r\nCSeq: 1\r\n");
        var ok = RtspParser.TryParseRequest(raw, out _, out _);
        ok.Should().BeFalse();
    }

    [Fact]
    public void BuildResponse_ContainsStatusAndCSeq()
    {
        var response = RtspParser.BuildResponse(200, "OK",
            new Dictionary<string, string> { ["CSeq"] = "42" });
        var text = Encoding.ASCII.GetString(response);
        text.Should().Contain("RTSP/1.0 200 OK");
        text.Should().Contain("CSeq: 42");
    }

    [Fact]
    public void BuildResponse_IncludesContentLengthWhenBodyPresent()
    {
        var body = new byte[] { 0x01, 0x02, 0x03 };
        var response = RtspParser.BuildResponse(200, "OK",
            new Dictionary<string, string> { ["CSeq"] = "1" }, body);
        Encoding.ASCII.GetString(response).Should().Contain("Content-Length: 3");
        response.Length.Should().BeGreaterThan(body.Length);
    }
}