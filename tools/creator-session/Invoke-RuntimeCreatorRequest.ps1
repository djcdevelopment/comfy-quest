<#
.SYNOPSIS
Send one bounded Runtime Creator Session request and collect its receipt.

.DESCRIPTION
Writes the fixed comfy-quest-runtime-request/v1 envelope to OMEN or the verified i5
config lane. Operations are status, arm, disarm, build_on, and build_off. The build pair
only controls Valheim's local no-cost/all-pieces and god-mode booleans inside an
identity-pinned private world. Every request expires and pins machine, world UID, and
Creator Session id; there is no command, key, pack, Charm, prefab, or path field.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('status', 'arm', 'disarm', 'build_on', 'build_off')]
    [string]$Operation,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9._-]{1,80}$')]
    [string]$ExpectedMachine,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^-?[0-9]{1,20}$')]
    [string]$ExpectedWorldUid,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9._-]{1,80}$')]
    [string]$CreatorSessionId,

    [ValidateSet('omen', 'i5')]
    [string]$Lane = 'omen',

    [ValidateRange(1, 30)]
    [int]$ExpiresMinutes = 10,

    [ValidateRange(0, 60)]
    [int]$WaitSeconds = 45,

    [string]$OmenValheimRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim',

    [string]$OutputDirectory,

    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

function Get-Sha256([string]$Path) {
    # Deliberately not Get-FileHash: it lives in Microsoft.PowerShell.Utility, and a hosted
    # runner has been observed failing to resolve it with the module present and its directory
    # on PSModulePath. Request verification must not depend on module autoloading.
    # See docs/creator-os-audit-2026-08-24.md D7.
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        try {
            return [System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
        } finally { $stream.Dispose() }
    } finally { $sha.Dispose() }
}
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }

$i5ValheimRoot = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim'
$deployScript = Join-Path $repoRoot 'tools\i5-deploy\Deploy-ToI5.ps1'
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot ("captures\creator-session\{0}" -f $CreatorSessionId)
}
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

$now = [DateTimeOffset]::UtcNow
$stamp = $now.ToString('yyyyMMddTHHmmssZ')
$nonce = [Guid]::NewGuid().ToString('N').Substring(0, 8)
$requestId = "runtime-$Operation-$stamp-$nonce"
$request = [ordered]@{
    schema = 'comfy-quest-runtime-request/v1'
    request_id = $requestId
    operation = $Operation
    created_utc = $now.ToString('o')
    expires_utc = $now.AddMinutes($ExpiresMinutes).ToString('o')
    expected_machine = $ExpectedMachine
    expected_world_uid = $ExpectedWorldUid
    creator_session_id = $CreatorSessionId
}
$requestJson = $request | ConvertTo-Json -Depth 3
$localRequest = Join-Path $OutputDirectory "$requestId-request.json"
[IO.File]::WriteAllText(
    $localRequest,
    $requestJson + [Environment]::NewLine,
    (New-Object System.Text.UTF8Encoding($false)))

if ($DryRun) {
    Write-Host 'dry run - no Runtime files changed'
    Write-Host "request envelope: $localRequest"
    Write-Output $requestJson
    exit 0
}

