#Requires -Version 5.1
[CmdletBinding()]
param([switch] $Check)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$script = Join-Path $PSScriptRoot 'build_creatoros_beta1.py'

if ($Check) {
    & python $script --check
    if ($LASTEXITCODE -ne 0) { throw 'CreatorOS Beta 1 generated artifact drift.' }
    exit 0
}

& (Join-Path $root 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }
$env:COMFY_CREATOROS_BETA1_WRITE = '1'
try {
    & python $script
    if ($LASTEXITCODE -ne 0) { throw 'CreatorOS Beta 1 generation failed.' }
}
finally {
    Remove-Item Env:\COMFY_CREATOROS_BETA1_WRITE -ErrorAction SilentlyContinue
}
