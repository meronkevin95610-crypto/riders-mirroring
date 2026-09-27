# Changelog

All notable changes to **Riders Mirroring** will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Sprint 3 — real-device integration tests (suite)

- `ScrcpyServer.StartAsync` end-to-end test against the lab ASUS ZenFone X00TD:
  - Push of `scrcpy-server.jar` (69 KB, v2.4) onto `/data/local/tmp/` verified via `adb shell ls`
  - Reverse tunnel `tcp:27183` registered, verified via `adb reverse --list`
  - `DisposeAsync` cleans up the tunnel
- Vendor: downloaded `scrcpy-server.jar` v2.4 official Genymobile build to `vendor/scrcpy-server/`
- New tests in `ScrcpyServerIntegrationTests`: 6 total (3 path resolution + 1 end-to-end + 2 negative cases)
- Lab results: **13/13 integration tests passed in 7 s** against the real device

### Sprint 3 — real-device integration tests (initial)

- New project `tests/RidersMirroring.IntegrationTests/` (10 tests) targeting a real connected Android device via ADB
  - `AdbServerManagerIntegrationTests` (5): probe `adb.exe` discovery, `EnsureStartedAsync` non-throwing, `GetDevicesAsync` returns Online device, enrichment populates `Model` + `AndroidVersion` + `Sdk`, transport detected as USB
  - `AdbDeviceWatcherIntegrationTests` (2): watcher fires first snapshot < 8 s, `StopAsync` is reentrant without throwing
  - `ScrcpyServerIntegrationTests` (3): `LocateServerJar` honours valid override, falls through to AppContext / source-tree candidate, never throws on a missing override
- All tests use `Xunit.SkippableFact` and probe `adb.exe` from PATH or Scoop fallback (`%USERPROFILE%\scoop\apps\adb\current\platform-tools\adb.exe`)
- Result on the lab ASUS ZenFone X00TD (Android 9, SDK 28, USB): **10/10 passed in 7 s**, 0 skipped
- Verified end-to-end: `adb devices` lists the phone, `AdbServerManager` enriches it with model/Android version/SDK, `DeviceDescriptor.DisplayName` renders as "ASUS_X00TD — USB"

### Sprint 2 — polish & quick wins

- Performance: cap `MainViewModel.LogLines` to 500 entries (FIFO) to prevent unbounded memory growth during long sessions
- Performance: enable UI virtualization on both `ListBox` controls (sidebar device list + live-log strip) — smooth at 100+ devices
- Security: redact SSIDs in logs via `WirelessHostService.Tail()` (last 4 chars or `****` if shorter)
- UX: catch `QRCoder.Exceptions.DataTooLongException` in `WirelessHostViewModel` with a friendly status message
- Reliability: `ThemeService.Persist()` now logs a warning instead of silently swallowing I/O errors
- Build: regenerated `App.ico` with 6 resolutions (16/32/48/64/128/256) for Windows 11 HiDPI
- Quality: `.editorconfig` now configures CA1062/CA1051 as warnings and CA1822/CA1848/CA2007/CA1812 as suggestions
- Documentation: added `README.md` in `Core/Scrcpy`, `Core/AirPlay`, `Core/Wireless`, `Core/Usb` covering wire formats, exception contracts, and exit codes
- Tests: added `ScrcpyStreamHeaderTests` (codec matrix), `ScrcpyStreamParser` edge cases (null stream, nameLen=2000, dimensions=10000, empty name/codec), `AirPlaySessionTests` (DisplayName fallback, Id stability), `WirelessHostServiceTests.Tail` (PII redaction)

## [Unreleased]

### Added

- Step 1 — Bootstrap
  - `RidersMirroring.slnx` (XML solution) with `src/`, `tests/`, `installer/` folders
  - `global.json` pinning the SDK to .NET 8 line (allows 10.0 preview via `latestMajor`)
  - `Directory.Build.props` centralising product metadata, language version, determinism
  - `.editorconfig` with C# + XAML style rules (`dotnet_sort_system_directives_first`, brace style, IDE/CA rules)
