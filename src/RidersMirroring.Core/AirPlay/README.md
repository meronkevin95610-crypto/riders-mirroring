# Riders Mirroring — AirPlay transport

Wraps [UxPlay](https://github.com/FDH2/UxPlay), an open-source AirPlay
receiver, and turns its stdout stream into structured connection events
the desktop UI can bind to.

## Why a separate process?

UxPlay is a native binary (C++, GStreamer). Rather than embed it we
spawn it as a child process — easier to update, easier to sandbox, and
the GStreamer pipeline gives us video decode/audio routing "for free".

## UxPlay stdout (Chromium-style)

UxPlay emits lines that look like Chromium's network stack log:

```
[1740000000.123] CONNECTED 192.168.1.42:7000  Bob's iPhone
[1740000030.456] DISCONNECTED 192.168.1.42:7000
[1740000100.789] ERROR  no audio backend
```

The parser ([`UxPlayLogParser.cs`](UxPlayLogParser.cs)) extracts the
three event kinds via regex; everything else is ignored.

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Clean shutdown (SIGINT) |
| 1 | `uxplay.exe` not found, or failed to bind to port |
| 137 | SIGKILL — we asked for it |
| Other | UxPlay-internal error, see stderr |

## Files

| File | Purpose |
|---|---|
| [`IAirPlayReceiver.cs`](IAirPlayReceiver.cs) | Abstraction over the receiver for unit tests |
| [`AirPlayReceiverProcess.cs`](AirPlayReceiverProcess.cs) | Spawns `uxplay.exe`, routes stdout/stderr |
| [`UxPlayLogParser.cs`](UxPlayLogParser.cs) | Regex parser for connection events |
| [`AirPlaySession.cs`](AirPlaySession.cs) | One connected iPhone / iPad |

## Binary location

The Windows build lives in
[`../../../vendor/uxplay/`](../../../vendor/uxplay/) (see the README
there).
