#Requires -Version 5.1
<#
.SYNOPSIS
Install one hash-verified CreatorOS Beta 1 player package into an existing BepInEx client.

.DESCRIPTION
The installer preserves unrelated files, backs up every replaced target, writes through a
same-directory temporary file, and records the exact installed hashes. It does not install
BepInEx, enroll a Steam account, start Valheim, or contact a server.
#>
[CmdletBinding()]
param(
    [string] $ValheimDir = (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Valheim')
)

$ErrorActionPreference = 'Stop'
$packageRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$manifestPath = Join-Path $packageRoot 'install-manifest.json'
if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
    throw 'install-manifest.json is missing from the player package.'
}
if (Get-Process -Name valheim -ErrorAction SilentlyContinue) {
    throw 'Close Valheim before installing CreatorOS Beta 1.'
}
$valheim = [IO.Path]::GetFullPath($ValheimDir)
$bepInEx = Join-Path $valheim 'BepInEx'
if (-not (Test-Path -LiteralPath (Join-Path $bepInEx 'core\BepInEx.dll') -PathType Leaf)) {
    throw "An existing BepInEx installation was not found under $valheim."
}
$manifest = Get-Content -LiteralPath $manifestPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$manifest.schema -ne 'creatoros-beta-install-manifest/v1' -or
    [string]$manifest.release_id -ne 'creatoros-beta1') {
    throw 'The install manifest has an unsupported identity.'
}
$rootPrefix = $valheim.TrimEnd('\') + '\'
$stamp = (Get-Date).ToUniversalTime().ToString('yyyyMMddTHHmmssfffZ')
$backupRoot = Join-Path $bepInEx ("creatoros-backups\$stamp")
$installed = @()

foreach ($file in @($manifest.files)) {
    $relativeSource = [string]$file.source
    $relativeTarget = [string]$file.target
    if ([IO.Path]::IsPathRooted($relativeSource) -or [IO.Path]::IsPathRooted($relativeTarget) -or
        $relativeSource -match '(^|[\\/])\.\.([\\/]|$)' -or
        $relativeTarget -match '(^|[\\/])\.\.([\\/]|$)') {
        throw "Unsafe install path in manifest: $relativeTarget"
    }
    $source = [IO.Path]::GetFullPath((Join-Path $packageRoot $relativeSource))
    $target = [IO.Path]::GetFullPath((Join-Path $valheim $relativeTarget))
    if (-not $target.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Install target escaped the Valheim directory: $relativeTarget"
    }
    if (-not (Test-Path -LiteralPath $source -PathType Leaf)) {
        throw "Package payload is missing: $relativeSource"
    }
    $sourceHash = (Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($sourceHash -ne ([string]$file.sha256).ToLowerInvariant()) {
        throw "Package payload hash mismatch: $relativeSource"
    }
    $targetDirectory = Split-Path -Parent $target
    New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    $backup = $null
    if (Test-Path -LiteralPath $target -PathType Leaf) {
        $backup = Join-Path $backupRoot $relativeTarget
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backup) | Out-Null
        Copy-Item -LiteralPath $target -Destination $backup
    }
    $temporary = $target + '.creatoros-new-' + [Guid]::NewGuid().ToString('N')
    try {
        Copy-Item -LiteralPath $source -Destination $temporary
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash.ToLowerInvariant() -ne $sourceHash) {
            throw "Staged install hash mismatch: $relativeTarget"
        }
        Move-Item -LiteralPath $temporary -Destination $target -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
    $installed += [ordered]@{
        target = $relativeTarget.Replace('\', '/')
        sha256 = $sourceHash
        backup = if ($backup) { $backup } else { $null }
    }
}

$receipt = [ordered]@{
    schema = 'creatoros-beta-install-receipt/v1'
    release_id = [string]$manifest.release_id
    player_label = [string]$manifest.player_label
    installed_utc = (Get-Date).ToUniversalTime().ToString('o')
    valheim_dir = $valheim
    world_name = [string]$manifest.world_name
    world_uid = [string]$manifest.world_uid
    pack_content_hash = [string]$manifest.pack_content_hash
    files = $installed
}
$receiptDirectory = Join-Path $bepInEx 'config\creatoros-beta1\receipts'
New-Item -ItemType Directory -Force -Path $receiptDirectory | Out-Null
$receiptPath = Join-Path $receiptDirectory ("install-$stamp.json")
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText($receiptPath, ($receipt | ConvertTo-Json -Depth 8) + [Environment]::NewLine, $utf8)
Write-Output $receiptPath
