# licenses/

This folder holds the **license texts of third-party components** that
Riders Mirroring ships, depends on, or aggregates. Riders Mirroring itself is
licensed under MIT — see [`../LICENSE`](../LICENSE).

| Component | License | File | Used by |
|---|---|---|---|
| scrcpy | Apache 2.0 | `scrcpy-LICENSE.txt` | `scrcpy-server.jar` is pushed onto Android devices via `ScrcpyServer` |
| UxPlay | GPLv3 | `uxplay-LICENSE.txt` | Spawned as a separate process for AirPlay mirroring (aggregation, not linking) |
| FFmpeg | LGPL 2.1+ | `ffmpeg-LICENSE.txt` | Optional video-decoder backend (next milestone) |
| QRCoder | MIT | `QRCoder-LICENSE.txt` | NuGet dep used by `WirelessQrEncoder` |
| AdvancedSharpAdbClient | MIT | `AdvancedSharpAdbClient-LICENSE.txt` | NuGet dep used by `AdbServerManager` |
| Serilog | Apache 2.0 | `Serilog-LICENSE.txt` | NuGet dep used by `RidersLogger` |

## Notes

- **GPL aggregation**: UxPlay is GPLv3. We deliberately **only spawn it as a
  separate process** and never link its object files, so the MIT code of
  Riders Mirroring is not considered a "derivative work" of UxPlay. End users
  installing Riders Mirroring receive the UxPlay source / binary alongside the
  app under the GPLv3.
- **LGPL FFmpeg**: if/when we ship the FFmpeg decoder backend, we use the
  shared-library (DLL) build so users can swap the DLLs for a different
  version without recompiling Riders Mirroring. Static linking is avoided to
  preserve LGPL compliance.
- **No bundled binaries**: these texts are kept here for traceability. The
  actual binary files live under `vendor/` (see [`../vendor/README.md`](../vendor/README.md)).
