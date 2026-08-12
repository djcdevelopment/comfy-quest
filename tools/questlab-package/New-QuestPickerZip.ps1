#Requires -Version 5.1
<#
.SYNOPSIS
Build the synthetic Quest Picker starter zip.
#>
[CmdletBinding()]
param([string]$OutDir)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $root 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'dist' }
$staging = Join-Path ([IO.Path]::GetTempPath()) ('quest-picker-zip-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $staging | Out-Null

function Invoke-Python {
    param([string]$WorkDir, [string[]]$Arguments)
    Push-Location $WorkDir
    try {
        & python @Arguments
        if ($LASTEXITCODE -ne 0) { throw "python exited $LASTEXITCODE" }
    }
    finally { Pop-Location }
}

try {
    $catalogStage = Join-Path $staging 'quest-catalogs'
    $sampleStage = Join-Path $staging 'sample'
    New-Item -ItemType Directory -Path $catalogStage, $sampleStage | Out-Null
    @('harvest.py', 'render_quest_picker.py', 'validate.py', 'schema.md', 'quest-view-schema.md') |
        ForEach-Object {
            Copy-Item (Join-Path $root "recipes\quest-catalogs\$_") -Destination $catalogStage
        }
    $exampleView = Join-Path $root 'recipes\quest-catalogs\example-quest-view.json'
    if (Test-Path -LiteralPath $exampleView) { Copy-Item $exampleView -Destination $catalogStage }

    $samples = Join-Path $PSScriptRoot 'samples\quest-picker'
    Copy-Item (Join-Path $samples 'sources.sample.json') -Destination (
        Join-Path $catalogStage 'sources.json')
    Copy-Item (Join-Path $samples 'make_sample_tracker.py') -Destination $sampleStage
    Copy-Item (Join-Path $samples 'RUN-ME.md') -Destination $staging

    Invoke-Python -WorkDir $sampleStage -Arguments @('make_sample_tracker.py')
    Invoke-Python -WorkDir $staging -Arguments @(
        'quest-catalogs/harvest.py', 'sample-guild')
    Invoke-Python -WorkDir $staging -Arguments @(
        'quest-catalogs/render_quest_picker.py',
        'quest-picker.html',
        'sample/quest-catalog-sample.json')
    $marker = [Environment]::NewLine +
        '<!-- SAMPLE-CATALOG: synthetic demonstration data only -->' +
        [Environment]::NewLine
    [IO.File]::AppendAllText(
        (Join-Path $staging 'quest-picker.html'),
        $marker,
        (New-Object Text.UTF8Encoding($false)))

    $entries = @()
    Get-ChildItem -LiteralPath $staging -Recurse -File | ForEach-Object {
        $entries += [ordered]@{
            path = $_.FullName.Substring($staging.Length + 1) -replace '\\', '/'
            sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $_.FullName).Hash.ToLowerInvariant()
            bytes = $_.Length
        }
    }
    $manifest = [ordered]@{
        schema = 'comfy-quest-package/v1'
        tool = 'quest-picker'
        files = $entries
    }
    [IO.File]::WriteAllText(
        (Join-Path $staging 'manifest.json'),
        ($manifest | ConvertTo-Json -Depth 6) + [Environment]::NewLine,
        (New-Object Text.UTF8Encoding($false)))

    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File (
        Join-Path $PSScriptRoot 'Test-PackagePrivacy.ps1') -Path $staging
    if ($LASTEXITCODE -ne 0) { throw 'privacy scan failed; zip was not built' }

    New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
    $zipPath = Join-Path $OutDir 'quest-picker.zip'
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $stream = [IO.File]::Open($zipPath, [IO.FileMode]::CreateNew)
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            Get-ChildItem -LiteralPath $staging -Recurse -File | ForEach-Object {
                $relative = $_.FullName.Substring($staging.Length + 1) -replace '\\', '/'
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive, $_.FullName, $relative) | Out-Null
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }

    $hash = (Get-FileHash -Algorithm SHA256 -LiteralPath $zipPath).Hash.ToLowerInvariant()
    $size = (Get-Item -LiteralPath $zipPath).Length
    Write-Host "BUILT  $zipPath"
    Write-Host "sha256 $hash"
    Write-Host "bytes  $size"
    Remove-Item -LiteralPath $staging -Recurse -Force
}
catch {
    Write-Host "FAILED: $_" -ForegroundColor Red
    Write-Host "staging retained: $staging"
    exit 1
}