if ($Lane -eq 'omen') {
    $OmenValheimRoot = [IO.Path]::GetFullPath($OmenValheimRoot).TrimEnd('\', '/')
    if ([IO.Path]::GetPathRoot($OmenValheimRoot) -eq $OmenValheimRoot -or
        -not (Test-Path -LiteralPath $OmenValheimRoot -PathType Container)) {
        throw "Unsafe or missing OMEN Valheim root: $OmenValheimRoot"
    }
}

if ($Lane -eq 'i5') {
    $tempBase = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    $tempRoot = Join-Path $tempBase ("quest-runtime-request-" + $nonce)
    $configRoot = Join-Path $tempRoot 'comfy-quest-runtime'
    $requestDirectory = Join-Path $configRoot 'requests'
    New-Item -ItemType Directory -Force -Path $requestDirectory | Out-Null
    [IO.File]::WriteAllText(
        (Join-Path $requestDirectory 'creator-request.json'),
        $requestJson + [Environment]::NewLine,
        (New-Object System.Text.UTF8Encoding($false)))
    try {
        & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $deployScript `
            -Path $configRoot -ValheimConfig
        if ($LASTEXITCODE -ne 0) {
            throw "Runtime request deploy failed with exit code $LASTEXITCODE"
        }
    } finally {
        $resolvedTemp = [IO.Path]::GetFullPath($tempRoot)
        if ($resolvedTemp.StartsWith($tempBase, [StringComparison]::OrdinalIgnoreCase) -and
            (Test-Path -LiteralPath $resolvedTemp)) {
            Remove-Item -LiteralPath $resolvedTemp -Recurse -Force
        }
    }
} else {
    $requestDirectory = Join-Path $omenValheimRoot 'BepInEx\config\comfy-quest-runtime\requests'
    $requestPath = Join-Path $requestDirectory 'creator-request.json'
    $stagingPath = $requestPath + '.deploying'
    New-Item -ItemType Directory -Force -Path $requestDirectory | Out-Null
    try {
        [IO.File]::WriteAllText(
            $stagingPath,
            $requestJson + [Environment]::NewLine,
            (New-Object System.Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $requestPath) { Remove-Item -LiteralPath $requestPath -Force }
        Move-Item -LiteralPath $stagingPath -Destination $requestPath
        if ((Get-Sha256 $localRequest) -ne (Get-Sha256 $requestPath)) {
            throw 'Runtime OMEN request SHA256 verification failed.'
        }
    } finally {
        if (Test-Path -LiteralPath $stagingPath) { Remove-Item -LiteralPath $stagingPath -Force }
    }
}

Write-Host "Runtime request delivered: $requestId"
if ($WaitSeconds -eq 0) { exit 0 }

$remoteReceipt = "$i5ValheimRoot/BepInEx/config/comfy-quest-runtime/receipts/creator-requests/$requestId.json"
$omenReceipt = Join-Path $omenValheimRoot "BepInEx\config\comfy-quest-runtime\receipts\creator-requests\$requestId.json"
if ($Lane -eq 'i5') {
    $escapedReceipt = $remoteReceipt.Replace("'", "''")
    $waitScript = @"
`$path = '$escapedReceipt'
`$deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
do {
    if (Test-Path -LiteralPath `$path) {
        [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding(`$false)
        [Console]::Write([IO.File]::ReadAllText(`$path))
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
    $receiptJson = @($receiptLines) -join [Environment]::NewLine
} else {
    $waitExit = $null
    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    do {
        if (Test-Path -LiteralPath $omenReceipt) {
            $receiptJson = [IO.File]::ReadAllText($omenReceipt)
            $waitExit = 0
            break
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $waitExit) { $waitExit = 3 }
}
if ($waitExit -eq 3) {
    Write-Host "no Runtime receipt within ${WaitSeconds}s; Valheim may be closed or outside a world."
    exit 3
}
if ($waitExit -ne 0) { throw "Runtime receipt read failed with exit code $waitExit" }

$receipt = $receiptJson | ConvertFrom-Json
if ($receipt.schema -ne 'comfy-quest-runtime-request-receipt/v1') {
    throw "Unexpected Runtime receipt schema: $($receipt.schema)"
}
if ($receipt.request_id -ne $requestId) { throw 'Runtime receipt id mismatch.' }
if ($receipt.operation -ne $Operation) { throw 'Runtime receipt operation mismatch.' }
if ($receipt.creator_session_id -ne $CreatorSessionId) { throw 'Runtime receipt session mismatch.' }
if ($receipt.machine -ne $ExpectedMachine) { throw 'Runtime receipt machine mismatch.' }
if ($receipt.world_uid -ne $ExpectedWorldUid) { throw 'Runtime receipt world mismatch.' }
$localReceipt = Join-Path $OutputDirectory "$requestId-receipt.json"
[IO.File]::WriteAllText(
    $localReceipt,
    $receiptJson + [Environment]::NewLine,
    (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Runtime request state: $($receipt.state)"
Write-Host "Runtime request receipt: $localReceipt"
if ($receipt.state -in @('rejected', 'failed')) {
    Write-Error "Runtime request $($receipt.state): $($receipt.detail)"
    exit 1
}
exit 0
