# Riders Mirroring — USB transport (stub)

The current build doesn't ship a native USB configuration switch.
[`StubUsbConfigurationSwitch.cs`](StubUsbConfigurationSwitch.cs) is
the placeholder that lets the rest of the codebase compile and the
unit tests run.

## Why a stub?

The real switcher needs a kernel-mode driver (libusb-win32 + a signed
`.cat`) to flip the device from MTP to ADB mode on Windows. That's
out of scope for the v1.0 milestone — users connect over TCP/IP via
the Wireless pane instead.

## Result codes

| Code | When |
|---|---|
| `NotSupported` | We're the stub. Use the Wireless Host pane instead. |
| `InvalidArguments` | Future: user passed a bad VID/PID pair. |
| `Failed` | Future: the driver call failed. Inspect logs. |

## Replacing the stub

To wire the real implementation, drop a new class implementing
`IUsbConfigurationSwitch`, register it in `AppServices`, and delete
`StubUsbConfigurationSwitch`. The interface surface is stable.
