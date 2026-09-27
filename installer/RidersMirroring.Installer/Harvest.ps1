# Harvest.ps1 — auto-generates obj\HarvestedFiles.wxs from publish\win-x64\
# by walking the directory and emitting one <File> per artifact, grouped under
# a single <ComponentGroup Id="HarvestedFiles">.

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$PublishDir,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

if (-not (Test-Path $PublishDir)) {
    throw "Publish directory not found: $PublishDir"
}

$root = (Resolve-Path $PublishDir).ProviderPath
$files = Get-ChildItem -Path $root -Recurse -File | Where-Object { $_.Name -ne '.gitkeep' } | Sort-Object FullName

function Get-WixId([string]$Path) {
    $rel = $Path.Substring($root.Length).TrimStart('\', '/')
    $id = $rel -replace '[^A-Za-z0-9]', '_'
    return "F_$id"
}

function Get-DirId([string]$RelPath) {
    $clean = $RelPath.Trim('\', '/') -replace '[^A-Za-z0-9]', '_'
    return "DIR_$clean"
}

function Get-WixGuid([string]$Path) {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Path.ToLowerInvariant())
    $md5 = [System.Security.Cryptography.MD5]::Create()
    $hash = $md5.ComputeHash($bytes)
    $md5.Dispose()
    $guid = [Guid]::New([byte[]]$hash)
    return $guid.ToString().ToUpperInvariant()
}

$subDirs = Get-ChildItem -Path $root -Recurse -Directory | Sort-Object FullName

$sb = New-Object System.Text.StringBuilder

[void]$sb.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$sb.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">')

if ($subDirs.Count -gt 0) {
    [void]$sb.AppendLine('  <Fragment>')
    [void]$sb.AppendLine('    <DirectoryRef Id="INSTALLFOLDER">')

    foreach ($sd in $subDirs) {
        $parent = $sd.Parent.FullName
        $parentRef = if ($parent -eq $root) { "INSTALLFOLDER" } else { Get-DirId ($parent.Substring($root.Length)) }
        $dirId = Get-DirId ($sd.FullName.Substring($root.Length))
        $name = $sd.Name
        [void]$sb.AppendLine("      <Directory Id=""$dirId"" Name=""$name"" />")
    }

    [void]$sb.AppendLine('    </DirectoryRef>')
    [void]$sb.AppendLine('  </Fragment>')
}

[void]$sb.AppendLine('  <Fragment>')
[void]$sb.AppendLine('    <ComponentGroup Id="HarvestedFiles">')

foreach ($file in $files) {
    $id   = Get-WixId $file.FullName
    $guid = Get-WixGuid $file.FullName
    $rel  = $file.FullName.Substring($root.Length).TrimStart('\', '/').Replace('\', '/')
    $name = $file.Name

    $dirName = $file.Directory.FullName
    $targetDirId = if ($dirName -eq $root) { "INSTALLFOLDER" } else { Get-DirId ($dirName.Substring($root.Length)) }

    [void]$sb.AppendLine("      <Component Id=""$id"" Guid=""$guid"" Directory=""$targetDirId"">")
    [void]$sb.AppendLine("        <File Id=""$id"" Source=""publish\win-x64\$rel"" Name=""$name"" KeyPath=""yes"" />")
    [void]$sb.AppendLine("      </Component>")
}

[void]$sb.AppendLine('    </ComponentGroup>')
[void]$sb.AppendLine('  </Fragment>')
[void]$sb.AppendLine('</Wix>')

$dir = Split-Path -Parent $OutputFile
if (-not (Test-Path $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

Set-Content -Path $OutputFile -Value $sb.ToString() -Encoding UTF8

Write-Host "Harvested $($files.Count) file(s) -> $OutputFile"
