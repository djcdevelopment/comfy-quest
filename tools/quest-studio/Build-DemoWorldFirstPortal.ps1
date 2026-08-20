#Requires -Version 5.1
[CmdletBinding()]
param([switch] $Check)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$builder = Join-Path $PSScriptRoot 'build_demo_world_first_portal.py'
if ($Check) {
    & python $builder --check
    if ($LASTEXITCODE -ne 0) { throw 'Demo World: First Portal artifact drift check failed.' }
    exit 0
}

& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }
$env:COMFY_QUEST_DEMO_WORLD_WRITE = '1'
try {
    & python $builder
    if ($LASTEXITCODE -ne 0) { throw 'Demo World: First Portal artifact build failed.' }
} finally {
    Remove-Item Env:\COMFY_QUEST_DEMO_WORLD_WRITE -ErrorAction SilentlyContinue
}
