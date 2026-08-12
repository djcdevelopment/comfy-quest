#Requires -Version 5.1
[CmdletBinding()]
param([switch] $Check)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script = Join-Path $PSScriptRoot 'build_live_test_pack.py'
if ($Check) {
    & python $script --check
    if ($LASTEXITCODE -ne 0) { throw 'Live-test pack drift check failed.' }
    exit 0
}

& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }
$env:COMFY_QUEST_RUNTIME_PACK_WRITE = '1'
try {
    & python $script
    if ($LASTEXITCODE -ne 0) { throw 'Live-test pack build failed.' }
} finally {
    Remove-Item Env:\COMFY_QUEST_RUNTIME_PACK_WRITE -ErrorAction SilentlyContinue
}
