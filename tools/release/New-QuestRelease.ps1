#Requires -Version 5.1
<#
.SYNOPSIS
Build and verify the four public Quest release assets.

.DESCRIPTION
Creates the split-proof Quest release bundle locally. This script never creates a
Git tag, GitHub release, or cloud build; publication remains a separate operator
action after the generated assets and manifest have been reviewed.
#>
[CmdletBinding()]
param(
    [string]$ReleaseTag = 'quest-v0.2.0-split-proof',
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path

function Invoke-Checked {
    param(
        [Parameter(Mandatory = $true)][string]$Program,
        [Parameter(Mandatory = $true)][string[]]$Arguments
    )
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Program exited with $LASTEXITCODE"
    }
}

function Get-Sha256Bytes {
    param([Parameter(Mandatory = $true)][byte[]]$Bytes)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($Bytes))).Replace('-', '').ToLowerInvariant()
    }
    finally {
        $sha.Dispose()
    }
}

function Get-ArtifactRecord {
    param([Parameter(Mandatory = $true)][string]$Path)
    $item = Get-Item -LiteralPath $Path
    return [ordered]@{
        name = $item.Name
        sha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $item.FullName).Hash.ToLowerInvariant()
        bytes = $item.Length
    }
}

& (Join-Path $root 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }

$match = [regex]::Match($ReleaseTag, '^quest-v([0-9]+\.[0-9]+\.[0-9]+)-split-proof$')
if (-not $match.Success) {
    throw 'ReleaseTag must be quest-v<stable-semver>-split-proof.'
}
$version = $match.Groups[1].Value
if (-not $OutDir) {
    $OutDir = Join-Path $root ('artifacts\releases\' + $ReleaseTag)
}
if (-not [IO.Path]::IsPathRooted($OutDir)) {
    $OutDir = Join-Path $root $OutDir
}
$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path -LiteralPath $OutDir) {
    $existing = @(Get-ChildItem -LiteralPath $OutDir -Force)
    if ($existing.Count -ne 0) {
        throw "Output directory must be absent or empty: $OutDir"
    }
}
else {
    New-Item -ItemType Directory -Path $OutDir | Out-Null
}

$dirty = (& git -C $root status --porcelain=v1)
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect repository status.' }
if ($dirty) {
    throw 'Release build requires a clean checkout so revision and generated assets are auditable.'
}
$revision = (& git -C $root rev-parse HEAD).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-f]{40}$') {
    throw 'Unable to resolve a full lowercase release revision.'
}

$pluginManifestPath = Join-Path $root 'network\mod\ComfyQuestLab\manifest.json'
$pluginManifest = Get-Content -LiteralPath $pluginManifestPath -Raw | ConvertFrom-Json
if ([string]$pluginManifest.version_number -ne $version) {
    throw "Quest Lab manifest version $($pluginManifest.version_number) does not match $version."
}
$pluginSource = Get-Content -LiteralPath (
    Join-Path $root 'network\mod\ComfyQuestLab\ComfyQuestLab.cs') -Raw
$releaseMatch = [regex]::Match($pluginSource, 'public const string ReleaseId = "([^"]+)";')
if (-not $releaseMatch.Success -or $releaseMatch.Groups[1].Value -eq 'dev') {
    throw 'Quest Lab ReleaseId is missing or unbaked.'
}
$releaseId = $releaseMatch.Groups[1].Value

$generatedQuestLab = Join-Path $root 'docs\generated\questlab.html'
$beforeHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $generatedQuestLab).Hash
Invoke-Checked -Program 'python' -Arguments @(
    (Join-Path $root 'tools\component-packets\render_quest_lab.py')
)
Invoke-Checked -Program 'python' -Arguments @(
    (Join-Path $root 'tools\component-packets\render_quest_lab.py'),
    '--check'
)
$afterHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $generatedQuestLab).Hash
if ($beforeHash -ne $afterHash) {
    throw 'docs/generated/questlab.html was stale; commit the regenerated file before building.'
}
& git -C $root diff --quiet -- 'docs/generated/questlab.html'
if ($LASTEXITCODE -ne 0) {
    throw 'docs/generated/questlab.html differs from the committed release revision.'
}

$packageDir = Join-Path $OutDir '.packages'
New-Item -ItemType Directory -Path $packageDir | Out-Null
Invoke-Checked -Program 'powershell.exe' -Arguments @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', (Join-Path $root 'tools\questlab-package\New-QuestLabZip.ps1'),
    '-OutDir', $packageDir,
    '-RepoBlobBase', ('https://github.com/djcdevelopment/comfy-quest/blob/' + $ReleaseTag + '/')
)
Invoke-Checked -Program 'powershell.exe' -Arguments @(
    '-NoProfile',
    '-ExecutionPolicy', 'Bypass',
    '-File', (Join-Path $root 'tools\questlab-package\New-QuestPickerZip.ps1'),
    '-OutDir', $packageDir
)

