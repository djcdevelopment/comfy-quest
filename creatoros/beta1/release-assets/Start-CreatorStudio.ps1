#Requires -Version 5.1
[CmdletBinding()]
param([int] $Port = 8085)

$ErrorActionPreference = 'Stop'
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Port must be between 1024 and 65535.' }
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$hostPath = Join-Path $root 'studio\Comfy.Quest.Studio.Host.exe'
if (-not (Test-Path -LiteralPath $hostPath -PathType Leaf)) {
    throw 'The packaged Quest Studio host is missing.'
}
$state = Join-Path $env:LOCALAPPDATA 'ComfyQuest\CreatorOSBeta1'
New-Item -ItemType Directory -Force -Path $state | Out-Null
$environment = @{
    COMFY_QUEST_STUDIO_STATE = $state
    COMFY_QUEST_STUDIO_PORT = [string]$Port
}
foreach ($entry in $environment.GetEnumerator()) {
    [Environment]::SetEnvironmentVariable($entry.Key, $entry.Value, 'Process')
}
Start-Process -FilePath $hostPath -ArgumentList @('--port', [string]$Port) `
    -WorkingDirectory (Split-Path -Parent $hostPath) -WindowStyle Hidden | Out-Null
$health = "http://127.0.0.1:$Port/health"
$ready = $false
for ($attempt = 0; $attempt -lt 40; $attempt++) {
    try {
        $result = Invoke-RestMethod -Uri $health -TimeoutSec 2
        if ($result.status -eq 'ok') { $ready = $true; break }
    }
    catch { Start-Sleep -Milliseconds 250 }
}
if (-not $ready) { throw 'Quest Studio did not become ready on loopback.' }
Start-Process "http://127.0.0.1:$Port/quest-studio" | Out-Null
