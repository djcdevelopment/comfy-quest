#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'

function Get-Sha256([string]$Path) {
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
        try {
            return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-', '').ToLowerInvariant()
        } finally { $stream.Dispose() }
    } finally { $sha.Dispose() }
}

function New-DeterministicZip([string]$SourceDirectory, [string]$Destination) {
    Add-Type -AssemblyName System.IO.Compression
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    try {
        $archive = [IO.Compression.ZipArchive]::new(
            $stream, [IO.Compression.ZipArchiveMode]::Create, $false)
        try {
            $fixedTime = [DateTimeOffset]::new(1980, 1, 1, 0, 0, 0, [TimeSpan]::Zero)
            foreach ($file in @(Get-ChildItem -LiteralPath $SourceDirectory -File | Sort-Object Name)) {
                $entry = $archive.CreateEntry($file.Name, [IO.Compression.CompressionLevel]::Optimal)
                $entry.LastWriteTime = $fixedTime
                $input = [IO.File]::Open($file.FullName, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
                $output = $entry.Open()
                try { $input.CopyTo($output) }
                finally { $output.Dispose(); $input.Dispose() }
            }
        } finally { $archive.Dispose() }
    } finally { $stream.Dispose() }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }

if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts\quest-workbench-provider'
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$artifactRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'artifacts'))
if (-not ($outputRoot -eq $artifactRoot -or $outputRoot.StartsWith($artifactRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase))) {
    throw 'Workbench provider output must remain under this repository artifacts root.'
}

$source = Join-Path $PSScriptRoot 'comfy_quest_workbench.py'
$sourceHash = Get-Sha256 $source
$revision = (& git -C $repoRoot rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-f]{40}$') { throw 'Could not resolve source revision.' }
$dirty = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=no)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect source state.' }

New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
$staging = Join-Path $outputRoot ('staging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging | Out-Null
$archive = Join-Path $outputRoot 'comfy-quest-workbench-provider.zip'
$manifestPath = Join-Path $outputRoot 'comfy-quest-workbench-provider.manifest.json'
try {
    Copy-Item -LiteralPath $source -Destination (Join-Path $staging 'comfy_quest_workbench.py')
    $manifest = [ordered]@{
        schema = 'comfy-quest-workbench-provider-release/v1'
        module = 'comfy_quest_workbench'
        provider_sha256 = $sourceHash
        source_revision = $revision
        source_dirty = [bool]($dirty.Count -gt 0)
        runtime_root_env = 'COMFY_QUEST_RUNTIME_ROOT'
        lab_root_env = 'COMFY_QUEST_LAB_ROOT'
        reviewed_godbuild_root_env = 'COMFY_QUEST_REVIEWED_GODBUILD_ROOT'
        tools = @(
            'quest_runtime_status',
            'quest_runtime_receipts',
            'quest_runtime_creator_request',
            'quest_runtime_run_control',
            'quest_lab_replay')
    }
    $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $staging 'provider-manifest.json') -Encoding UTF8
    if (Test-Path -LiteralPath $archive) { Remove-Item -LiteralPath $archive -Force }
    New-DeterministicZip $staging $archive
    $archiveHash = Get-Sha256 $archive
    $release = [ordered]@{
        schema = 'comfy-quest-workbench-provider-artifact/v1'
        archive = [IO.Path]::GetFileName($archive)
        archive_sha256 = $archiveHash
        archive_bytes = (Get-Item -LiteralPath $archive).Length
        provider_sha256 = $sourceHash
        source_revision = $revision
        source_dirty = [bool]($dirty.Count -gt 0)
    }
    $release | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8
} finally {
    $resolvedStaging = [IO.Path]::GetFullPath($staging)
    if (-not $resolvedStaging.StartsWith($outputRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Refusing to remove an unexpected provider staging path.'
    }
    if (Test-Path -LiteralPath $resolvedStaging) { Remove-Item -LiteralPath $resolvedStaging -Recurse -Force }
}

Get-Content -Raw -LiteralPath $manifestPath
