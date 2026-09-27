#Requires -Version 5.1
<#
.SYNOPSIS
    Parse a PowerShell script with the Windows PowerShell 5.1 AST parser and
    report any syntax errors WITHOUT executing it. Useful for CI sanity checks.

.PARAMETER Path
    Absolute path to the .ps1 to validate.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $Path
)

if (-not (Test-Path $Path)) {
    Write-Host "::ERROR:: file not found: $Path" -ForegroundColor Red
    exit 2
}

$tokens = $null
$errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile($Path, [ref]$tokens, [ref]$errors)

if ($errors -and $errors.Count -gt 0) {
    Write-Host "::ERROR:: syntax errors in $Path" -ForegroundColor Red
    foreach ($e in $errors) {
        Write-Host ("  line {0,4} col {1,3}: {2}" -f $e.Extent.StartLineNumber, $e.Extent.StartColumnNumber, $e.Message)
    }
    exit 1
}

Write-Host "OK: $Path parses cleanly under Windows PowerShell 5.1" -ForegroundColor Green
exit 0
