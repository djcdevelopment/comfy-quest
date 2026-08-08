<#
.SYNOPSIS
Send one bounded Quest Lab suite/gallery request to i5 and collect its machine receipt.

.DESCRIPTION
Builds the fixed comfy-questlab-batch-request/v1 envelope, deploys it through the existing
SHA256-verified i5 config lane, and waits through one BatchMode SSH session for the plugin's
request receipt. This is not a remote console: operation, suite, and gallery profiles are
ValidateSet allowlists; there is no command text, path, key, or prefab field.

Run Test-I5Link.ps1 once before the first request in a live test block. This script does not
repeat that preflight and does not fall back to password authentication.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet(
        'prepare', 'run', 'reset', 'report', 'export',
        'gallery_build', 'gallery_compare', 'gallery_identify', 'gallery_clear', 'gallery_rebuild'
    )]
    [string]$Operation,

    [ValidateSet('all-schools', 'creator-events')]
    [string]$Suite = 'all-schools',

    [ValidateSet('classic', 'marble-wide', 'marble-grand')]
    [string]$Profile = 'marble-wide',

    [ValidateSet('classic', 'marble-wide', 'marble-grand')]
    [string]$CompareProfile = 'marble-grand',

    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$Selector = 'all',

    [ValidateRange(1, 30)]
    [int]$ExpiresMinutes = 10,

    [ValidateRange(0, 60)]
    [int]$WaitSeconds = 45,

    [string]$OutputDirectory
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$deployScript = Join-Path $PSScriptRoot 'Deploy-ToI5.ps1'
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot 'captures\questlab\i5'
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$now = [DateTimeOffset]::UtcNow
$stamp = $now.ToString('yyyyMMddTHHmmssZ')
$nonce = [guid]::NewGuid().ToString('N').Substring(0, 8)
$requestId = "$($Operation.Replace('_', '-'))-$stamp-$nonce"
$request = [ordered]@{
    schema      = 'comfy-questlab-batch-request/v1'
    request_id  = $requestId
    operation   = $Operation
    created_utc = $now.ToString('o')
    expires_utc = $now.AddMinutes($ExpiresMinutes).ToString('o')
}

switch ($Operation) {
    { $_ -in @('prepare', 'run') } {
        $request.suite = $Suite
        break
    }
    { $_ -in @('gallery_build', 'gallery_rebuild') } {
        $request.profile = $Profile
        break
    }
    'gallery_compare' {
        $request.profile = $Profile
        $request.compare_profile = $CompareProfile
        break
    }
    'gallery_clear' {
        $request.selector = $Selector
        break
    }
}

$requestJson = $request | ConvertTo-Json -Depth 4
$localRequestReceipt = Join-Path $OutputDirectory "$requestId-request.json"
[System.IO.File]::WriteAllText(
    $localRequestReceipt,
    $requestJson + [Environment]::NewLine,
    (New-Object System.Text.UTF8Encoding($false)))

$tempBase = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath())
$tempRoot = Join-Path $tempBase ("questlab-i5-" + $nonce)
$configRoot = Join-Path $tempRoot 'comfy-quest-lab'
$requestDir = Join-Path $configRoot 'requests'
New-Item -ItemType Directory -Force -Path $requestDir | Out-Null
$requestFile = Join-Path $requestDir 'questlab-batch-request.json'
[System.IO.File]::WriteAllText(
    $requestFile,
    $requestJson + [Environment]::NewLine,
    (New-Object System.Text.UTF8Encoding($false)))

