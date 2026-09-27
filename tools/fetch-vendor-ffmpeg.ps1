# fetch-vendor-ffmpeg.ps1 — Idempotent vendor script for the FFmpeg LGPL DLLs.
#
# Reads the pinned version/sha256 from vendor/ffmpeg/.known-good, downloads
# the archive (zip or 7z), verifies its sha256, and extracts only the
# runtime DLLs (+ LICENSE / NOTICE) into vendor/ffmpeg/. Idempotent: skips
# the download and the extraction if everything is already in place.
#
# Usage (from repo root):
#   pwsh tools/fetch-vendor-ffmpeg.ps1
#
# Force mode (re-download and re-extract even if already in place):
#   pwsh tools/fetch-vendor-ffmpeg.ps1 -Force
#
# Verbose mode:
#   pwsh tools/fetch-vendor-ffmpeg.ps1 -Verbose
#
# Pre-flight:
#   - PowerShell 7+ (pwsh) — Windows PowerShell 5.1 ships ancient tar/Expand-Archive
#     that do not preserve NTFS ACLs predictably; FFmpeg DLLs don't care but
#     reproducibility suffers if anyone extracts on PS 5.1.
#   - 7z.exe on PATH (Scoop ships it; winget install 7zip) — required when the
#     pinned asset is a .7z (current pin uses GyanD ffmpeg-*-full_build-shared.7z).
#     .zip assets use Expand-Archive directly.
#   - Network access to the pinned URL.

