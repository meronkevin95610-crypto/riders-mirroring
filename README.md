# Riders Mirroring

Desktop application for Windows that mirrors an **Android** device (USB or
Wi-Fi, via ADB + scrcpy-server) and an **iPhone** (via AirPlay, using UxPlay
as a redistributable external process).

> **Status:** early bootstrap — only the solution skeleton is in place.
> See the implementation plan at the bottom of this file for the roadmap.

---

## What this repo contains

| Folder | Purpose |
| --- | --- |
| `src/RidersMirroring.Core` | ADB / scrcpy / AirPlay / USB / Wireless Host / Logging logic (no UI). |
| `src/RidersMirroring.Desktop` | WPF UI (MVVM), theming, views, view-models. |
| `tests/RidersMirroring.Core.Tests` | xUnit + FluentAssertions tests for the core library. |
| `tests/RidersMirroring.Desktop.Tests` | xUnit + FluentAssertions tests for the WPF layer. |
| `installer/` | WiX Toolset sources (MSI). |
| `vendor/` | Redistributed third-party binaries (scrcpy-server, UxPlay, FFmpeg). |
| `licenses/` | Required licence texts for redistributed components. |

## Build

Requires the **.NET 8 SDK** (or any later SDK that can target
`net8.0-windows`) and the **Windows Desktop** runtime
(`Microsoft.WindowsDesktop.App 8.x`).

```powershell
dotnet build RidersMirroring.slnx
dotnet test  tests\RidersMirroring.Core.Tests\RidersMirroring.Core.Tests.csproj
dotnet test  tests\RidersMirroring.Desktop.Tests\RidersMirroring.Desktop.Tests.csproj
```

## Third-party components (see `licenses/`)

| Component | Licence | How it's used |
| --- | --- | --- |
| [scrcpy](https://github.com/Genymobile/scrcpy) | Apache 2.0 | `scrcpy-server` jar pushed to the Android device and run over ADB. |
| [AdvancedSharpAdbClient](https://github.com/SharpAdb/AdvancedSharpAdbClient) | MIT | Managed ADB client (no `adb.exe` spawn required). |
| [UxPlay](https://github.com/FDH2/UxPlay) | GPLv3 | Spawned as an external process; we do **not** link the library. |
| [FFmpeg](https://ffmpeg.org/) | LGPL 2.1 | H.264 decoding; redistributed as unmodified DLLs. |
| [QRCoder](https://github.com/codebude/QRCoder) | MIT | QR code rendering for wireless-adb pairing. |

> ⚠️ **GPL note:** UxPlay is launched as a **separate process** so the
> combination is an aggregation rather than a derivative work. We still ship
> UxPlay's source (or a link to it) and its full licence text in `licenses/`.
> If you plan to sell this software, have a lawyer review the GPLv3
> implications before release.

## Licence

Source code in this repository is released under the **MIT Licence**.
See [LICENSE](LICENSE).