try {
    # Deploy-ToI5.ps1 uses exit codes, so isolate it in a child PowerShell process.
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $deployScript `
        -Path $configRoot -ValheimConfig
    if ($LASTEXITCODE -ne 0) {
        throw "Quest Lab request deploy failed with exit code $LASTEXITCODE"
    }
} finally {
    $resolvedTemp = [System.IO.Path]::GetFullPath($tempRoot)
    if ($resolvedTemp.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTemp)) {
        Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
    }
}

Write-Host "request deployed and SHA256 verified: $requestId"
Write-Host "request envelope: $localRequestReceipt"
if ($WaitSeconds -eq 0) {
    Write-Host 'receipt wait disabled; use report or export as the next bounded request.'
    exit 0
}

$remoteReceipt = "C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/config/comfy-quest-lab/receipts/requests/$requestId.json"
$escapedRemoteReceipt = $remoteReceipt.Replace("'", "''")
$waitScript = @"
`$path = '$escapedRemoteReceipt'
`$deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
do {
    if (Test-Path -LiteralPath `$path) {
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding(`$false)
        [Console]::Write([System.IO.File]::ReadAllText(`$path))
        exit 0
    }
    Start-Sleep -Milliseconds 250
} while ([DateTime]::UtcNow -lt `$deadline)
exit 3
"@
$encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($waitScript))
$receiptLines = & ssh -o BatchMode=yes -o ConnectTimeout=8 i5 `
    "powershell.exe -NoProfile -EncodedCommand $encoded" 2>$null
$waitExit = $LASTEXITCODE
if ($waitExit -eq 3) {
    Write-Host "no plugin receipt within ${WaitSeconds}s; request was delivered once and was not reissued."
    Write-Host 'Valheim may not be running or may still be outside a world.'
    exit 3
}
if ($waitExit -ne 0) {
    throw "i5 receipt read failed with exit code $waitExit"
}

$receiptJson = @($receiptLines) -join [Environment]::NewLine
$receipt = $receiptJson | ConvertFrom-Json
if ($receipt.request_id -ne $requestId) {
    throw "receipt id mismatch: expected $requestId, got $($receipt.request_id)"
}
$localReceipt = Join-Path $OutputDirectory "$requestId-receipt.json"
[System.IO.File]::WriteAllText(
    $localReceipt,
    $receiptJson + [Environment]::NewLine,
    (New-Object System.Text.UTF8Encoding($false)))
Write-Host "request state: $($receipt.state)"
Write-Host "request receipt: $localReceipt"

if (-not [string]::IsNullOrWhiteSpace([string]$receipt.suite_receipt_path)) {
    $suitePath = ([string]$receipt.suite_receipt_path).Replace("'", "''")
    $suiteReadScript = @"
`$path = '$suitePath'
if (-not (Test-Path -LiteralPath `$path)) { exit 4 }
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding(`$false)
[Console]::Write([System.IO.File]::ReadAllText(`$path))
"@
    $suiteEncoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($suiteReadScript))
    $suiteLines = & ssh -o BatchMode=yes -o ConnectTimeout=8 i5 `
        "powershell.exe -NoProfile -EncodedCommand $suiteEncoded" 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'suite receipt path was reported but could not be read' }
    $suiteJson = @($suiteLines) -join [Environment]::NewLine
    $suiteObject = $suiteJson | ConvertFrom-Json
    if ($suiteObject.schema -ne 'comfy-questlab-suite-receipt/v1') {
        throw "unexpected suite receipt schema: $($suiteObject.schema)"
    }
    $localSuite = Join-Path $OutputDirectory "$requestId-suite.json"
    [System.IO.File]::WriteAllText(
        $localSuite,
        $suiteJson + [Environment]::NewLine,
        (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "suite receipt: $localSuite"
}

$logScript = @'
$path = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/LogOutput.log'
if (-not (Test-Path -LiteralPath $path)) { exit 0 }
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$matches = Get-Content -LiteralPath $path -Tail 400 |
    Select-String -Pattern '\[batch-request\]|\[gallery\]|\[quests\]|quest fired:'
[Console]::Write(($matches -join [Environment]::NewLine))
'@
$logEncoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($logScript))
$logLines = & ssh -o BatchMode=yes -o ConnectTimeout=8 i5 `
    "powershell.exe -NoProfile -EncodedCommand $logEncoded" 2>$null
if ($LASTEXITCODE -eq 0 -and @($logLines).Count -gt 0) {
    $localLog = Join-Path $OutputDirectory "$requestId-log.txt"
    [System.IO.File]::WriteAllText(
        $localLog,
        (@($logLines) -join [Environment]::NewLine) + [Environment]::NewLine,
        (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "relevant log tail: $localLog"
}

if ($receipt.state -in @('rejected', 'failed')) {
    Write-Error "Quest Lab request $($receipt.state): $($receipt.detail)"
    exit 1
}
exit 0