[CmdletBinding()]
param(
    [switch] $Force
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# --------------------------------------------------------------------------
# Resolved paths
# --------------------------------------------------------------------------
$script:RepoRoot       = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$script:VendorDir      = Join-Path $script:RepoRoot 'vendor/ffmpeg'
$script:BinDir         = Join-Path $script:VendorDir 'bin'
$script:CacheDir       = Join-Path $script:VendorDir '.cache'
$script:KnownGoodPath  = Join-Path $script:VendorDir '.known-good'

# --------------------------------------------------------------------------
# Step 0 — make sure we are running under pwsh, not Windows PowerShell 5.1
# --------------------------------------------------------------------------
if ($PSVersionTable.PSEdition -ne 'Core') {
    Write-Warning "This script targets PowerShell 7+ (pwsh). You are running $($PSVersionTable.PSEdition). Some steps may degrade. Re-run with 'pwsh' if anything misbehaves."
}

# --------------------------------------------------------------------------
# Step 1 — read .known-good
# --------------------------------------------------------------------------
if (-not (Test-Path $KnownGoodPath)) {
    throw "Missing $KnownGoodPath — see vendor/ffmpeg/README.md to bootstrap."
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

$requiredKeys = @('tag','asset-filename','url','sha256','source')
foreach ($k in $requiredKeys) {
    if (-not $pin.ContainsKey($k) -or [string]::IsNullOrWhiteSpace($pin[$k])) {
        throw ".known-good is missing required key '$k'."
    }
}
# The placeholder is detected by the prefix '# sha256:' on the original line.
if ($pin['sha256'] -match '^#\s*sha256') {
    throw "sha256 placeholder not replaced yet in $KnownGoodPath. Fill in the real value before running."
}

Write-Host "Pinned:" -ForegroundColor Cyan
foreach ($k in 'tag','asset-filename','sha256') {
    Write-Host ("  {0,-18} {1}" -f $k, $pin[$k])
}

# --------------------------------------------------------------------------
# Step 2 — ensure vendor dirs exist
# --------------------------------------------------------------------------
foreach ($d in @($VendorDir, $BinDir, $CacheDir)) {
    if (-not (Test-Path $d)) {
        New-Item -ItemType Directory -Path $d -Force | Out-Null
    }
}

# --------------------------------------------------------------------------
# Step 3 — locate (or download) the zip
# --------------------------------------------------------------------------
$zipName  = $pin['asset-filename']
$zipPath  = Join-Path $CacheDir $zipName
$zipUrl   = $pin['url']
$expectedSha = $pin['sha256'].ToLowerInvariant()

$needsDownload = -not (Test-Path $zipPath)
if ($needsDownload) {
    Write-Host "Downloading $zipUrl" -ForegroundColor Green
    # Disable progress bar — it's noisy over slow connections.
    $ProgressPreference = 'SilentlyContinue'
    try {
        Invoke-WebRequest -Uri $zipUrl -OutFile $zipPath -UseBasicParsing -MaximumRedirection 5
    } finally {
        $ProgressPreference = 'Continue'
    }
} else {
    Write-Host "Reusing cached $zipPath" -ForegroundColor DarkGray
}

# --------------------------------------------------------------------------
# Step 4 — verify sha256 (overrides any 'cached' assumption when refetched)
# --------------------------------------------------------------------------
$actualSha = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
if ($actualSha -ne $expectedSha) {
    Write-Host "SHA-256 mismatch:" -ForegroundColor Red
    Write-Host "  expected: $expectedSha"
    Write-Host "  actual  : $actualSha"
    # Don't delete the file automatically — let the user decide. Just bail.
    throw "Integrity check failed — refusing to extract. Delete $zipPath manually if you trust the upstream source."
}
Write-Host "SHA-256 OK" -ForegroundColor Green

# --------------------------------------------------------------------------
# Step 5 — extract only the LGPL-shared runtime DLLs (and the LICENSE files
# they ship with), into vendor/ffmpeg/.
#
# Strategy: expand to a staging subdir, move the bits we want, throw the rest.
# We never overwrite already-populated bin/ unless -Force was passed.
# --------------------------------------------------------------------------

$populate = $false
if (-not $Force) {
    $alreadyThere = Get-ChildItem -LiteralPath $BinDir -Filter '*.dll' -ErrorAction SilentlyContinue
    if ($alreadyThere -and $alreadyThere.Count -gt 0) {
        Write-Host "vendor/ffmpeg/bin already populated ($($alreadyThere.Count) DLLs) — skipping extraction. Use -Force to re-extract." -ForegroundColor Yellow
    } else {
        $populate = $true
    }
} else {
    # Force: clean and repopulate.
    Get-ChildItem -LiteralPath $BinDir -Force -ErrorAction SilentlyContinue | Remove-Item -Recurse -Force
    $populate = $true
}

if ($populate) {
    Write-Host "Extracting LGPL-shared runtime…" -ForegroundColor Green

    # Both BtbN (zip) and GyanD (7z) archives use a single top-level
    # directory named like the asset minus its extension, holding 'bin/' and
    # the LICENSE/NOTICE files at its root. We extract to a staging dir then
    # pluck what we need.
    $stage = Join-Path $CacheDir ([IO.Path]::GetFileNameWithoutExtension($zipName))
    if (Test-Path $stage) {
        Remove-Item -LiteralPath $stage -Recurse -Force
    }
    New-Item -ItemType Directory -Path $stage -Force | Out-Null

    $ext = [IO.Path]::GetExtension($zipName).ToLowerInvariant()
    if ($ext -eq '.zip') {
        try {
            Expand-Archive -LiteralPath $zipPath -DestinationPath $stage -Force
        } catch {
            throw "Expand-Archive failed: $($_.Exception.Message)"
        }
    } elseif ($ext -eq '.7z') {
        $7z = (Get-Command 7z.exe -ErrorAction SilentlyContinue).Source
        if (-not $7z) {
            throw "7z.exe not found on PATH. Install 7-Zip (winget install 7zip, or scoop install 7zip)."
        }
        # 7z exit codes: 0 = no error, 1 = warning (non-fatal), 2 = fatal.
        # We treat anything > 1 as fatal.
        & $7z x $zipPath -o"$stage" -y *>&1 | Out-Null
        if ($LASTEXITCODE -gt 1) {
            throw "7z extraction failed with exit code $LASTEXITCODE"
        }
    } else {
        throw "Unsupported archive extension '$ext' — only .zip and .7z are wired up."
    }

    # Find the inner bin/ — there must be exactly one.
    $candidates = Get-ChildItem -LiteralPath $stage -Directory -ErrorAction SilentlyContinue
    $innerStage = $null
    foreach ($c in $candidates) {
        $maybeBin = Join-Path $c.FullName 'bin'
        if (Test-Path $maybeBin) {
            $innerStage = $c.FullName
            break
        }
    }
    if (-not $innerStage) {
        # Some variants ship without the wrapper dir; in that case bin/ is
        # right under $stage.
        $probe = Join-Path $stage 'bin'
        if (Test-Path $probe) {
            $innerStage = $stage
        } else {
            throw "Could not locate 'bin/' inside the extracted archive at $stage."
        }
    }

    # ---- Whitelist of LGPL-shared runtime DLLs we actually need ----
    # avcodec : H.264 video decoding
    # avformat: container demuxing (we only need the helper, but it's tiny)
    # avutil  : common utilities (transcoding, refcount, log)
    # swscale : pixel format conversion (YUV420P → BGRA for WPF)
    # swresample: audio resampling (we don't decode audio today, but cheap)
    # avfilter: optional, useful if we ever add overlays
    $wantedGlobs = @(
        'avcodec-*.dll',
        'avformat-*.dll',
        'avutil-*.dll',
        'swscale-*.dll',
        'swresample-*.dll',
        'avfilter-*.dll'
    )

    $copied = 0
    foreach ($g in $wantedGlobs) {
        Get-ChildItem -LiteralPath (Join-Path $innerStage 'bin') -Filter $g -ErrorAction SilentlyContinue | ForEach-Object {
            Copy-Item -LiteralPath $_.FullName -Destination $BinDir -Force
            $copied++
        }
    }
    if ($copied -eq 0) {
        throw "No matching LGPL-shared DLLs found in extracted archive. Naming may have changed — adjust the whitelist in this script."
    }
    Write-Host "Copied $copied DLL(s) into $BinDir" -ForegroundColor Green

    # ---- License files (verbatim from BtbN build root) ----
    # BtbN win64-lgpl-shared archives historically ship one or more of:
    #   LICENSE, LICENSE.txt, NOTICE, NOTICE.txt — depending on the FFmpeg
    # # snapshot. We don't enforce a fixed name; we copy any file at the
    # inner archive root that matches the common license naming.
    $licenseNames = @('LICENSE','LICENSE.txt','NOTICE','NOTICE.txt','COPYING','COPYING.LESSER')
    foreach ($name in $licenseNames) {
        $src = Join-Path $innerStage $name
        if (Test-Path -LiteralPath $src) {
            $dstName = $name
            # LICENSE.txt → LICENSE in the vendor dir (the more canonical name).
            if ($name -eq 'LICENSE.txt') { $dstName = 'LICENSE' }
            if ($name -eq 'NOTICE.txt')  { $dstName = 'NOTICE' }
            if ($name -eq 'COPYING.LESSER') { $dstName = 'LICENSE' }
            $dst = Join-Path $VendorDir $dstName
            if (-not (Test-Path -LiteralPath $dst) -or $Force) {
                Copy-Item -LiteralPath $src -Destination $dst -Force
            }
        }
    }
}

# --------------------------------------------------------------------------
# Step 6 — record the fetch timestamp
# --------------------------------------------------------------------------
Write-Host "Updating .known-good…" -ForegroundColor DarkCyan
$content = Get-Content -LiteralPath $KnownGoodPath -Encoding UTF8
$content = $content -replace 'fetched-at-utc:.*', "fetched-at-utc: $((Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ'))"
Set-Content -LiteralPath $KnownGoodPath -Value $content -Encoding UTF8

Write-Host ""
Write-Host "Done." -ForegroundColor Green
Write-Host "  pin      : $($pin['tag'])"
Write-Host "  asset    : $($pin['asset-filename'])"
Write-Host "  sha256   : $expectedSha"
Write-Host "  bin dir  : $BinDir"
Write-Host ""
Write-Host "Next: add a '<None Include=... />' entry in src/RidersMirroring.Core/RidersMirroring.Core.csproj to ship these DLLs alongside the binary, then move on to FFmpeg.AutoGen integration (step 1B)."
