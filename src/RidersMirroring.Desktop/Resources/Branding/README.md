# Branding assets

This folder holds the **Riders Mirroring** brand assets bundled with the application.

## Files

| File | Purpose | Format |
|---|---|---|
| `riders-mirroring-logo.png` | Master logo - full-colour, square | PNG, sRGB |
| `riders-mirroring-logo.ico` | Windows multi-resolution icon | ICO |

## Adding your own logo

1. Drop a square PNG (>= 512x512) into this folder as `riders-mirroring-logo.png`.
2. From `tools/`, run `generate-app-icon.ps1`. This regenerates `Resources/App.ico` with 16/32/48/64/128/256 entries via `System.Drawing` (no external dependencies).

## Third-party logos

The scrcpy upstream logo is NOT bundled with Riders Mirroring. See `licenses/scrcpy-NOTICE.txt` for Apache 2.0 attribution of the upstream project itself.

UxPlay is similarly not bundled - spawned as an external process at runtime.
