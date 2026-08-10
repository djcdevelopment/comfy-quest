<#
.SYNOPSIS
Send one bounded Quest Lab suite/gallery request to i5 or local OMEN and collect its receipt.

.DESCRIPTION
Builds the fixed comfy-questlab-batch-request/v1 envelope, delivers it through either the
existing SHA256-verified i5 config lane or a fixed local OMEN config path, and waits for the
plugin's request receipt. This is not a console: operation, suite, gallery profiles, and lane
are ValidateSet allowlists; there is no command text, path, key, or prefab field.

Run Test-I5Link.ps1 once before the first request in a live test block. This script does not
repeat that preflight and does not fall back to password authentication.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet(
        'prepare', 'run', 'reset', 'report', 'export',
        'gallery_build', 'gallery_compare', 'gallery_identify', 'gallery_evidence',
        'gallery_clear', 'gallery_rebuild'
    )]
    [string]$Operation,

    [ValidateSet(
        'all-schools', 'creator-events',
        'scenario-attack_blocked', 'scenario-character_healed',
        'scenario-character_staggered', 'scenario-chat_sent',
        'scenario-container_emptied', 'scenario-damage_dealt',
        'scenario-global_key_removed', 'scenario-global_key_set',
        'scenario-item_consumed', 'scenario-item_crafted', 'scenario-item_dropped',
        'scenario-item_equipped', 'scenario-item_picked_up', 'scenario-item_unequipped',
        'scenario-kill', 'scenario-max_health_changed', 'scenario-piece_damaged',
        'scenario-piece_destroyed', 'scenario-piece_placed', 'scenario-piece_removed',
        'scenario-piece_repaired', 'scenario-player_died', 'scenario-player_teleported',
        'scenario-resource_damaged', 'scenario-resource_picked', 'scenario-sign_written',
        'scenario-skill_raised', 'scenario-skills_lowered', 'scenario-stamina_gained',
        'scenario-stamina_spent', 'scenario-station_fuel_added',
        'scenario-station_input_added', 'scenario-station_output_collected',
        'scenario-station_output_produced'
    )]
    [string]$Suite = 'all-schools',

    [ValidateSet('classic', 'marble-wide', 'marble-grand')]
    [string]$Profile = 'marble-grand',

    [ValidateSet('classic', 'marble-wide', 'marble-grand')]
    [string]$CompareProfile = 'marble-grand',

    [ValidatePattern('^[A-Za-z0-9._-]+$')]
    [string]$Selector = 'all',

    [ValidateRange(1, 30)]
    [int]$ExpiresMinutes = 10,

    [ValidateRange(0, 60)]
    [int]$WaitSeconds = 45,

    [string]$OutputDirectory,

    [switch]$DryRun,

    [ValidateSet('i5', 'omen')]
    [string]$Lane = 'i5'
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$deployScript = Join-Path $PSScriptRoot 'Deploy-ToI5.ps1'
$omenValheimRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim'
$i5ValheimRoot = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim'
if (-not $OutputDirectory) {
    $OutputDirectory = Join-Path $repoRoot ("captures\questlab\{0}" -f $Lane)
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
$effectiveProfile = if (
    $Operation -eq 'gallery_compare' -and
    -not $PSBoundParameters.ContainsKey('Profile')
) { 'marble-wide' } else { $Profile }

switch ($Operation) {
    { $_ -in @('prepare', 'run') } {
        $request.suite = $Suite
        break
    }
    { $_ -in @('gallery_build', 'gallery_rebuild') } {
        $request.profile = $effectiveProfile
        break
    }
    'gallery_compare' {
        $request.profile = $effectiveProfile
        $request.compare_profile = $CompareProfile
        break
    }
    'gallery_clear' {
        $request.selector = $Selector
        break
    }
    'gallery_evidence' {
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

if ($DryRun) {
    Write-Host 'dry run - no lane files changed'
    Write-Host "lane: $Lane"
    Write-Host "request id: $requestId"
    Write-Host "request envelope: $localRequestReceipt"
    Write-Output $requestJson
    exit 0
}

if ($Lane -eq 'i5') {
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
    Write-Host "request deployed and SHA256 verified to i5: $requestId"
} else {
    $omenRequestDir = Join-Path $omenValheimRoot 'BepInEx\config\comfy-quest-lab\requests'
    $omenRequestFile = Join-Path $omenRequestDir 'questlab-batch-request.json'
    $omenStagingFile = $omenRequestFile + '.deploying'
    New-Item -ItemType Directory -Force -Path $omenRequestDir | Out-Null
    try {
        [System.IO.File]::WriteAllText(
            $omenStagingFile,
            $requestJson + [Environment]::NewLine,
            (New-Object System.Text.UTF8Encoding($false)))
        if (Test-Path -LiteralPath $omenRequestFile) {
            Remove-Item -LiteralPath $omenRequestFile -Force
        }
        Move-Item -LiteralPath $omenStagingFile -Destination $omenRequestFile
        $sourceHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $localRequestReceipt).Hash
        $deliveredHash = (Get-FileHash -Algorithm SHA256 -LiteralPath $omenRequestFile).Hash
        if ($sourceHash -ne $deliveredHash) {
            throw 'Quest Lab OMEN request SHA256 verification failed'
        }
    } finally {
        if (Test-Path -LiteralPath $omenStagingFile) {
            Remove-Item -LiteralPath $omenStagingFile -Force
        }
    }
    Write-Host "request delivered and SHA256 verified to OMEN: $requestId"
}

Write-Host "request envelope: $localRequestReceipt"
if ($WaitSeconds -eq 0) {
    Write-Host 'receipt wait disabled; use report or export as the next bounded request.'
    exit 0
}

$remoteReceipt = "$i5ValheimRoot/BepInEx/config/comfy-quest-lab/receipts/requests/$requestId.json"
$omenReceipt = Join-Path $omenValheimRoot "BepInEx\config\comfy-quest-lab\receipts\requests\$requestId.json"
if ($Lane -eq 'i5') {
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
    $receiptJson = @($receiptLines) -join [Environment]::NewLine
} else {
    $waitExit = $null
    $deadline = [DateTime]::UtcNow.AddSeconds($WaitSeconds)
    do {
        if (Test-Path -LiteralPath $omenReceipt) {
            $receiptJson = [System.IO.File]::ReadAllText($omenReceipt)
            $waitExit = 0
            break
        }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $waitExit) { $waitExit = 3 }
}
if ($waitExit -eq 3) {
    Write-Host "no plugin receipt within ${WaitSeconds}s; request was delivered once and was not reissued."
    Write-Host 'Valheim may not be running or may still be outside a world.'
    exit 3
}
if ($waitExit -ne 0) {
    throw "$Lane receipt read failed with exit code $waitExit"
}

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
    $suitePathRaw = [string]$receipt.suite_receipt_path
    $suitePathNormalized = $suitePathRaw.Replace('\', '/')
    $expectedSuiteRoot = if ($Lane -eq 'i5') {
        "$i5ValheimRoot/BepInEx/config/comfy-quest-lab/receipts/suites/"
    } else {
        ($omenValheimRoot.Replace('\', '/') + '/BepInEx/config/comfy-quest-lab/receipts/suites/')
    }
    if (-not $suitePathNormalized.StartsWith(
            $expectedSuiteRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "suite receipt escaped the fixed receipt directory: $suitePathRaw"
    }
    $suiteLeaf = $suitePathNormalized.Substring($expectedSuiteRoot.Length)
    if ($suiteLeaf -notmatch '^[A-Za-z0-9._-]+\.json$') {
        throw "suite receipt did not name one fixed-directory JSON file: $suitePathRaw"
    }
    if ($Lane -eq 'i5') {
        $suitePath = $suitePathRaw.Replace("'", "''")
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
    } else {
        if (-not (Test-Path -LiteralPath $suitePathRaw)) {
            throw 'suite receipt path was reported but could not be read'
        }
        $suiteJson = [System.IO.File]::ReadAllText($suitePathRaw)
    }
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
    Write-Host ("suite verdict: {0} ({1}/{2} events, {3}/{2} quests, {4} double completions)" -f `
        $suiteObject.verdict,
        $suiteObject.witnessed_events,
        $suiteObject.required_events,
        $suiteObject.completed_example_quests,
        $suiteObject.double_completions)
    if ([int]$suiteObject.double_completions -ne 0) {
        Write-Error 'Quest Lab suite receipt records a same-action double completion.'
        exit 1
    }
    if ($Operation -eq 'run' -and
        ($Suite -eq 'creator-events' -or $Suite.StartsWith('scenario-')) -and
        $suiteObject.verdict -ne 'pass') {
        Write-Error "synthetic evaluator suite did not pass: $($suiteObject.verdict)"
        exit 1
    }
}

if (-not [string]::IsNullOrWhiteSpace([string]$receipt.evidence_path)) {
    $evidencePathRaw = [string]$receipt.evidence_path
    $evidencePathNormalized = $evidencePathRaw.Replace('\', '/')
    $expectedEvidenceRoot = if ($Lane -eq 'i5') {
        "$i5ValheimRoot/BepInEx/config/comfy-quest-lab/receipts/truth/"
    } else {
        ($omenValheimRoot.Replace('\', '/') + '/BepInEx/config/comfy-quest-lab/receipts/truth/')
    }
    if (-not $evidencePathNormalized.StartsWith(
            $expectedEvidenceRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw "truth evidence escaped the fixed receipt directory: $evidencePathRaw"
    }
    $evidenceLeaf = $evidencePathNormalized.Substring($expectedEvidenceRoot.Length)
    if ($evidenceLeaf -notmatch '^[A-Za-z0-9._-]+\.json$') {
        throw "truth evidence did not name one fixed-directory JSON file: $evidencePathRaw"
    }
    if ($Lane -eq 'i5') {
        $escapedEvidencePath = $evidencePathRaw.Replace("'", "''")
        $evidenceReadScript = @"
`$path = '$escapedEvidencePath'
if (-not (Test-Path -LiteralPath `$path)) { exit 4 }
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding(`$false)
[Console]::Write([System.IO.File]::ReadAllText(`$path))
"@
        $evidenceEncoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($evidenceReadScript))
        $evidenceLines = & ssh -o BatchMode=yes -o ConnectTimeout=8 i5 `
            "powershell.exe -NoProfile -EncodedCommand $evidenceEncoded" 2>$null
        if ($LASTEXITCODE -ne 0) { throw 'truth evidence path was reported but could not be read' }
        $evidenceJson = @($evidenceLines) -join [Environment]::NewLine
    } else {
        if (-not (Test-Path -LiteralPath $evidencePathRaw)) {
            throw 'truth evidence path was reported but could not be read'
        }
        $evidenceJson = [System.IO.File]::ReadAllText($evidencePathRaw)
    }
    $evidenceObject = $evidenceJson | ConvertFrom-Json
    if ($evidenceObject.schema -ne 'comfy-questlab-gallery-truth/v1') {
        throw "unexpected truth evidence schema: $($evidenceObject.schema)"
    }
    $localEvidence = Join-Path $OutputDirectory "$requestId-truth.json"
    [System.IO.File]::WriteAllText(
        $localEvidence,
        $evidenceJson + [Environment]::NewLine,
        (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "truth evidence: $localEvidence"
    Write-Host "truth verdict: $($evidenceObject.verdict)"
}

$logScript = @'
$path = 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/LogOutput.log'
if (-not (Test-Path -LiteralPath $path)) { exit 0 }
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
$matches = Get-Content -LiteralPath $path -Tail 400 |
    Select-String -Pattern '\[batch-request\]|\[gallery\]|\[quests\]|quest fired:'
[Console]::Write(($matches -join [Environment]::NewLine))
'@
if ($Lane -eq 'i5') {
    $logEncoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($logScript))
    $logLines = & ssh -o BatchMode=yes -o ConnectTimeout=8 i5 `
        "powershell.exe -NoProfile -EncodedCommand $logEncoded" 2>$null
    $logReadExit = $LASTEXITCODE
} else {
    $omenLog = Join-Path $omenValheimRoot 'BepInEx\LogOutput.log'
    $logLines = @()
    if (Test-Path -LiteralPath $omenLog) {
        $logLines = Get-Content -LiteralPath $omenLog -Tail 400 |
            Select-String -Pattern '\[batch-request\]|\[gallery\]|\[quests\]|quest fired:' |
            ForEach-Object { $_.Line }
    }
    $logReadExit = 0
}
if ($logReadExit -eq 0 -and @($logLines).Count -gt 0) {
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
