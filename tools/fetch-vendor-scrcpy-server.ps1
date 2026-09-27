# fetch-vendor-scrcpy-server.ps1 — Idempotent vendor script for scrcpy-server.jar
#
# Reads the pinned version/sha256 from src/RidersMirroring.Core/Scrcpy/.scrcpy-server-known-good,
# downloads the Genymobile upstream asset, verifies its sha256, and (re)writes
# vendor/scrcpy-server/scrcpy-server.jar. Idempotent: skips download + verification
# if everything is already in place.
#
# The pin file lives next to ScrcpyServer.cs (not at the repo root) because
# the consumer code (ScrcpyServer.LocateServerJar) reads from
# vendor/scrcpy-server/scrcpy-server.jar — co-locating the pin file means a
# future maintainer editing the path resolution finds it next door.
#
# Usage (from repo root):
#   pwsh tools/fetch-vendor-scrcpy-server.ps1
#
# Force mode (re-download and re-verify even if the jar is already in place):
#   pwsh tools/fetch-vendor-scrcpy-server.ps1 -Force
#
# Notes:
#   * Requires PowerShell 7+ (we use Get-FileHash which works on PS 5.1 but
#     the rest of the toolchain targets 7).
#   * Network access to github.com (released under the "github.com" host).
#   * The upstream asset is served as application/octet-stream without an
#     extension. We save it directly to the local-name declared in the pin
#     file (e.g. `scrcpy-server.jar`) so that ScrcpyServer.cs keeps finding
#     it at the unchanged on-disk path.

[CmdletBinding()]
param(
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --------------------------------------------------------------------------
# Resolved paths
# --------------------------------------------------------------------------
$script:RepoRoot      = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:VendorDir     = Join-Path $script:RepoRoot 'vendor/scrcpy-server'
$script:CacheDir      = Join-Path $script:VendorDir '.cache'
$script:KnownGoodPath = Join-Path $script:RepoRoot 'src/RidersMirroring.Core/Scrcpy/.scrcpy-server-known-good'

# --------------------------------------------------------------------------
# Step 0 — pwsh warning
# --------------------------------------------------------------------------
if ($PSVersionTable.PSEdition -ne 'Core') {
    Write-Warning "This script targets PowerShell 7+ (pwsh). You are running $($PSVersionTable.PSEdition). Some steps may degrade. Re-run with 'pwsh' if anything misbehaves."
}

# --------------------------------------------------------------------------
# Step 1 — read the pin file (yaml-ish: "key: value")
# --------------------------------------------------------------------------
if (-not (Test-Path $KnownGoodPath)) {
    throw "Missing $KnownGoodPath"
}

Write-Verbose "Reading pin from $KnownGoodPath"
$pin = @{}
Get-Content -LiteralPath $KnownGoodPath -Encoding UTF8 | ForEach-Object {
    $line = $_.Trim()
    if ($line.Length -eq 0) { return }
    if ($line.StartsWith('#')) { return }
    $eq = $line.IndexOf(':')
    if ($eq -lt 1) { return }
    $k = $line.Substring(0, $eq).Trim()
    $v = $line.Substring($eq + 1).Trim()
    $pin[$k] = $v
}

foreach ($k in 'tag','asset-filename','local-name','url','sha256','source','upstream-version') {
    if (-not $pin.ContainsKey($k) -or [string]::IsNullOrWhiteSpace($pin[$k])) {
        throw ".scrcpy-server-known-good is missing required key '$k'."
    }
}

Write-Host "Pinned:" -ForegroundColor Cyan
foreach ($k in 'tag','local-name','sha256','upstream-version') {
    Write-Host ("  {0,-18} {1}" -f $k, $pin[$k])
}

# --------------------------------------------------------------------------
# Step 2 — ensure dirs
# --------------------------------------------------------------------------
foreach ($d in @($VendorDir, $CacheDir)) {
    if (-not (Test-Path $d)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
    }
}

# --------------------------------------------------------------------------
# Step 3 — locate (or download) the upstream asset
# --------------------------------------------------------------------------
$cacheName    = $pin['asset-filename']
$cachedAsset  = Join-Path $CacheDir $cacheName
$expectedSha  = $pin['sha256'].ToLowerInvariant()
$localJar     = Join-Path $VendorDir $pin['local-name']
$downloadUrl  = $pin['url']

$needsDownload = -not (Test-Path $cachedAsset)
if ($needsDownload) {
    Write-Host "Downloading $downloadUrl" -ForegroundColor Green
    $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest -Uri $downloadUrl -OutFile $cachedAsset -UseBasicParsing -MaximumRedirection 5
    } finally {
        $ProgressPreference = 'Continue'
    }
} else {
    Write-Host "Reusing cached $cachedAsset" -ForegroundColor DarkGray
}

# --------------------------------------------------------------------------
# Step 4 — verify sha256 every run (do not trust the cache silently)
# --------------------------------------------------------------------------
$actualSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $cachedAsset).Hash.ToLowerInvariant()
if ($actualSha -ne $expectedSha) {
    Write-Host "SHA-256 mismatch:" -ForegroundColor Red
    Write-Host "  expected: $expectedSha"
    Write-Host "  actual  : $actualSha"
    throw "Integrity check failed — refusing to copy. Delete $cachedAsset manually if you trust the upstream source."
}
Write-Host "SHA-256 OK" -ForegroundColor Green

# --------------------------------------------------------------------------
# Step 5 — copy/overwrite the local jar (under vendor/scrcpy-server/).
# This is `PreserveNewest` by default — we only rewrite when the upstream
# really changed OR on -Force.
# --------------------------------------------------------------------------
if (-not (Test-Path -LiteralPath $localJar)) {
    Write-Host "Installing to $localJar (first run)" -ForegroundColor Green
    Copy-Item -LiteralPath $cachedAsset -Destination $localJar
} elseif ($Force -or (Get-FileHash -Algorithm SHA256 -LiteralPath $localJar).Hash.ToLowerInvariant() -ne $expectedSha) {
    Write-Host "Re-installing $localJar (hash mismatch or -Force)" -ForegroundColor Yellow
    Copy-Item -LiteralPath $cachedAsset -Destination $localJar -Force
} else {
    Write-Host "vendor/scrcpy-server/$($pin['local-name']) already up to date — skipping copy." -ForegroundColor DarkGray
}

# --------------------------------------------------------------------------
# Step 6 — record fetch timestamp
# --------------------------------------------------------------------------
Write-Host "Updating .scrcpy-server-known-good…" -ForegroundColor DarkCyan
$content = Get-Content -LiteralPath $KnownGoodPath -Encoding UTF8
$content = $content -replace 'fetched-at-utc:.*', "fetched-at-utc: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
Set-Content -LiteralPath $KnownGoodPath -Value $content -Encoding UTF8

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  pin       : $($pin['tag'])"
Write-Host "  asset     : $($pin['asset-filename'])"
Write-Host "  local-name: $($pin['local-name'])"
Write-Host "  sha256    : $expectedSha"
Write-Host "  jar       : $localJar"
Write-Host ""
Write-Host "Next: ScrcpyServer.cs must pass '$($pin['upstream-version'])' as the first argument to app_process for the server to accept the connection."
