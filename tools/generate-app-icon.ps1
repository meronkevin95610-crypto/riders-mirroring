# Generate a multi-resolution .ico file for Riders Mirroring from a master PNG.
# Compatible with Windows PowerShell 5.1+ (no PS 7-only features).
# Usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\generate-app-icon.ps1

Add-Type -AssemblyName System.Drawing

$ErrorActionPreference = 'Stop'

$iconSizes = @(16, 32, 48, 64, 128, 256)
$root      = Resolve-Path (Join-Path $PSScriptRoot '..')
$outDir    = Join-Path $root 'src\RidersMirroring.Desktop\Resources'
$branding  = Join-Path $outDir 'Branding'
$outPath   = Join-Path $outDir 'App.ico'
$pngPath   = Join-Path $branding 'riders-mirroring-logo.png'

if (-not (Test-Path $pngPath)) {
    Write-Error "Source PNG not found at $pngPath. Drop a >= 512x512 PNG there and re-run."
    exit 1
}

# Encode each size to PNG bytes first (System.Drawing.Icon.Save needs PNG-in-icon).
$entries = @()
foreach ($s in $iconSizes) {
    $src = [System.Drawing.Image]::FromFile($pngPath)
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode   = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($src, 0, 0, $s, $s)
    $g.Dispose()
    $src.Dispose()

    $pngStream = New-Object System.IO.MemoryStream
    $bmp.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
    $bytes = $pngStream.ToArray()
    $pngStream.Dispose()

    $w = if ($s -ge 256) { 0 } else { [byte]$s }
    $h = if ($s -ge 256) { 0 } else { [byte]$s }
    $entries += [pscustomobject]@{
        Width  = $w
        Height = $h
        Size   = $bytes.Length
        Bytes  = $bytes
    }
}

# Bundle all sizes into a single .ico container (PNG-in-ICO format, supported since Vista).
$ms = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter $ms

$bw.Write([uint16]0)               # reserved
$bw.Write([uint16]1)               # type = 1 (icon)
$bw.Write([uint16]$entries.Count)  # image count

$headerSize = 6 + (16 * $entries.Count)
$offset = $headerSize
foreach ($e in $entries) {
    $bw.Write([byte]$e.Width)
    $bw.Write([byte]$e.Height)
    $bw.Write([byte]0)              # color count
    $bw.Write([byte]0)              # reserved
    $bw.Write([uint16]1)            # color planes
    $bw.Write([uint16]32)           # bits per pixel
    $bw.Write([uint32]$e.Size)
    $bw.Write([uint32]$offset)
    $offset += $e.Size
}

foreach ($e in $entries) {
    $bw.Write($e.Bytes)
}

$bw.Flush()
[System.IO.File]::WriteAllBytes($outPath, $ms.ToArray())
$bw.Close()

Write-Host "Wrote $outPath with $($entries.Count) sizes: $($iconSizes -join ', ')"
