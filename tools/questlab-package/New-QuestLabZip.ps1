#Requires -Version 5.1
<#
.SYNOPSIS
Build the self-contained Quest Lab creator zip.

.DESCRIPTION
Builds the reviewed plugin, stages only explicit public files, runs the privacy gate,
and emits a zip plus SHA256/byte receipt. No platform or telemetry sources are read.
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [string]$RepoBlobBase = 'https://github.com/djcdevelopment/comfy-quest/blob/main/'
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $root 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }
if (-not $OutDir) { $OutDir = Join-Path $PSScriptRoot 'dist' }
$staging = Join-Path ([IO.Path]::GetTempPath()) ('quest-lab-zip-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Path $staging | Out-Null
Write-Host "staging: $staging"

function Add-ZipFromDirectory {
    param([string]$Source, [string]$Destination)
    Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem
    $stream = [IO.File]::Open($Destination, [IO.FileMode]::CreateNew)
    try {
        $archive = New-Object IO.Compression.ZipArchive($stream, [IO.Compression.ZipArchiveMode]::Create)
        try {
            Get-ChildItem -LiteralPath $Source -Recurse -File | ForEach-Object {
                $relative = $_.FullName.Substring($Source.Length + 1) -replace '\\', '/'
                [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                    $archive, $_.FullName, $relative) | Out-Null
            }
        }
        finally { $archive.Dispose() }
    }
    finally { $stream.Dispose() }
}

try {
    Push-Location (Join-Path $root 'network\mod\ComfyQuestLab')
    try {
        & dotnet build -c Release
        if ($LASTEXITCODE -ne 0) { throw "dotnet build failed with $LASTEXITCODE" }
    }
    finally { Pop-Location }

    $dll = Join-Path $root 'network\mod\ComfyQuestLab\bin\Release\ComfyQuestLab.dll'
    if (-not (Test-Path -LiteralPath $dll)) { throw "Quest Lab DLL not found: $dll" }
    Copy-Item -LiteralPath $dll -Destination $staging

    $readme = Join-Path $root 'network\mod\ComfyQuestLab\README.md'
    $packageReadme = Get-Content -LiteralPath $readme -Raw
    $repoLinkPrefix = '../' + '../' + '../'
    $readmeLinks = [ordered]@{}
    $readmeLinks[$repoLinkPrefix + 'tools/questlab-sheets/Start-QuestLabSheets.ps1'] = 'questlab-sheets/Start-QuestLabSheets.ps1'
    $readmeLinks[$repoLinkPrefix + 'tools/questlab-sheets/README.md'] = 'questlab-sheets/README.md'
    $readmeLinks[$repoLinkPrefix + 'tools/component-packets/EVENT-ATLAS.md'] = $RepoBlobBase + 'tools/component-packets/EVENT-ATLAS.md'
    $readmeLinks[$repoLinkPrefix + 'tools/component-packets/samples/gallery-profiles.json'] = $RepoBlobBase + 'tools/component-packets/samples/gallery-profiles.json'
    $readmeLinks[$repoLinkPrefix + 'tools/questlab-batch/Invoke-I5QuestLabBatch.ps1'] = $RepoBlobBase + 'tools/questlab-batch/Invoke-I5QuestLabBatch.ps1'
    $readmeLinks[$repoLinkPrefix + 'tools/component-packets/quest-event-authoring.json'] = $RepoBlobBase + 'tools/component-packets/quest-event-authoring.json'
    $readmeLinks[$repoLinkPrefix + 'tools/quest-packs/README.md'] = $RepoBlobBase + 'tools/quest-packs/README.md'
    $readmeLinks[$repoLinkPrefix + 'docs/legal/LICENSING.md'] = $RepoBlobBase + 'LICENSE'
    $readmeLinks['Patches/HarvestPatches.cs'] = $RepoBlobBase + 'network/mod/ComfyQuestLab/Patches/HarvestPatches.cs'
    foreach ($link in $readmeLinks.GetEnumerator()) {
        $packageReadme = $packageReadme.Replace([string]$link.Key, [string]$link.Value)
    }
    if ($packageReadme.Contains($repoLinkPrefix)) {
        throw 'Quest Lab package README retains a repository-relative link'
    }
    [IO.File]::WriteAllText(
        (Join-Path $staging 'README.md'), $packageReadme, (New-Object Text.UTF8Encoding($false)))

    $config = Join-Path $root 'network\mod\ComfyQuestLab\djcdevelopment.valheim.comfyquestlab.cfg'
    Copy-Item -LiteralPath $config -Destination $staging

    # explicit file allowlist: no credentials, tokens, caches, or local receipts can hitch a ride.
    $sheetsSource = Join-Path $root 'tools\questlab-sheets'
    $sheetsStage = Join-Path $staging 'questlab-sheets'
    New-Item -ItemType Directory -Path $sheetsStage | Out-Null
    @('questlab_sheets.py', 'Start-QuestLabSheets.ps1', 'requirements-google.txt', 'README.md') |
        ForEach-Object { Copy-Item (Join-Path $sheetsSource $_) -Destination $sheetsStage }

    $eventsSource = Join-Path $root 'tools\questlab-events'
    $eventsStage = Join-Path $staging 'questlab-events'
    New-Item -ItemType Directory -Path $eventsStage | Out-Null
    @('questlab_events.py', 'README.md') |
        ForEach-Object { Copy-Item (Join-Path $eventsSource $_) -Destination $eventsStage }

    @(
        'questlab-sheets\Start-QuestLabSheets.ps1',
        'questlab-sheets\README.md',
        'questlab-events\README.md'
    ) | ForEach-Object {
        if (-not (Test-Path -LiteralPath (Join-Path $staging $_))) {
            throw "Quest Lab package README target is missing: $_"
        }
    }
    [regex]::Matches($packageReadme, '\]\((?!https?://|#)([^)#]+)(?:#[^)]*)?\)') |
        ForEach-Object {
            $relativeTarget = $_.Groups[1].Value -replace '/', '\'
            if (-not (Test-Path -LiteralPath (Join-Path $staging $relativeTarget))) {
                throw "Quest Lab package README has a broken local link: $relativeTarget"
            }
        }

    $pluginManifest = Get-Content (
        Join-Path $root 'network\mod\ComfyQuestLab\manifest.json') -Raw | ConvertFrom-Json
    $pluginSource = Get-Content (
        Join-Path $root 'network\mod\ComfyQuestLab\ComfyQuestLab.cs') -Raw
    $releaseMatch = [regex]::Match($pluginSource, 'public const string ReleaseId = "([^"]+)";')
    if (-not $releaseMatch.Success) { throw 'Quest Lab ReleaseId was not found.' }

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
        tool = 'quest-lab'
        version = [string]$pluginManifest.version_number
        release_id = $releaseMatch.Groups[1].Value
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
    $zipPath = Join-Path $OutDir 'quest-lab.zip'
    if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
    Add-ZipFromDirectory -Source $staging -Destination $zipPath
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
