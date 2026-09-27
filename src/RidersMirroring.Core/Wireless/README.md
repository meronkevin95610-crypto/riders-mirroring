# Riders Mirroring — Wireless host

Powers the "Wireless Host" pane: assembles the Wi-Fi pairing QR code
and ensures the local ADB server is listening on port 5555.

## QR payload (zxing `WIFI:` URI)

```
WIFI:T:WPA;S:<ssid>;P:<password>;H:false;;
```

- `T:WPA` — auth type. `nopass` is also valid but not currently emitted.
- `S:` — SSID, **backslash, semicolon, comma, colon, and double quote
  must be escaped** with a leading backslash.
- `P:` — WPA password.
- `H:false` — hidden SSID flag. We don't expose hidden networks today.

[`WirelessPairingInfo.cs`](WirelessPairingInfo.cs) builds the payload;
[`WirelessQrEncoder.cs`](WirelessQrEncoder.cs) turns it into a PNG.

## ADB port

Default 5555 — see `WirelessHostService.DefaultAdbPort`. The constant
is shared with `AdbServerManager` so both ends agree.

## SSID redaction in logs

Full SSIDs can leak the user's location ("CafeDuCoin_5GHz", "iPhone de
Bob"). [`WirelessHostService.cs`](WirelessHostService.cs) logs only
the last 4 characters via the `Tail` helper, or `****` when the SSID
is 4 characters or fewer.
