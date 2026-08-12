#Requires -Version 5.1
<#
.SYNOPSIS
Check the optional i5 lane once, using key-only SSH.
#>
[CmdletBinding()]
param([string]$SshAlias = 'i5')

$ErrorActionPreference = 'Continue'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { exit 1 }

$configuration = @(ssh -G $SshAlias 2>$null)
$hostLine = $configuration | Where-Object { $_ -match '^hostname\s+' } | Select-Object -First 1
if ($LASTEXITCODE -ne 0 -or -not $hostLine) {
    Write-Host "[FAIL] SSH alias '$SshAlias' does not resolve."
    exit 1
}
$sshHost = ($hostLine -replace '^hostname\s+', '').Trim()
if (-not (Test-NetConnection $sshHost -Port 22 -InformationLevel Quiet -WarningAction SilentlyContinue)) {
    Write-Host "[FAIL] i5 lane offline at $sshHost (normal for this roaming laptop; do not retry-loop)."
    exit 1
}
$who = ssh -o BatchMode=yes -o ConnectTimeout=8 $SshAlias 'whoami' 2>$null
if ($LASTEXITCODE -ne 0) {
    Write-Host '[FAIL] i5 did not accept key authentication; password fallback is disabled.'
    exit 1
}
Write-Host "[PASS] i5 lane up; remote user: $who"
exit 0