- Step 2 — WPF Shell + Theming
  - `MainWindow` 3-zone layout (sidebar 280px, centre pane, log strip 180px)
  - `ThemeService` (live switch dark/light + accent preset, persists to `%LOCALAPPDATA%`)
  - 4 accent presets (VioletPink, Ocean, Forest, Sunset) via `ThemePreset.All`
  - 6 XAML converters (`BoolToVisibility`, `NullOrEmptyToVisibility`, `InverseBool`, `HexToBrush`, `StringToVisibility`, `AppViewLabelConverter`)
- Step 3 — ADB + scrcpy
  - `AdbServerManager` with 12-attempt retry loop (500ms) and `getprop` enrichment
  - `AdbDeviceWatcher` polling 2s with snapshot diff
  - `DeviceDescriptor` record (serial, state, model, Android version)
  - `ScrcpyOptions` record + `--flag=value` argument serialiser
  - `ScrcpyServer` (push jar, reverse tunnel, spawn `app_process`)
- Step 4 — AirPlay
  - `IAirPlayReceiver` + `AirPlaySession` + `AirPlayReceiverProcess` (UxPlay wrapper)
  - `UxPlayLogParser` (handles both Chromium-style and plain UxPlay log lines)
  - `AirPlayView` + `AirPlayViewModel` (Start/Stop, exit banner, live sessions list)
- Step 5 — Wireless Host + USB
  - `WirelessPairingInfo` (WIFI:T:WPA payload with full backslash escape)
  - `WirelessQrEncoder` (PNG via QRCoder)
  - `WirelessHostService` (local IPv4 resolution, SSID/PWD QR builder)
  - `WirelessHostView` (SSID/Password inputs + 220×220 QR card)
  - `UsbConfigurationSwitch` (P/Invoke stub for setup API, returns `NotSupported` for now)
- Step 6 — Logging
  - `RidersLogger` (Serilog JSON sink, daily rotation, 14-day retention, factory thread-safe)
- Step 7 — MSI + CI
  - `installer/RidersMirroring.Installer.wixproj` (WiX 4.0.5 SDK)
  - `Harvest.ps1` — auto-generates `obj\HarvestedFiles.wxs` (replaces removed `wix heat`)
  - `.github/workflows/ci.yml` — `test` (Core+Desktop) and `msi` jobs on `windows-latest`
- Step 8 — scrcpy video pipeline (abstraction)
  - `ScrcpyStreamHeader` record + `ScrcpyStreamParser` (parses scrcpy 1.x header: name + codec + w/h)
  - `IScrcpyVideoDecoder` + `DecodedVideoFrame` (Backend-agnostic, FFmpeg-ready)
  - `ScrcpyVideoStream` async reader loop (frame NAL packets with 4-byte BE length prefix)
  - `FakeScrcpyVideoDecoder` test double with configurable `FrameFactory`

### Tests

- Core: 52 tests across 10 files (`AdbDeviceWatcher`, `DeviceDescriptor`, `RidersLogger`,
  `ScrcpyOptions`, `UxPlayLogParser`, `WirelessHostService`, `WirelessPairingInfo`,
  `WirelessQrEncoder`, `ScrcpyStreamParser`, `ScrcpyVideoStream`, `FakeScrcpyVideoDecoder`)
- Desktop: 22 tests across 3 files (`AppViewLabels`, `AppViewLabelConverter`,
  `StringToVisibilityConverter`)

### Documentation

- `README.md` — build, test, project layout, GPL note for UxPlay aggregation
- `vendor/README.md` — where to drop third-party binaries (scrcpy, FFmpeg, UxPlay)
- `licenses/README.md` — license texts overview table
- This `CHANGELOG.md`

### Not yet implemented

- FFmpeg-backed implementation of `IScrcpyVideoDecoder` (pipeline is wired but the actual
  decoder drops into the next milestone once FFmpeg DLLs land in `vendor/ffmpeg/`)
- Auto-update channel (Velopack / Squirrel) to replace the manual MSI flow
- Localised `.resx` resources (FR/EN/ES)
- `AboutViewModel` (currently `AboutView.xaml` is static)

## [0.0.0] — Bootstrap

Initial empty repository.