$questLabZip = Join-Path $OutDir 'quest-lab.zip'
$pickerZip = Join-Path $OutDir 'quest-picker.zip'
Move-Item -LiteralPath (Join-Path $packageDir 'quest-lab.zip') -Destination $questLabZip
Move-Item -LiteralPath (Join-Path $packageDir 'quest-picker.zip') -Destination $pickerZip
Remove-Item -LiteralPath $packageDir

$questLabHtml = Join-Path $OutDir 'questlab.html'
$pickerHtml = Join-Path $OutDir 'quest-picker.html'
Copy-Item -LiteralPath $generatedQuestLab -Destination $questLabHtml

Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
$pickerArchive = [IO.Compression.ZipFile]::OpenRead($pickerZip)
try {
    $pickerEntries = @($pickerArchive.Entries | Where-Object { $_.FullName -eq 'quest-picker.html' })
    if ($pickerEntries.Count -ne 1) {
        throw 'quest-picker.zip must contain exactly one quest-picker.html.'
    }
    [IO.Compression.ZipFileExtensions]::ExtractToFile($pickerEntries[0], $pickerHtml, $false)
}
finally {
    $pickerArchive.Dispose()
}

$labArchive = [IO.Compression.ZipFile]::OpenRead($questLabZip)
try {
    $manifestEntries = @($labArchive.Entries | Where-Object { $_.FullName -eq 'manifest.json' })
    $dllEntries = @($labArchive.Entries | Where-Object { $_.FullName -eq 'ComfyQuestLab.dll' })
    if ($manifestEntries.Count -ne 1 -or $dllEntries.Count -ne 1) {
        throw 'quest-lab.zip must contain one manifest.json and one ComfyQuestLab.dll.'
    }
    $reader = New-Object IO.StreamReader($manifestEntries[0].Open(), [Text.Encoding]::UTF8)
    try {
        $packageManifest = $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
    }
    $dllStream = $dllEntries[0].Open()
    $memory = New-Object IO.MemoryStream
    try {
        $dllStream.CopyTo($memory)
        $dllBytes = $memory.ToArray()
    }
    finally {
        $memory.Dispose()
        $dllStream.Dispose()
    }
}
finally {
    $labArchive.Dispose()
}

if (
    [string]$packageManifest.schema -ne 'comfy-quest-package/v1' -or
    [string]$packageManifest.tool -ne 'quest-lab' -or
    [string]$packageManifest.version -ne $version -or
    [string]$packageManifest.release_id -ne $releaseId
) {
    throw 'Quest Lab ZIP manifest identity does not match the release identity.'
}

$assetNames = @('questlab.html', 'quest-lab.zip', 'quest-picker.html', 'quest-picker.zip')
$records = @()
foreach ($name in $assetNames) {
    $records += Get-ArtifactRecord -Path (Join-Path $OutDir $name)
}
$releaseManifest = [ordered]@{
    schema = 'comfy-quest-release-manifest/v1'
    repository = 'djcdevelopment/comfy-quest'
    release_tag = $ReleaseTag
    revision = $revision
    version = $version
    quest_lab = [ordered]@{
        package_schema = [string]$packageManifest.schema
        plugin_version = [string]$packageManifest.version
        release_id = [string]$packageManifest.release_id
        dll_sha256 = Get-Sha256Bytes -Bytes $dllBytes
        dll_bytes = $dllBytes.Length
    }
    artifacts = $records
}
$utf8 = New-Object Text.UTF8Encoding($false)
[IO.File]::WriteAllText(
    (Join-Path $OutDir 'release-manifest.json'),
    ($releaseManifest | ConvertTo-Json -Depth 8) + [Environment]::NewLine,
    $utf8)
$sumLines = @(
    $records | ForEach-Object { [string]$_.sha256 + '  ' + [string]$_.name }
)
[IO.File]::WriteAllText(
    (Join-Path $OutDir 'SHA256SUMS'),
    ($sumLines -join [Environment]::NewLine) + [Environment]::NewLine,
    $utf8)

Invoke-Checked -Program 'python' -Arguments @(
    (Join-Path $root 'tools\release\verify_quest_release.py'),
    '--release-dir', $OutDir,
    '--expected-tag', $ReleaseTag,
    '--expected-questlab', $generatedQuestLab,
    '--expected-revision', $revision
)

Write-Host "BUILT AND VERIFIED $ReleaseTag"
Write-Host "revision $revision"
Write-Host "output   $OutDir"
