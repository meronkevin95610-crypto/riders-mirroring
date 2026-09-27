# Riders Mirroring

Desktop application for Windows that mirrors an **Android** device (USB or
Wi-Fi, via ADB + scrcpy-server) and an **iPhone** (via AirPlay, using UxPlay
as a redistributable external process).

> **Status:** v1.0 — multi-device scrcpy mirroring, chrome-style tab hub,
> 261 tests verts (Debug + Release). Site web officiel :
> **https://riders-mirroring.github.io/**

---

## 📦 Pour les utilisateurs

👉 [Télécharger l'installateur Windows](https://riders-mirroring.github.io/download.html)

Site officiel servi par **GitHub Pages** :
- Accueil : https://riders-mirroring.github.io/
- Télécharger : https://riders-mirroring.github.io/download.html
- Nouveautés : https://riders-mirroring.github.io/changelog.html

Le MSI est aussi publié sur
[GitHub Releases](https://github.com/riders-mirroring/riders-mirroring/releases/latest).

---

## 🚀 Pour les mainteneurs — publier une release

1. **Premier déploiement uniquement** : crée le repo GitHub
   `riders-mirroring/riders-mirroring`, puis :

   ```powershell
   git remote add origin https://github.com/riders-mirroring/riders-mirroring.git
   git push -u origin master
   ```

2. **Active GitHub Pages** : repo → Settings → Pages → Source :
   *GitHub Actions* (pas "Deploy from a branch").

3. **Publie une release** :

   ```powershell
   git tag v1.0.0
   git push origin v1.0.0
   ```

   Le workflow `.github/workflows/release.yml` :
   - Build + tests en Debug + Release
   - Construit le MSI Release win-x64
   - Le publie sur GitHub Releases avec SHA256
   - Bundle une copie du MSI dans `docs/site/downloads/`

4. **Mise à jour du site** : à chaque push touchant `docs/site/**`, le
   workflow `.github/workflows/pages.yml` redéploie le site sur
   `gh-pages`.

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