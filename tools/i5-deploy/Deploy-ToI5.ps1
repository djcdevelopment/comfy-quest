#Requires -Version 5.1
<#
.SYNOPSIS
Deploy Quest files to the i5 over key-only SSH and verify every byte by SHA256.

.DESCRIPTION
This is the Quest-owned copy of the generic i5 file lane. It has no dependency on a
sibling checkout. The i5 is a roaming laptop: an unavailable lane is reported once
and never retried or downgraded to password authentication.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string[]]$Path,
    [string]$Dest = 'C:/deploy/comfy-quest',
    [switch]$ValheimPlugins,
    [switch]$ValheimConfig,
    [switch]$DryRun,
    [string[]]$ExcludeDirectoryName = @()
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }

$sshAlias = 'i5'
$sshOptions = @('-o', 'BatchMode=yes', '-o', 'ConnectTimeout=8')
if ($ValheimPlugins) {
    $Dest = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/plugins'
}
if ($ValheimConfig) {
    $Dest = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/config'
}
$Dest = ($Dest -replace '\\', '/').TrimEnd('/')

$topLevel = @($Path | ForEach-Object { Get-Item -LiteralPath $_ -ErrorAction Stop })
$duplicates = @($topLevel | Group-Object Name | Where-Object Count -gt 1)
if ($duplicates.Count) {
    throw "Duplicate top-level names would overwrite each other remotely: $(($duplicates.Name) -join ', ')"
}

$manifest = @()
foreach ($item in $topLevel) {
    if ($item.PSIsContainer) {
        $files = @(Get-ChildItem -LiteralPath $item.FullName -Recurse -File | Where-Object {
            $relativeDirectory = $_.DirectoryName.Substring($item.FullName.Length).TrimStart('\', '/')
            $segments = @($relativeDirectory -split '[\\/]' | Where-Object { $_ })
            @($segments | Where-Object { $_ -in $ExcludeDirectoryName }).Count -eq 0
        })
        if (-not $files.Count) { throw "Directory has no deployable files: $($item.FullName)" }
        foreach ($file in $files) {
            $relative = $file.FullName.Substring($item.FullName.Length).TrimStart('\', '/') -replace '\\', '/'
            $manifest += [pscustomobject]@{ Local = $file.FullName; Remote = "$Dest/$($item.Name)/$relative" }
        }
    }
    else {
        $manifest += [pscustomobject]@{ Local = $item.FullName; Remote = "$Dest/$($item.Name)" }
    }
}

Write-Host ("deploy -> {0}:{1} [{2} file(s)]" -f $sshAlias, $Dest, $manifest.Count)
$manifest | ForEach-Object { Write-Host ("  {0}" -f $_.Remote) }
if ($DryRun) { Write-Host 'dry run - nothing copied'; exit 0 }

$null = ssh @sshOptions $sshAlias 'whoami' 2>$null
if ($LASTEXITCODE -ne 0) {
    throw 'i5 ssh lane unavailable (offline is normal; report and stop without retrying).'
}

foreach ($entry in $manifest) {
    $parent = Split-Path -Parent $entry.Remote
    $escapedParent = $parent.Replace("'", "''")
    $mkdir = "New-Item -ItemType Directory -Force -Path '$escapedParent' | Out-Null"
    $encodedMkdir = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($mkdir))
    $null = ssh @sshOptions $sshAlias "powershell.exe -NoProfile -EncodedCommand $encodedMkdir" 2>$null
    if ($LASTEXITCODE -ne 0) { throw "Could not create remote directory: $parent" }

    $scpOptions = @('-q') + $sshOptions
    $remoteTarget = $sshAlias + ':' + $entry.Remote
    & scp @scpOptions $entry.Local $remoteTarget
    if ($LASTEXITCODE -ne 0) { throw "scp failed for $($entry.Local)" }

    $escapedRemote = $entry.Remote.Replace("'", "''")
    $hashScript = "if(Test-Path -LiteralPath '$escapedRemote'){(Get-FileHash -Algorithm SHA256 -LiteralPath '$escapedRemote').Hash}else{'MISSING'}"
    $encodedHash = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($hashScript))
    $remoteHash = @(ssh @sshOptions $sshAlias "powershell.exe -NoProfile -EncodedCommand $encodedHash" 2>$null) | Select-Object -Last 1
    if ($LASTEXITCODE -ne 0) { throw "Remote hash verification failed for $($entry.Remote)" }
    $localHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $entry.Local).Hash
    if ($localHash -ne $remoteHash) {
        throw "Deployment hash mismatch for $($entry.Remote): local=$localHash remote=$remoteHash"
    }
    Write-Host ("  OK {0} sha256:{1}" -f $entry.Remote, $localHash.Substring(0, 12).ToLowerInvariant())
}

Write-Host ("deploy verified: {0}/{0} file(s) match on the i5" -f $manifest.Count)
exit 0
