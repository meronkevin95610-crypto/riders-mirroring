#Requires -Version 5.1
<#
.SYNOPSIS
    Run riders-mirroring-host's vitest suite, parse the output, and emit a
    per-file PASS/FAIL report. Exits non-zero if any test failed or if the
    suite couldn't run.

.DESCRIPTION
    This script is the companion to the signaling-token-TTL fix:
      - calls `npm test` in riders-mirroring-host/
      - expects the suite to include the new tests:
          tests/ws-server-token.test.ts   (H1: token authority)
          tests/ws-server-ttl.test.ts     (H2: TTL window)
          tests/ws-server-reconnect.test.ts (H3: same Session on reconnect)
      - the existing ws-server.test.ts, ws-server-signal.test.ts, qr.test.ts,
        and network.test.ts must still pass

    After running, the script prints a summary table:
      FILE                                          PASS / FAIL
      tests/ws-server.test.ts                       5/5 PASS
      tests/ws-server-signal.test.ts                3/3 PASS
      tests/ws-server-token.test.ts                 4/4 PASS  <-- new
      tests/ws-server-ttl.test.ts                   3/3 PASS  <-- new
      tests/ws-server-reconnect.test.ts             2/2 PASS  <-- new
      tests/qr.test.ts                              2/2 PASS
      tests/network.test.ts                         3/3 PASS

.PARAMETER RepoRoot
    Absolute path to the directory containing riders-mirroring-host/. Defaults
    to the parent of the script's location.

.PARAMETER TimeoutSeconds
    Hard timeout for `npm test`. Default 180s.

.EXAMPLE
    pwsh tools/validate-host-tests.ps1
#>
[CmdletBinding()]
param(
    [string] $RepoRoot,
    [int]    $TimeoutSeconds = 180
)

# Resolve $RepoRoot robustly across invocation styles. `$PSScriptRoot` is
# empty when the script is launched through `& 'powershell.exe' -File`,
# so we fall back to `$MyInvocation.MyCommand.Path` and ultimately to the
# process cwd.
if (-not $RepoRoot) {
    $scriptPath = $PSScriptRoot
    if (-not $scriptPath) { $scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path }
    if (-not $scriptPath) { $scriptPath = (Get-Location).Path }
    $RepoRoot = Split-Path -Parent $scriptPath
}

$ErrorActionPreference = 'Stop'

# Look for the host repo: by default it's a sibling of the repo that
# contains this script (e.g. `riders-mirroring-host/` next to
# `riders-mirroring/`). If not found there, fall back to a sub-folder
# called `riders-mirroring-host` inside the current repo (monorepo layout).
$HostDir = Join-Path $RepoRoot 'riders-mirroring-host'
if (-not (Test-Path $HostDir)) {
    $parentSibling = Join-Path (Split-Path -Parent $RepoRoot) 'riders-mirroring-host'
    if (Test-Path $parentSibling) {
        $HostDir = $parentSibling
    } else {
        Write-Host "::ERROR:: riders-mirroring-host not found at $HostDir nor at $parentSibling" -ForegroundColor Red
        Write-Host "Pass -HostDir to override, or set up the host repo as a sibling of this script's repo." -ForegroundColor Yellow
        exit 2
    }
}
Write-Host "Using host dir: $HostDir" -ForegroundColor DarkGray

if (-not (Test-Path (Join-Path $HostDir 'package.json'))) {
    Write-Host "::ERROR:: package.json missing in $HostDir" -ForegroundColor Red
    exit 2
}

