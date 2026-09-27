# Riders Mirroring — Scrcpy transport

This folder contains everything needed to bridge an Android device's
screen to a local desktop window through the
[scrcpy](https://github.com/Genymobile/scrcpy) protocol.

## Wire format (scrcpy v3.x / v4.x)

The contract implemented by [`ScrcpyStreamParser.cs`](ScrcpyStreamParser.cs)
is derived from the canonical spec in
[`doc/develop.md` § "Protocol"](https://github.com/Genymobile/scrcpy/blob/master/doc/develop.md)
and from the Java reference implementation in
[`Streamer.java`](https://github.com/Genymobile/scrcpy/blob/master/server/src/main/java/com/genymobile/scrcpy/device/Streamer.java).

### Socket handshake (server → client, video and audio streams)

```
device_name (64 bytes, NUL-padded UTF-8)        // one packet, fixed width
codec_id     (4 bytes, big-endian ASCII)        // "h264" / "h265" / "av1" / "opus" / "aac" / "flac"
```

When the server detects a fatal stream error it sends codec-id
`0x00000001` ("stream disabled") and closes the socket — the client
should surface a user-friendly error and tear the session down.

### Session packet (12 bytes, big-endian) — emitted on capture-session change

```
byte 0..3   : flags (uint32)   — bit 0 = client_resized; bit 31 = session marker (legacy)
byte 4..7   : width  (uint32)
byte 8..11  : height (uint32)
```

### Media packet (12-byte header + payload) — one NAL access unit per packet

```
byte 0..7   : PTS | flags (uint64)
               bit 63 = PACKET_FLAG_SESSION   (1 ⇒ this packet is a session packet, not media)
               bit 62 = PACKET_FLAG_CONFIG    (codec-config packet, not a frame)
               bit 61 = PACKET_FLAG_KEY_FRAME
               bits 0-60 : PTS in microseconds
byte 8..11  : packet size (uint32, big-endian) — size of the codec payload that follows
byte 12..   : raw codec data
```

Reference: `SC_PACKET_HEADER_SIZE = 12` (server/demuxer). A packet with
`PACKET_FLAG_SESSION` set MUST NOT carry codec data — its payload is
the session-packet body described above.

> **Note**: Riders Mirroring's current `ScrcpyStreamParser` was originally
> written against the older scrcpy 1.x/2.x contract (length-prefixed
> device name, length-prefixed codec id, 4-byte LE length prefix per
> media packet). A v4 upgrade would replace the handshake with the
> fixed-width 64-byte device name + 4-byte BE codec id and switch the
> media header to the 12-byte BE PTS/flags layout shown above. The
> upgrade is tracked as a follow-up sprint item — the field-set stays
> the same (`CodecId`, `Width`, `Height`, plus PTS/flags on each packet).

## Exceptions raised by `TryReadHeader`

| Condition | Exception |
|---|---|
| `stream` is `null` | `ArgumentNullException("stream")` |
| Name length > 1024 | `InvalidDataException` ("exceeds sanity limit") |
| Codec length > 32 | `InvalidDataException` |
| Width or height ≤ 0 or > 8192 | `InvalidDataException` ("implausible dimensions") |
| Stream ended mid-header | Returns `false` — caller retries after more data |

The decoder abstraction lives in
[`IScrcpyVideoDecoder.cs`](IScrcpyVideoDecoder.cs). Concrete decoders
can be swapped in without touching the transport layer.

## CLI options passed to scrcpy-server

The full list of `--key=value` pairs the server understands is in
[`Options.java`](https://github.com/Genymobile/scrcpy/blob/master/server/src/main/java/com/genymobile/scrcpy/Options.java).
Riders Mirroring surfaces the most useful ones through
[`ScrcpyOptions.cs`](ScrcpyOptions.cs):

| Option | Default | Purpose |
|---|---|---|
| `--max-fps` | 60 | Target frame rate |
| `--max-size` | 0 (auto) | Largest viewport dimension |
| `--bit-rate` | 8M | H.264 bitrate in Mbps |
| `--video-codec` | (auto) | Force a specific encoder (e.g. `OMX.qcom.video.encoder.avc`) |
| `--show-touches` / `--stay-awake` / `--no-clipboard` / `--no-audio` | various | UI / UX toggles |

Server-version pinning is **mandatory**: the server throws if
`clientVersion` doesn't match its `BuildConfig.VERSION_NAME`. Riders
Mirroring's vendored jar (`vendor/scrcpy-server/scrcpy-server.jar`) is
locked to the version documented in `vendor/scrcpy-server/README.md`.

## Minimal usage

```csharp
using var socket = new TcpClient("127.0.0.1", localPort).GetStream();
if (!ScrcpyStreamParser.TryReadHeader(socket, out var header, out _))
{
    return; // need more bytes
}

IDecoder decoder = header!.CodecId switch
{
    "h264" => new H264Decoder(header.Width, header.Height),
    "h265" => new H265Decoder(header.Width, header.Height),
    "av1"  => new Av1Decoder(header.Width, header.Height),
    _      => throw new NotSupportedException(header.CodecId),
};

await decoder.PushPacketAsync(packet, ct);
```

## Reused upstream assets

Riders Mirroring does not bundle the upstream scrcpy logo — only the
`scrcpy-server.jar` is shipped, unmodified. Apache 2.0 attribution for
the upstream project itself is preserved in
[`licenses/scrcpy-NOTICE.txt`](../../../licenses/scrcpy-NOTICE.txt).

## Server bootstrap

[`ScrcpyServer.cs`](ScrcpyServer.cs) handles the ADB side: pushing the
prebuilt `scrcpy-server.jar`, opening the reverse tunnel, and spawning
the server process on-device. The binary lives in
[`../../../vendor/scrcpy-server/`](../../../vendor/scrcpy-server/)
(see the README there for download instructions).
