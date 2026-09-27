# vendor/

This folder holds **third-party binaries** that Riders Mirroring shells out to
or embeds at runtime. They are intentionally **not committed** to the
repository (see root `.gitignore`); each sub-folder only carries a `.gitkeep`
plus, where applicable, the upstream `LICENSE` text under `licenses/`.

Drop the files below into the matching sub-folder before shipping a build.
The application will fall back to `PATH` lookups if the local copy is missing,
but pinning the versions here is strongly recommended for reproducibility.

## scrcpy-server/ — Android mirroring

- **File:** `scrcpy-server.jar`
- **Source:** <https://github.com/Genymobile/scrcpy/releases>
- **Recommended version:** scrcpy 2.x (matches `Riders.Mirroring.Core.Scrcpy` server flags)
- **Used by:** `src/RidersMirroring.Core/Scrcpy/ScrcpyServer.cs` (pushed onto the device)
- **Detection order** (see `ScrcpyServer.LocateServerJar`):
  1. `vendor/scrcpy-server/scrcpy-server.jar` (next to the executable)
  2. `%LOCALAPPDATA%\Riders Mirroring\vendor\scrcpy-server\`
  3. Caller-supplied override (tests)

## ffmpeg/ — video decoding (next milestone)

- **Files:** `avcodec-x_y.dll`, `avformat-x_y.dll`, `avutil-x_y.dll`,
  `swscale-x_y.dll`, `swresample-x_y.dll` (matching version suffix)
- **Source:** <https://www.gyan.dev/ffmpeg/builds/> (shared LGPL build) or
  <https://github.com/BtbN/FFmpeg-Builds/releases> (gpl-shared)
- **License:** LGPL 2.1+ — copy `licenses/ffmpeg-LICENSE.txt` next to the DLLs
- **Note:** currently unused. The video pipeline is wired through
  `IScrcpyVideoDecoder` but the FFmpeg-backed implementation ships in the next
  milestone. Drop the DLLs now to be ready.

## uxplay/ — AirPlay receiver

- **File:** `uxplay.exe` (+ companion `Bonjour` if you don't have Apple's mDNS
  responder installed system-wide)
- **Source:** <https://github.com/FDH2/UxPlay/releases>
- **License:** GPLv3 — copy `licenses/uxplay-LICENSE.txt` next to the binary.
  Riders Mirroring **aggregates** UxPlay without linking its source code, so
  the resulting work is still MIT-licensed.
- **Detection order** (see `AirPlayReceiverProcess.DefaultBinaryLocator`):
  1. `%LOCALAPPDATA%\Riders Mirroring\bin\uxplay\uxplay.exe`
  2. `vendor/uxplay/uxplay.exe` (next to the executable)
  3. `PATH` lookup (`where uxplay.exe` on Windows)

### Fallback shim: UxPlayStub

Building the real UxPlay on Windows requires MSYS2 + the GStreamer SDK
(40+ MB of native dependencies), which is way too much overhead for a CI
runner. To keep the AirPlay pipeline testable end-to-end without those
dependencies, the repo ships a small `tools/UxPlayStub/` console app that
emits the exact same stdout protocol that `UxPlayLogParser` consumes
(`Server initialized`, `VIDEO has been received from "<name>"`,
`disconnected <name>`, etc.).

Rebuild it any time with:
```
tools\UxPlayStub\build-and-copy.cmd
```

When the real `uxplay.exe` is dropped here it takes precedence over the
stub — the stub is detected by its assembly metadata, so a hand-installed
real UxPlay always wins.

## Updating versions

1. Download the new release from the upstream link above.
2. Verify the SHA-256 against the value published by the maintainers.
3. Drop the file into the matching sub-folder.
4. Bump the version in `src/RidersMirroring.Core/RidersMirroring.Core.csproj`
   (NuGet refs) if a managed package is involved.
5. Add a line under `Changed` in `CHANGELOG.md`.

## Why these binaries live outside the repo

- scrcpy-server.jar is ~70 KB but rebuilt by upstream every release
- FFmpeg shared DLLs total ~50 MB and would bloat every clone
- uxplay.exe ~500 KB and depends on user-specific Bonjour install
- Putting them in `vendor/` (gitignored) keeps the source tree small while
  still giving the app a deterministic discovery path.