# Make sure node_modules exists; if not, run npm ci (sandbox-friendly: read-only HOME).
Push-Location $HostDir
try {
    if (-not (Test-Path 'node_modules')) {
        Write-Host "node_modules missing — running 'npm ci' first..." -ForegroundColor Yellow
        & npm ci
        if ($LASTEXITCODE -ne 0) {
            Write-Host "::ERROR:: npm ci failed (exit $LASTEXITCODE)" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    }

    Write-Host ""
    Write-Host "Running: npm test -- --reporter=verbose" -ForegroundColor Cyan
    Write-Host "(timeout: ${TimeoutSeconds}s)"
    Write-Host ""

    # Capture both stdout and stderr, with a hard timeout.
    $job = Start-Job -ScriptBlock {
        Set-Location $using:HostDir
        & npm test -- --reporter=verbose 2>&1
    }
    $completed = Wait-Job $job -Timeout $TimeoutSeconds
    if (-not $completed) {
        Stop-Job $job
        Remove-Job $job -Force
        Write-Host "::ERROR:: npm test exceeded ${TimeoutSeconds}s timeout" -ForegroundColor Red
        exit 3
    }
    $output = Receive-Job $job -Keep
    Remove-Job $job -Force

    $exitCode = $LASTEXITCODE
}
finally {
    Pop-Location
}

# Persist full output for post-mortem.
$logDir = Join-Path $RepoRoot 'BenchmarkDotNet.Artifacts'
if (-not (Test-Path $logDir)) { New-Item -ItemType Directory -Path $logDir -Force | Out-Null }
$logFile = Join-Path $logDir 'host-tests.log'
$output | Out-File -FilePath $logFile -Encoding utf8

# ─────────────────────────────────────────────────────────────────────
# Parse output — vitest verbose reporter prints:
#   ✓ <test name>  (<ms>ms)
#   ✗ <test name>  (<ms>ms)
#   FAIL  tests/foo.test.ts > describe > test name
#   PASS  tests/foo.test.ts > describe > test name
# We'll count passes/fails per file from the verbose lines.
# ─────────────────────────────────────────────────────────────────────

$lines = $output -split "`n"
$byFile = @{}  # file -> @{ pass=N; fail=N; tests=@() }

foreach ($line in $lines) {
    $trimmed = $line.Trim()

    # Vitest verbose "  ✓ path/to/file.test.ts > describe > test name  (Xms)"
    if ($trimmed -match '^[✓✗×]\s+(?<file>[\w\./-]+\.test\.ts)\s*>\s*(?<rest>.+?)\s*\((?<ms>\d+)\s*ms\)\s*$') {
        $file = $Matches.file
        $rest = $Matches.rest
        $ok   = $trimmed.StartsWith('✓')
        if (-not $byFile.ContainsKey($file)) { $byFile[$file] = @{ pass = 0; fail = 0; tests = @() } }
        if ($ok) { $byFile[$file].pass++ } else { $byFile[$file].fail++ }
        $byFile[$file].tests += [pscustomobject]@{
            name = $rest
            pass = $ok
        }
        continue
    }

    # Final summary line: "Test Files  3 passed (3)"
    if ($trimmed -match '^Test Files\s+(?<files>\d+)\s+(?<verdict>passed|failed)') {
        $script:summaryFiles = [int]$Matches.files
        $script:summaryFilesVerdict = $Matches.verdict
    }
    if ($trimmed -match '^\s*Tests\s+(?<tests>\d+)\s+(?<verdict>passed|failed)') {
        $script:summaryTests = [int]$Matches.tests
        $script:summaryTestsVerdict = $Matches.verdict
    }
}

# ─────────────────────────────────────────────────────────────────────
# Render report
# ─────────────────────────────────────────────────────────────────────

Write-Host ""
Write-Host "═══════════════════════════════════════════════════════════════════════════════" -ForegroundColor White
Write-Host "  riders-mirroring-host — test report" -ForegroundColor White
Write-Host "═══════════════════════════════════════════════════════════════════════════════" -ForegroundColor White

$expectedNew = @(
    @{ file = 'tests/ws-server-token.test.ts';      minTests = 4; label = 'H1 token authority' }
    @{ file = 'tests/ws-server-ttl.test.ts';        minTests = 3; label = 'H2 token TTL' }
    @{ file = 'tests/ws-server-reconnect.test.ts';  minTests = 2; label = 'H3 reconnect within TTL' }
)
$allOk = $true

if ($byFile.Count -eq 0) {
    Write-Host ""
    Write-Host "::ERROR:: No test verdicts parsed from vitest output." -ForegroundColor Red
    Write-Host "Full output was written to $logFile — please inspect manually."
    exit 4
}

foreach ($entry in ($byFile.GetEnumerator() | Sort-Object Name)) {
    $file = $entry.Key
    $data = $entry.Value
    $total = $data.pass + $data.fail
    $verdict = if ($data.fail -eq 0) { 'PASS' } else { 'FAIL' }
    $color   = if ($data.fail -eq 0) { 'Green' } else { 'Red' }
    if ($data.fail -gt 0) { $allOk = $allOk -and $false }
    Write-Host ("  {0,-48} {1,3} pass / {2,-3} fail  [{3}]" -f $file, $data.pass, $data.fail, $verdict) -ForegroundColor $color
}

Write-Host ""
Write-Host ("  TOTAL  {0,3} pass / {1,-3} fail  [{2}]" -f `
    (($byFile.Values | Measure-Object -Property pass -Sum).Sum), `
    (($byFile.Values | Measure-Object -Property fail -Sum).Sum), `
    (if ($allOk) { 'ALL GREEN' } else { 'REGRESSION' })) -ForegroundColor (
    if ($allOk) { 'Green' } else { 'Red' })

# Coverage check: ensure the new H1/H2/H3 files are present and have tests.
Write-Host ""
Write-Host "── Coverage check (new tests) ────────────────────────────────────────────────" -ForegroundColor White

foreach ($exp in $expectedNew) {
    if (-not $byFile.ContainsKey($exp.file)) {
        Write-Host ("  ::MISSING:: {0,-44} ({1})" -f $exp.file, $exp.label) -ForegroundColor Red
        $allOk = $allOk -and $false
        continue
    }
    $total = $byFile[$exp.file].pass + $byFile[$exp.file].fail
    if ($total -lt $exp.minTests) {
        Write-Host ("  ::INCOMPLETE:: {0,-42} {1} tests (expected ≥ {2})" -f $exp.file, $total, $exp.minTests) -ForegroundColor Yellow
        $allOk = $allOk -and $false
    } elseif ($byFile[$exp.file].fail -gt 0) {
        Write-Host ("  ::FAILURES:: {0,-42} {1} failed" -f $exp.file, $byFile[$exp.file].fail) -ForegroundColor Red
        $allOk = $allOk -and $false
    } else {
        Write-Host ("  {0,-44} {1,3} tests  ✓" -f $exp.file, $total) -ForegroundColor Green
    }
}


Write-Host ""
Write-Host "Full vitest output: $logFile" -ForegroundColor Gray
Write-Host ""

if (-not $allOk -or $exitCode -ne 0) {
    Write-Host "EXIT 1 — regression detected (or suite returned non-zero)." -ForegroundColor Red
    exit 1
}

Write-Host "EXIT 0 — all tests green." -ForegroundColor Green
exit 0
