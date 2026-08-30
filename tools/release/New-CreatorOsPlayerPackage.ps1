#Requires -Version 5.1
<#
.SYNOPSIS
Create one deterministic, credential-free CreatorOS Beta 1 player ZIP.

.DESCRIPTION
The player label is a short support pseudonym, never a Steam ID. Enrollment remains a separate
one-time link so a leaked ZIP contains neither an invite nor a standing access credential.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $ReleaseDir,
    [Parameter(Mandatory = $true)][string] $PlayerLabel,
    [string] $OutDir,
    [switch] $AllowCandidate
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $root 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }
$release = (Resolve-Path -LiteralPath $ReleaseDir).Path
if (-not $OutDir) { $OutDir = Join-Path (Split-Path -Parent $release) 'player-packages' }
$outputRoot = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $outputRoot | Out-Null
if ($PlayerLabel -notmatch '^[A-Za-z0-9][A-Za-z0-9._-]{0,31}$' -or
    $PlayerLabel -match '^7656119[0-9]{10}$') {
    throw "PlayerLabel must be a 1-32 character pseudonym, not a Steam numeric ID."
}
$manifestBytes = [IO.File]::ReadAllBytes((Join-Path $release 'release-manifest.json'))
$sha = [Security.Cryptography.SHA256]::Create()
try { $manifestHash = ([BitConverter]::ToString($sha.ComputeHash($manifestBytes))).Replace('-', '').ToLowerInvariant() }
finally { $sha.Dispose() }
$output = Join-Path $outputRoot ("CreatorOSBeta1-{0}-{1}.zip" -f $PlayerLabel, $manifestHash.Substring(0, 12))
if (Test-Path -LiteralPath $output) { throw "Player package already exists: $output" }
$arguments = @(
    (Join-Path $root 'tools\release\creatoros_player_package.py'), 'build',
    '--release-dir', $release, '--player-label', $PlayerLabel, '--output', $output
)
if ($AllowCandidate) { $arguments += '--allow-candidate' }
& python @arguments
if ($LASTEXITCODE -ne 0) { throw 'CreatorOS player package build failed.' }
Write-Output $output
