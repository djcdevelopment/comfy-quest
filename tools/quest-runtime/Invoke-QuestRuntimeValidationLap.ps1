#Requires -Version 5.1
<#
.SYNOPSIS
Proof-gated preparation, receipt coaching, and restoration for the local OMEN validation lap.

.DESCRIPTION
This harness owns machine preparation and evidence only. Studio authors the quest, Runtime
performs every game mutation, and the player controls every in-game action. Human pacing never
times out; -ActionObserved starts a short wait only after the player says an action was completed.
#>
[CmdletBinding()]
param(
    [ValidateSet('Plan','Preflight','Prepare','ValidateRevision','ArmPrivateWorld','Monitor','Status','Cleanup')]
    [string] $Action = 'Status',
    [string] $RunId = '',
    [string] $ValheimRoot = '',
    [string] $EvidenceRoot = '',
    [ValidateSet('','startup','check_r1','load_r1','charm_ready','bind_r1','chat_advance','drop_partial','drop_advance','pickup_complete','check_r2','load_r2','orphan_r2')]
    [string] $Expectation = '',
    [ValidateSet('','1.0.0','1.0.1')]
    [string] $ExpectedVersion = '',
    [ValidateRange(1,120)]
    [int] $MachineTimeoutSeconds = 15,
    [switch] $ActionObserved,
    [switch] $PlanOnly,
    # Test-only seams are rejected unless ValheimRoot contains the fixture sentinel.
    [switch] $FixtureMode,
    [switch] $TestPretendValheimRunning,
    [switch] $TestFaultDuringCleanup,
    [ValidateSet('','Quarantine','Deploy')]
    [string] $TestFaultAfter = ''
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$capturesRoot = [IO.Path]::GetFullPath((Join-Path $repoRoot 'captures'))
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    $EvidenceRoot = Join-Path $capturesRoot 'five-intent-slice1'
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
if ([string]::IsNullOrWhiteSpace($ValheimRoot)) {
    $ValheimRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim'
}
$ValheimRoot = [IO.Path]::GetFullPath($ValheimRoot)

$sentinelName = '.comfy-quest-validation-lap'
$fixtureSentinelName = '.comfy-quest-validation-fixture'
$runtimeRelative = 'BepInEx\config\comfy-quest-runtime'
$configRelative = 'BepInEx\config\djcdevelopment.valheim.comfyquestruntime.cfg'
$pluginRelative = 'BepInEx\plugins'
$runtimeRoot = Join-Path $ValheimRoot $runtimeRelative
$configPath = Join-Path $ValheimRoot $configRelative
$pluginRoot = Join-Path $ValheimRoot $pluginRelative
$logPath = Join-Path $ValheimRoot 'BepInEx\LogOutput.log'
$runtimeDll = Join-Path $repoRoot 'network\mod\ComfyQuestRuntime\bin\Release\net48\ComfyQuestRuntime.dll'
$contractsDll = Join-Path $repoRoot 'network\mod\ComfyQuestContracts\bin\Release\netstandard2.0\ComfyQuestContracts.dll'
$jsonDll = Join-Path $repoRoot 'network\mod\ComfyQuestRuntime\bin\Release\net48\Newtonsoft.Json.dll'
$expectedMessages = @(
    'The charm wakes. Two offerings of wood, before the moment passes.',
    'The offering is heard. Reclaim one piece to seal the rite.',
    'The circuit closes. The charm remembers this telling.'
)
$expectedR2FinalMessage = 'The circuit closes. The Charm remembers a new telling.'

function Test-ChildPath([string] $Parent, [string] $Child) {
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $childFull = [IO.Path]::GetFullPath($Child)
    return $childFull.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)
}

function Assert-SafeLayout {
    if (-not (Test-ChildPath $capturesRoot $EvidenceRoot)) {
        throw "EvidenceRoot must stay beneath the repository captures directory: $capturesRoot"
    }
    $repoFull = [IO.Path]::GetFullPath($repoRoot).TrimEnd('\','/')
    $valheimFull = [IO.Path]::GetFullPath($ValheimRoot).TrimEnd('\','/')
    if ($valheimFull.Length -lt 12 -or $valheimFull -eq $repoFull) {
        throw "Unsafe ValheimRoot: $ValheimRoot"
    }
    foreach ($path in @($runtimeRoot,$configPath,$pluginRoot,$logPath)) {
        if (-not (Test-ChildPath $ValheimRoot $path)) { throw "Valheim path escaped its root: $path" }
    }
}

function Assert-FixtureSeams {
    if (-not ($FixtureMode -or $TestPretendValheimRunning -or $TestFaultDuringCleanup -or $TestFaultAfter)) { return }
    if (-not (Test-Path -LiteralPath (Join-Path $ValheimRoot $fixtureSentinelName) -PathType Leaf)) {
        throw 'Test-only switches require a fixture-owned ValheimRoot.'
    }
}

function Write-Json([string] $Path, [object] $Value) {
    $directory = Split-Path -Parent $Path
    if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    $temporary = "$Path.tmp"
    [IO.File]::WriteAllText(
        $temporary,
        (($Value | ConvertTo-Json -Depth 30) + "`n"),
        [Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}

function Get-Sha256([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    # BepInEx keeps LogOutput.log open while the game runs. Hash through a stream
    # that permits the existing writer instead of Get-FileHash's narrower sharing.
    $stream = [IO.File]::Open($Path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($sha.ComputeHash($stream))).Replace('-','').ToLowerInvariant()
    } finally {
        $sha.Dispose()
        $stream.Dispose()
    }
}

function Get-Relative([string] $Base, [string] $Path) {
    $baseUri = [Uri](([IO.Path]::GetFullPath($Base).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar))
    $pathUri = [Uri]([IO.Path]::GetFullPath($Path))
    return [Uri]::UnescapeDataString($baseUri.MakeRelativeUri($pathUri).ToString()).Replace('/','\')
}

function Copy-Atomic([string] $Source, [string] $Destination) {
    $directory = Split-Path -Parent $Destination
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
    $temporary = "$Destination.quest-validation.tmp"
    Copy-Item -LiteralPath $Source -Destination $temporary -Force
    Move-Item -LiteralPath $temporary -Destination $Destination -Force
    if ((Get-Sha256 $Source) -ne (Get-Sha256 $Destination)) {
        throw "Copy hash mismatch: $Destination"
    }
}

function Move-OwnedFile([string] $Source, [string] $Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { return }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    if (Test-Path -LiteralPath $Destination) { throw "Owned destination already exists: $Destination" }
    Move-Item -LiteralPath $Source -Destination $Destination
}

function Set-PrivateConfirmation([string] $Path, [bool] $Enabled) {
    $value = if ($Enabled) { 'true' } else { 'false' }
    $newline = "`r`n"
    $text = if (Test-Path -LiteralPath $Path) { [IO.File]::ReadAllText($Path) } else { '' }
    if ($text.Contains("`n") -and -not $text.Contains("`r`n")) { $newline = "`n" }
    if ($text -match '(?m)^\s*PrivateWorldConfirmed\s*=') {
        $text = [regex]::Replace(
            $text,
            '(?m)^(\s*PrivateWorldConfirmed\s*=\s*)\S+\s*$',
            ('$1' + $value))
    } else {
        if ($text.Length -gt 0 -and -not $text.EndsWith("`n")) { $text += $newline }
        $text += '[Safety]' + $newline + 'PrivateWorldConfirmed = ' + $value + $newline
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $text, [Text.UTF8Encoding]::new($false))
}

function Get-PrivateConfirmation([string] $Path) {
    if (-not (Test-Path -LiteralPath $Path)) { return $false }
    $match = [regex]::Match([IO.File]::ReadAllText($Path), '(?m)^\s*PrivateWorldConfirmed\s*=\s*(true|false)\s*$')
    return $match.Success -and $match.Groups[1].Value -ieq 'true'
}

function Test-ValheimRunning {
    if ($TestPretendValheimRunning) { return $true }
    return @(Get-Process valheim -ErrorAction SilentlyContinue).Count -gt 0
}

function Assert-ValheimClosed([string] $Purpose) {
    if (Test-ValheimRunning) { throw "Valheim must be closed before $Purpose." }
}

function Get-RunPaths {
    if ([string]::IsNullOrWhiteSpace($RunId) -or $RunId -notmatch '^[A-Za-z0-9._-]{1,80}$') {
        throw 'RunId must be a safe 1-80 character token.'
    }
    $root = [IO.Path]::GetFullPath((Join-Path $EvidenceRoot $RunId))
    if (-not (Test-ChildPath $EvidenceRoot $root)) { throw 'Run path escaped EvidenceRoot.' }
    return [ordered]@{
        Root = $root
        Sentinel = Join-Path $root $sentinelName
        Context = Join-Path $root 'context.json'
        Quarantine = Join-Path $root 'quarantine'
        Backup = Join-Path $root 'backup'
        Post = Join-Path $root 'post-run'
    }
}

function Read-Context([object] $Paths) {
    if (-not (Test-Path -LiteralPath $Paths.Sentinel -PathType Leaf)) { throw 'Validation-lap sentinel is missing.' }
    if (-not (Test-Path -LiteralPath $Paths.Context -PathType Leaf)) { throw 'Validation-lap context is missing.' }
    $sentinel = Get-Content -LiteralPath $Paths.Sentinel -Raw | ConvertFrom-Json
    $context = Get-Content -LiteralPath $Paths.Context -Raw | ConvertFrom-Json
    if ($sentinel.schema -ne 'comfy-quest-validation-lap-sentinel/v1' -or $sentinel.run_id -ne $RunId) {
        throw 'Validation-lap sentinel does not own this run.'
    }
    if ($context.schema -ne 'comfy-quest-validation-lap-context/v1' -or $context.run_id -ne $RunId) {
        throw 'Validation-lap context identity mismatch.'
    }
    if ([IO.Path]::GetFullPath([string]$context.valheim_root) -ne $ValheimRoot) {
        throw 'Validation-lap context points at a different ValheimRoot.'
    }
    return $context
}

function Save-Context([object] $Paths, [object] $Context) {
    $Context.updated_utc = [DateTimeOffset]::UtcNow.ToString('o')
    Write-Json $Paths.Context $Context
}

function Import-ContractAssemblies {
    foreach ($required in @($jsonDll,$contractsDll)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required Contracts payload missing: $required" }
    }
    if (-not ('Newtonsoft.Json.Linq.JToken' -as [type])) { Add-Type -Path $jsonDll }
    if (-not ('ComfyQuestContracts.QuestPackStore' -as [type])) { Add-Type -Path $contractsDll }
}

function Read-StrictToken([string] $Path) {
    Import-ContractAssemblies
    $input = [IO.StringReader]::new([IO.File]::ReadAllText($Path))
    $reader = [Newtonsoft.Json.JsonTextReader]::new($input)
    $reader.DateParseHandling = [Newtonsoft.Json.DateParseHandling]::None
    $settings = [Newtonsoft.Json.Linq.JsonLoadSettings]::new()
    $settings.DuplicatePropertyNameHandling = [Newtonsoft.Json.Linq.DuplicatePropertyNameHandling]::Error
    try {
        $token = [Newtonsoft.Json.Linq.JToken]::ReadFrom($reader, $settings)
        if ($reader.Read()) { throw "JSON has trailing content: $Path" }
        # JObject implements IEnumerable. Keep PowerShell's success pipeline from
        # unrolling a valid receipt into JProperty objects before ToObject sees it.
        Write-Output -NoEnumerate $token
        return
    } catch {
        throw "Strict JSON parse failed for $Path`: $($_.Exception.Message)"
    } finally {
        $reader.Dispose()
        $input.Dispose()
    }
}

function Get-TokenSha256([Newtonsoft.Json.Linq.JToken] $Token) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($Token.ToString([Newtonsoft.Json.Formatting]::None))
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-','').ToLowerInvariant() }
    finally { $sha.Dispose() }
}

function Get-MessageText([object] $Transition) {
    $messages = @($Transition.Actions | Where-Object { $_.Type -eq 'message' })
    if ($messages.Count -ne 1 -or @($Transition.Actions).Count -ne 1) {
        throw "Each Woodbound transition must carry exactly one message action: $($Transition.Id)"
    }
    if ($null -eq $messages[0].Parameters -or $null -eq $messages[0].Parameters['text']) {
        throw "Message action text is missing: $($Transition.Id)"
    }
    return [string]$messages[0].Parameters['text'].ToString()
}

function Assert-WoodboundCandidate([object] $Candidate, [string] $Version) {
    if (-not $Candidate.IsValid) {
        $detail = @($Candidate.Diagnostics | ForEach-Object { $_.Code + ': ' + $_.Message }) -join '; '
        throw "Questpack contract rejected $($Candidate.Path): $detail"
    }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $archive = [IO.Compression.ZipFile]::OpenRead($Candidate.Path)
    try {
        $entries = @($archive.Entries | Where-Object { $_.FullName.StartsWith('experiences/') -and $_.FullName.EndsWith('.json') })
        if ($entries.Count -ne 1) { throw 'Woodbound Signal must contain exactly one experience document.' }
        $stream = $entries[0].Open()
        $reader = [IO.StreamReader]::new($stream)
        try { $json = $reader.ReadToEnd() } finally { $reader.Dispose(); $stream.Dispose() }
    } finally { $archive.Dispose() }

    $tokenReader = [Newtonsoft.Json.JsonTextReader]::new([IO.StringReader]::new($json))
    $settings = [Newtonsoft.Json.Linq.JsonLoadSettings]::new()
    $settings.DuplicatePropertyNameHandling = [Newtonsoft.Json.Linq.DuplicatePropertyNameHandling]::Error
    try { $token = [Newtonsoft.Json.Linq.JToken]::ReadFrom($tokenReader, $settings) }
    finally { $tokenReader.Dispose() }
    $document = [Newtonsoft.Json.JsonConvert]::DeserializeObject($json, [ComfyQuestContracts.ExperienceDocument])
    if ($document.Title -ne 'The Woodbound Signal') { throw 'Quest title must be The Woodbound Signal.' }
    if ($Candidate.Manifest.Version -ne $Version) { throw "Expected version $Version, found $($Candidate.Manifest.Version)." }
    $stages = @($document.Stages)
    if ($stages.Count -ne 3) { throw "Woodbound Signal must have exactly three stages, found $($stages.Count)." }
    $transitions = @($stages | ForEach-Object {
        if (@($_.Transitions).Count -ne 1) { throw "Stage $($_.Id) must have exactly one transition." }
        $_.Transitions[0]
    })
    $first = $transitions[0].When
    $second = $transitions[1].When
    $third = $transitions[2].When
    if ($first.Op -ne 'EVENT' -or $first.Event -ne 'chat_sent' -or $first.Target -ne 'normal') {
        throw 'Stage 1 must be normal local chat.'
    }
    if ($second.Op -ne 'COUNT' -or $second.Count -ne 2 -or $second.WithinSeconds -ne 30 -or @($second.Children).Count -ne 1 -or
        $second.Children[0].Event -ne 'item_dropped' -or $second.Children[0].Target -ne 'Wood') {
        throw 'Stage 2 must drop Wood twice within 30 seconds.'
    }
    if ($third.Op -ne 'EVENT' -or $third.Event -ne 'item_picked_up' -or $third.Target -ne 'Wood') {
        throw 'Stage 3 must pick up Wood.'
    }
    if ($transitions[0].NextStage -ne $stages[1].Id -or $transitions[1].NextStage -ne $stages[2].Id -or
        $transitions[2].Outcome -ne 'complete' -or -not [string]::IsNullOrWhiteSpace($transitions[2].NextStage)) {
        throw 'Woodbound Signal stage order or completion outcome is invalid.'
    }
    $wantedFinal = if ($Version -eq '1.0.1') { $expectedR2FinalMessage } else { $expectedMessages[2] }
    $actualMessages = @(
        (Get-MessageText $transitions[0]),
        (Get-MessageText $transitions[1]),
        (Get-MessageText $transitions[2]))
    $wantedMessages = @($expectedMessages[0],$expectedMessages[1],$wantedFinal)
    for ($index = 0; $index -lt 3; $index++) {
        if ($actualMessages[$index] -cne $wantedMessages[$index]) {
            throw "Stage $($index + 1) message differs from the validated lap copy."
        }
    }

    $clone = $token.DeepClone()
    $finalActions = $clone['stages'][2]['transitions'][0]['actions']
    $finalMessage = @($finalActions | Where-Object { [string]$_['type'] -eq 'message' })
    if ($finalMessage.Count -ne 1) { throw 'Compiled JSON final message shape is invalid.' }
    $finalMessage[0]['text'] = [Newtonsoft.Json.Linq.JValue]::new('__WOODBOUND_FINAL_MESSAGE__')
    return [ordered]@{
        pack_id = $Candidate.Manifest.PackId
        version = $Candidate.Manifest.Version
        content_hash = $Candidate.ContentHash
        package_sha256 = $Candidate.Sha256
        filename = [IO.Path]::GetFileName($Candidate.Path)
        experience_id = $document.Id
        normalized_experience_sha256 = Get-TokenSha256 $clone
        final_message = $actualMessages[2]
    }
}

function Get-ActiveSet {
    $path = Join-Path $runtimeRoot 'active\active-set.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }
    $token = Read-StrictToken $path
    return $token.ToObject([ComfyQuestContracts.ActiveSet])
}

function Get-ReceiptFiles([object] $Context) {
    $directory = Join-Path $runtimeRoot 'receipts'
    if (-not (Test-Path -LiteralPath $directory)) { return @() }
    $baseline = @($Context.receipt_baseline)
    return @(Get-ChildItem -LiteralPath $directory -Filter *.json -File |
        Where-Object { $_.Name -notin $baseline } | Sort-Object Name)
}

function Read-NewReceipts([object] $Context) {
    $rows = @()
    foreach ($file in Get-ReceiptFiles $Context) {
        $token = Read-StrictToken $file.FullName
        $receipt = $token.ToObject([ComfyQuestContracts.RuntimeReceipt])
        if ($receipt.Schema -ne 'comfy-quest-runtime-receipt/v1' -or
            [string]::IsNullOrWhiteSpace($receipt.Id) -or
            [string]::IsNullOrWhiteSpace($receipt.Operation) -or
            [string]::IsNullOrWhiteSpace($receipt.Status) -or
            $receipt.AtUtc -eq [DateTimeOffset]::MinValue) {
            throw "Receipt contract fields are missing: $($file.FullName)"
        }
        if ($receipt.Operation -in @('event','action','transition') -and $receipt.PackId) {
            if ($receipt.ActivationId -notmatch '^act-[0-9]{8}T[0-9]{9}Z-[0-9a-f]{8}$') {
                throw "Gameplay receipt lacks a valid activation_id: $($file.Name)"
            }
            if ($receipt.CorrelationId -notmatch '^evt-[0-9a-f]{12}$') {
                throw "Gameplay receipt lacks a valid correlation_id: $($file.Name)"
            }
        }
        if (($receipt.Operation -eq 'event' -and $receipt.Status -eq 'matched') -or
            ($receipt.Operation -eq 'transition' -and $receipt.Status -in @('advanced','complete'))) {
            if ($null -eq $receipt.Evidence -or -not $receipt.Evidence.Satisfied) {
                throw "Matched receipt lacks satisfied clause evidence: $($file.Name)"
            }
        }
        if ($receipt.Operation -eq 'transition' -and $receipt.Status -in @('advanced','complete') -and
            $null -eq $receipt.StageEnteredUtc) {
            throw "Transition receipt lacks stage_entered_utc: $($file.Name)"
        }
        $rows += [pscustomobject]@{ File = $file.Name; Receipt = $receipt }
    }
    return @($rows)
}

function Get-Revision([object] $Context, [string] $Label) {
    $property = $Context.revisions.PSObject.Properties[$Label]
    if ($null -eq $property) { throw "Validated revision $Label is missing from the run context." }
    return $property.Value
}

function Test-ReceiptIdentity([object] $Receipt, [object] $Revision) {
    return $Receipt.PackId -eq $Revision.pack_id -and $Receipt.Version -eq $Revision.version -and
        $Receipt.ContentHash -ieq $Revision.content_hash
}

function Find-Expectation([string] $Name, [object] $Context, [object[]] $Rows) {
    $label = if ($Name.EndsWith('_r2')) { 'r2' } else { 'r1' }
    $revision = if ($Name -eq 'startup' -or $Name -eq 'charm_ready') { $null } else { Get-Revision $Context $label }
    $receipts = @($Rows | ForEach-Object { $_.Receipt })
    switch ($Name) {
        'startup' {
            if (-not (Test-Path -LiteralPath $logPath)) { return $null }
            $hash = Get-Sha256 $logPath
            if ($hash -eq [string]$Context.log_baseline_sha256) { return $null }
            $stream = [IO.File]::Open($logPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::ReadWrite)
            try {
                $reader = [IO.StreamReader]::new($stream)
                try { $log = $reader.ReadToEnd() } finally { $reader.Dispose() }
            } finally { $stream.Dispose() }
            if ($log -notmatch 'Runtime ready\.') { return $null }
            return [pscustomobject]@{ proof = 'fresh Runtime ready log file'; file = [IO.Path]::GetFileName($logPath); sha256 = $hash }
        }
        'check_r1' { $match = @($receipts | Where-Object { $_.Operation -eq 'check' -and $_.Status -eq 'accepted' -and (Test-ReceiptIdentity $_ $revision) }) | Select-Object -Last 1 }
        'check_r2' { $match = @($receipts | Where-Object { $_.Operation -eq 'check' -and $_.Status -eq 'accepted' -and (Test-ReceiptIdentity $_ $revision) }) | Select-Object -Last 1 }
        'load_r1' { $match = @($receipts | Where-Object { $_.Operation -eq 'load' -and $_.Status -eq 'activated' -and (Test-ReceiptIdentity $_ $revision) }) | Select-Object -Last 1 }
        'load_r2' { $match = @($receipts | Where-Object { $_.Operation -eq 'load' -and $_.Status -eq 'activated' -and (Test-ReceiptIdentity $_ $revision) }) | Select-Object -Last 1 }
        'charm_ready' { $match = @($receipts | Where-Object { $_.Operation -eq 'charm_check' -and $_.Status -eq 'ready' }) | Select-Object -Last 1 }
        'bind_r1' { $match = @($receipts | Where-Object { $_.Operation -eq 'bind' -and $_.Status -eq 'inscribed' -and (Test-ReceiptIdentity $_ $revision) }) | Select-Object -Last 1 }
        'chat_advance' { $match = @($receipts | Where-Object { $_.Operation -eq 'transition' -and $_.Status -eq 'advanced' -and $_.EventName -eq 'chat_sent' -and (Test-ReceiptIdentity $_ $revision) }) | Select-Object -Last 1 }
        'drop_partial' { $match = @($receipts | Where-Object { $_.Operation -eq 'event' -and $_.Status -eq 'ignored' -and $_.EventName -eq 'item_dropped' -and $_.CurrentCount -eq 1 -and $_.RequiredCount -eq 2 -and (Test-ReceiptIdentity $_ $revision) }) | Select-Object -Last 1 }
        'drop_advance' { $match = @($receipts | Where-Object { $_.Operation -eq 'transition' -and $_.Status -eq 'advanced' -and $_.EventName -eq 'item_dropped' -and $_.CurrentCount -eq 2 -and $_.RequiredCount -eq 2 -and (Test-ReceiptIdentity $_ $revision) }) | Select-Object -Last 1 }
        'pickup_complete' { $match = @($receipts | Where-Object { $_.Operation -eq 'transition' -and $_.Status -eq 'complete' -and $_.EventName -eq 'item_picked_up' -and (Test-ReceiptIdentity $_ $revision) }) | Select-Object -Last 1 }
        'orphan_r2' { $match = @($receipts | Where-Object { $_.Operation -eq 'activation' -and $_.Status -eq 'orphaned_bindings' -and $_.CandidateCount -ge 1 -and (Test-ReceiptIdentity $_ $revision) }) | Select-Object -Last 1 }
        default { throw "Unsupported expectation: $Name" }
    }
    if (@($match).Count -eq 0) { return $null }
    if ($Name -in @('load_r1','load_r2')) {
        $active = Get-ActiveSet
        if ($null -eq $active -or $active.PackId -ne $revision.pack_id -or $active.Version -ne $revision.version -or
            $active.ContentHash -ine $revision.content_hash -or
            $active.ActivationId -notmatch '^act-[0-9]{8}T[0-9]{9}Z-[0-9a-f]{8}$') { return $null }
        return [pscustomobject]@{ proof = $Name; receipt_id = $match.Id; activation_id = $active.ActivationId }
    }
    return [pscustomobject]@{ proof = $Name; receipt_id = $match.Id; correlation_id = $match.CorrelationId; activation_id = $match.ActivationId }
}

function Invoke-CommandGate([string] $Name, [scriptblock] $Command) {
    Write-Host "=== $Name ==="
    # PowerShell 5.1 turns redirected native stderr (including unittest progress)
    # into NativeCommandError under Stop. Capture only success output; stderr stays
    # visible on its native stream and the real process exit code remains decisive.
    $gateOutput = @(& $Command)
    $gateExit = $LASTEXITCODE
    $gateOutput | Out-Host
    if ($gateExit -ne 0) { throw "$Name failed with exit code $gateExit." }
}

function Get-DotNet9Executable {
    $workspaceToolchain = Join-Path (Split-Path -Parent $repoRoot) 'dotnet9\dotnet.exe'
    $candidates = @(
        $env:COMFY_QUEST_DOTNET,
        $(if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' }),
        $(if (Test-Path -LiteralPath $workspaceToolchain) { $workspaceToolchain }),
        $(if (Get-Command dotnet -ErrorAction SilentlyContinue) { (Get-Command dotnet).Source })
    ) | Where-Object { $_ } | Select-Object -Unique
    foreach ($candidate in $candidates) {
        try {
            $resolved = (Get-Item -LiteralPath $candidate -ErrorAction Stop).FullName
            $major = [int]((& $resolved --version) -split '\.')[0]
            if ($LASTEXITCODE -eq 0 -and $major -ge 9) { return $resolved }
        } catch {}
    }
    throw 'A .NET 9 SDK is required for Studio verification.'
}

function Invoke-SourceGates {
    $dotnet9 = Get-DotNet9Executable
    Invoke-CommandGate 'repository identity' { & (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') }
    Invoke-CommandGate 'repository boundary' { python tools/assert_no_reach_in.py; if ($LASTEXITCODE -eq 0) { python tools/assert_no_reach_in.py --self-test } }
    Invoke-CommandGate 'Quest Lab build' { dotnet build network/mod/ComfyQuestLab/ComfyQuestLab.csproj -c Release }
    Invoke-CommandGate 'Contracts and Lab xUnit' { dotnet test network/mod/ComfyQuestLab.Tests/ComfyQuestLab.Tests.csproj -c Release }
    Invoke-CommandGate 'Python pins and drift' { python -m unittest discover -s tests }
    Invoke-CommandGate 'generated tome drift' { python tools/component-packets/render_quest_lab.py --check }
    Invoke-CommandGate 'seam catalog drift' { python tools/component-packets/generate_seam_catalog.py --check }
    Invoke-CommandGate 'Lab patch coverage' { python tools/component-packets/check_lab_patches.py }
    Invoke-CommandGate 'interim package pin' { python tools/nuget/repin_public.py --check-interim }
    Invoke-CommandGate 'release verifier self-test' { python tools/release/verify_quest_release.py --self-test }
    Invoke-CommandGate 'Studio build' { & $dotnet9 build src/Quest.Studio/Quest.Studio.csproj -c Release }
    Invoke-CommandGate 'Studio xUnit' { & $dotnet9 test src/Quest.Studio.Tests/Quest.Studio.Tests.csproj -c Release }
    Invoke-CommandGate 'Studio browser E2E' { & tools/quest-studio/Test-QuestStudioE2E.ps1 -SkipBrowserInstall }
    Invoke-CommandGate 'licensed Runtime build' { dotnet build network/mod/ComfyQuestRuntime/ComfyQuestRuntime.csproj -c Release --no-restore }
    Invoke-CommandGate 'validation harness self-test' { & tools/quest-runtime/Test-QuestRuntimeValidationLap.ps1 }
    if (Get-Command gitleaks -ErrorAction SilentlyContinue) {
        Invoke-CommandGate 'full-history secret scan' { gitleaks git --no-banner --redact --log-opts='--all' . }
    } else { throw 'gitleaks is required for the full-history secret scan.' }
}

function Invoke-Preflight {
    Assert-ValheimClosed 'validation-lap preflight'
    foreach ($required in @($runtimeDll,$contractsDll,$jsonDll)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Build output missing: $required" }
    }
    $unexpected = @(git status --porcelain | Where-Object { $_ -notmatch '^\?\? \.claude/' })
    if ($LASTEXITCODE -ne 0) { throw 'git status failed.' }
    if ($unexpected.Count) { throw "Working tree has unexpected changes: $($unexpected -join '; ')" }
    $allEol = @(git ls-files --eol)
    if ($LASTEXITCODE -ne 0) { throw 'git line-ending inspection failed.' }
    $indexEol = @($allEol | Where-Object { $_ -match 'i/(crlf|mixed).*attr/text' })
    if ($indexEol.Count) { throw "Canonical Git text is not LF: $($indexEol -join '; ')" }
    $lapFiles = @(
        'docs/five-intent-validation-lap-backlog.md',
        'docs/runbooks/I2-QUESTPACK-OMEN.md',
        'src/Quest.Studio.E2E.Tests/QuestStudioSyntheticE2ETests.cs',
        'src/Quest.Studio.Tests/QuestStudioServiceTests.cs',
        'tests/test_quest_runtime_validation_lap.py',
        'tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1',
        'tools/quest-runtime/Test-QuestRuntimeValidationLap.ps1'
    )
    $lapEol = @(git ls-files --eol -- $lapFiles | Where-Object { $_ -notmatch '^i/lf\s+w/lf\s+' })
    if ($lapEol.Count) { throw "Validation-lap source is not physical LF: $($lapEol -join '; ')" }
    if (-not $FixtureMode) { Invoke-SourceGates }
    $installed = @()
    foreach ($source in @($runtimeDll,$contractsDll,$jsonDll)) {
        $target = Join-Path $pluginRoot ([IO.Path]::GetFileName($source))
        $installed += [ordered]@{
            name = [IO.Path]::GetFileName($source)
            source_sha256 = Get-Sha256 $source
            installed_sha256 = Get-Sha256 $target
            matches = (Get-Sha256 $source) -eq (Get-Sha256 $target)
        }
    }
    [ordered]@{
        schema = 'comfy-quest-validation-lap-preflight/v1'
        verdict = 'ready_for_prepare'
        commit = (git rev-parse HEAD)
        valheim_closed = $true
        private_world_confirmed = Get-PrivateConfirmation $configPath
        deployment = $installed
        note = 'Prepare will deploy mismatched payloads, quarantine prior packs, and force safety false before player interaction.'
    }
}

function New-FileManifest([string] $Source, [string] $DestinationRoot, [string] $OriginalRoot) {
    $relative = Get-Relative $OriginalRoot $Source
    return [ordered]@{
        relative = $relative
        original = [IO.Path]::GetFullPath($Source)
        quarantine = [IO.Path]::GetFullPath((Join-Path $DestinationRoot $relative))
        sha256 = Get-Sha256 $Source
    }
}

function Restore-Run([object] $Paths, [object] $Context, [bool] $PrepareFailure = $false) {
    Assert-ValheimClosed 'validation-lap cleanup'
    $errors = [Collections.Generic.List[string]]::new()
    try { Set-PrivateConfirmation $configPath $false } catch { $errors.Add("safety_false:$($_.Exception.Message)") }

    foreach ($item in @($Context.install)) {
        try {
            $target = [string]$item.target
            $currentHash = Get-Sha256 $target
            if ($currentHash -and $currentHash -notin @([string]$item.deployed_sha256,[string]$item.original_sha256)) {
                throw "Installed payload changed outside the run: $target"
            }
            if ([bool]$item.existed) {
                Copy-Atomic ([string]$item.backup) $target
                if ((Get-Sha256 $target) -ne [string]$item.original_sha256) { throw "Restore hash mismatch: $target" }
            } elseif (Test-Path -LiteralPath $target -PathType Leaf) {
                if ((Get-Sha256 $target) -ne [string]$item.deployed_sha256) { throw "Refusing to remove changed payload: $target" }
                Remove-Item -LiteralPath $target -Force
            }
        } catch { $errors.Add("install_restore:$($_.Exception.Message)") }
    }

    foreach ($kind in @('active','inbox')) {
        try {
            $liveRoot = if ($kind -eq 'active') { Join-Path $runtimeRoot 'active' } else { Join-Path $runtimeRoot 'inbox' }
            $postRoot = Join-Path $Paths.Post $kind
            $filter = if ($kind -eq 'inbox') { '*.questpack' } else { '*' }
            $baseline = @($Context.$kind)
            if (Test-Path -LiteralPath $liveRoot) {
                foreach ($file in Get-ChildItem -LiteralPath $liveRoot -Filter $filter -File -Recurse) {
                    $relative = Get-Relative $liveRoot $file.FullName
                    $restored = @($baseline | Where-Object {
                        [string]$_.relative -ieq $relative -and [string]$_.sha256 -eq (Get-Sha256 $file.FullName)
                    }).Count -eq 1
                    if ($restored) { continue }
                    $postPath = Join-Path $postRoot $relative
                    if (Test-Path -LiteralPath $postPath -PathType Leaf) {
                        $postPath = Join-Path (Join-Path $Paths.Post ('retry-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ'))) (Join-Path $kind $relative)
                    }
                    Move-OwnedFile $file.FullName $postPath
                }
            }
            foreach ($item in $baseline) {
                $quarantine = [string]$item.quarantine
                $original = [string]$item.original
                if (Test-Path -LiteralPath $original -PathType Leaf) {
                    if ((Get-Sha256 $original) -ne [string]$item.sha256) { throw "Restored path contains unexpected bytes: $original" }
                    continue
                }
                if (-not (Test-Path -LiteralPath $quarantine -PathType Leaf)) { throw "Quarantined and restored file are both missing: $quarantine" }
                if ((Get-Sha256 $quarantine) -ne [string]$item.sha256) { throw "Quarantined file changed: $quarantine" }
                Move-OwnedFile $quarantine $original
                if ((Get-Sha256 $original) -ne [string]$item.sha256) { throw "Restored file hash mismatch: $original" }
            }
        } catch { $errors.Add("${kind}_restore:$($_.Exception.Message)") }
        if ($kind -eq 'active' -and $TestFaultDuringCleanup -and -not $PrepareFailure) {
            throw 'fixture_fault_during_cleanup'
        }
    }

    try {
        if (Test-Path -LiteralPath $configPath -PathType Leaf) {
            Copy-Atomic $configPath (Join-Path $Paths.Post 'runtime-config.after')
        }
        if ([bool]$Context.config.existed) {
            Copy-Atomic ([string]$Context.config.backup) $configPath
        } elseif (Test-Path -LiteralPath $configPath -PathType Leaf) {
            Remove-Item -LiteralPath $configPath -Force
        }
        Set-PrivateConfirmation $configPath $false
        if (Get-PrivateConfirmation $configPath) { throw 'PrivateWorldConfirmed remained true after cleanup.' }
    } catch { $errors.Add("config_restore:$($_.Exception.Message)") }

    $Context.state = if ($PrepareFailure) { 'cleaned_after_prepare_failure' } elseif ($errors.Count) { 'cleanup_incomplete' } else { 'cleaned' }
    $Context.cleanup_utc = [DateTimeOffset]::UtcNow.ToString('o')
    $Context.cleanup_errors = @($errors)
    Save-Context $Paths $Context
    if ($errors.Count) { throw "Cleanup incomplete: $($errors -join '; ')" }
    return [ordered]@{ schema='comfy-quest-validation-lap-cleanup/v1'; run_id=$RunId; verdict='restored'; safety_private_world_confirmed=$false }
}

function Invoke-Prepare {
    Assert-ValheimClosed 'validation-lap preparation'
    foreach ($required in @($runtimeDll,$contractsDll,$jsonDll)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Build output missing: $required" }
    }
    $paths = Get-RunPaths
    if (Test-Path -LiteralPath $paths.Root) { throw "RunId already exists: $RunId" }
    New-Item -ItemType Directory -Force -Path $paths.Root | Out-Null
    $sentinel = [ordered]@{ schema='comfy-quest-validation-lap-sentinel/v1'; run_id=$RunId; repo_root=$repoRoot; valheim_root=$ValheimRoot }
    Write-Json $paths.Sentinel $sentinel
    New-Item -ItemType Directory -Force -Path $paths.Quarantine,$paths.Backup,$paths.Post | Out-Null

    $install = @()
    foreach ($source in @($runtimeDll,$contractsDll,$jsonDll)) {
        $name = [IO.Path]::GetFileName($source)
        $target = Join-Path $pluginRoot $name
        $backup = Join-Path $paths.Backup ('plugins\' + $name)
        $existed = Test-Path -LiteralPath $target -PathType Leaf
        if ($existed) { Copy-Atomic $target $backup }
        $install += [ordered]@{
            name=$name; source=$source; target=$target; backup=$backup; existed=$existed
            original_sha256=$(if($existed){Get-Sha256 $target}else{$null})
            deployed_sha256=Get-Sha256 $source
        }
    }
    $configExisted = Test-Path -LiteralPath $configPath -PathType Leaf
    $configBackup = Join-Path $paths.Backup 'runtime-config.before'
    if ($configExisted) { Copy-Atomic $configPath $configBackup }
    $active = @()
    $activeRoot = Join-Path $runtimeRoot 'active'
    if (Test-Path -LiteralPath $activeRoot) {
        foreach ($file in Get-ChildItem -LiteralPath $activeRoot -File -Recurse) {
            $active += New-FileManifest $file.FullName (Join-Path $paths.Quarantine 'active') $activeRoot
        }
    }
    $inbox = @()
    $inboxRoot = Join-Path $runtimeRoot 'inbox'
    if (Test-Path -LiteralPath $inboxRoot) {
        foreach ($file in Get-ChildItem -LiteralPath $inboxRoot -Filter *.questpack -File) {
            $inbox += New-FileManifest $file.FullName (Join-Path $paths.Quarantine 'inbox') $inboxRoot
        }
    }
    $receiptsRoot = Join-Path $runtimeRoot 'receipts'
    $receiptBaseline = if (Test-Path -LiteralPath $receiptsRoot) { @(Get-ChildItem -LiteralPath $receiptsRoot -Filter *.json -File | ForEach-Object Name) } else { @() }
    $logHash = Get-Sha256 $logPath
    $context = [pscustomobject][ordered]@{
        schema='comfy-quest-validation-lap-context/v1'; run_id=$RunId; state='preparing'
        repo_root=$repoRoot; commit=(git rev-parse HEAD); valheim_root=$ValheimRoot
        created_utc=[DateTimeOffset]::UtcNow.ToString('o'); updated_utc=$null
        prepared_utc=$null; armed_utc=$null; cleanup_utc=$null
        config=[ordered]@{existed=$configExisted; backup=$configBackup; original_sha256=$(if($configExisted){Get-Sha256 $configPath}else{$null})}
        install=$install; active=$active; inbox=$inbox; receipt_baseline=$receiptBaseline
        log_baseline_sha256=$logHash; revisions=[pscustomobject]@{}; activations=[pscustomobject]@{}
        cleanup_errors=@()
    }
    Save-Context $paths $context
    try {
        Set-PrivateConfirmation $configPath $false
        foreach ($item in @($active)) { Move-OwnedFile $item.original $item.quarantine }
        foreach ($item in @($inbox)) { Move-OwnedFile $item.original $item.quarantine }
        if ($TestFaultAfter -eq 'Quarantine') { throw 'fixture_fault_after_quarantine' }
        foreach ($item in @($install)) { Copy-Atomic $item.source $item.target }
        if ($TestFaultAfter -eq 'Deploy') { throw 'fixture_fault_after_deploy' }
        foreach ($item in @($install)) {
            if ((Get-Sha256 $item.target) -ne [string]$item.deployed_sha256) { throw "Deployment hash mismatch: $($item.target)" }
        }
        $context.state = 'prepared'
        $context.prepared_utc = [DateTimeOffset]::UtcNow.ToString('o')
        Save-Context $paths $context
        return [ordered]@{
            schema='comfy-quest-validation-lap-prepare/v1'; run_id=$RunId; verdict='prepared'
            private_world_confirmed=$false; quarantined_inbox_count=@($inbox).Count
            quarantined_active_file_count=@($active).Count
            deployment=@($install | ForEach-Object { [ordered]@{name=$_.name;sha256=$_.deployed_sha256} })
        }
    } catch {
        $failure = $_.Exception.Message
        try { Restore-Run $paths $context $true | Out-Null } catch { $failure += '; automatic_cleanup: ' + $_.Exception.Message }
        throw "Prepare failed and stopped before player interaction: $failure"
    }
}

function Invoke-ValidateRevision {
    if ([string]::IsNullOrWhiteSpace($ExpectedVersion)) { throw 'ValidateRevision requires -ExpectedVersion.' }
    $paths = Get-RunPaths
    $context = Read-Context $paths
    if ($context.state -notin @('prepared','r1_validated','armed','running','r2_validated')) {
        throw "Revision validation is not allowed from state $($context.state)."
    }
    Import-ContractAssemblies
    $store = [ComfyQuestContracts.QuestPackStore]::new($runtimeRoot)
    $candidates = @($store.CheckInbox())
    $wantedCount = if ($ExpectedVersion -eq '1.0.0') { 1 } else { 2 }
    if ($candidates.Count -ne $wantedCount) { throw "Expected $wantedCount isolated questpack(s), found $($candidates.Count)." }
    if (@($candidates | Where-Object { -not $_.IsValid }).Count) { throw 'At least one isolated questpack failed Contracts inspection.' }
    $candidate = @($candidates | Where-Object { $_.Manifest.Version -eq $ExpectedVersion })
    if ($candidate.Count -ne 1) { throw "Expected exactly one $ExpectedVersion candidate, found $($candidate.Count)." }
    $identity = Assert-WoodboundCandidate $candidate[0] $ExpectedVersion
    $label = if ($ExpectedVersion -eq '1.0.0') { 'r1' } else { 'r2' }
    if ($label -eq 'r2') {
        $r1 = Get-Revision $context 'r1'
        if ($identity.pack_id -ne $r1.pack_id -or $identity.experience_id -ne $r1.experience_id) {
            throw 'r2 changed the pack or experience identity.'
        }
        if ($identity.content_hash -ieq $r1.content_hash) { throw 'r2 did not produce a fresh content hash.' }
        if ($identity.normalized_experience_sha256 -ne $r1.normalized_experience_sha256) {
            throw 'r2 changed more than the final message and version.'
        }
    }
    $context.revisions | Add-Member -NotePropertyName $label -NotePropertyValue ([pscustomobject]$identity) -Force
    $context.state = $label + '_validated'
    Save-Context $paths $context
    return [ordered]@{schema='comfy-quest-validation-lap-revision/v1';run_id=$RunId;verdict='valid';label=$label;identity=$identity}
}

function Invoke-ArmPrivateWorld {
    Assert-ValheimClosed 'private-world arming'
    $paths = Get-RunPaths
    $context = Read-Context $paths
    if ($context.state -ne 'r1_validated') { throw 'ArmPrivateWorld requires a machine-validated r1 first.' }
    # Re-inspect immediately before changing the safety state; no stale validation is trusted.
    $ExpectedVersion = '1.0.0'
    $null = Invoke-ValidateRevision
    $context = Read-Context $paths
    Set-PrivateConfirmation $configPath $true
    if (-not (Get-PrivateConfirmation $configPath)) { throw 'PrivateWorldConfirmed did not become true.' }
    $context.state = 'armed'
    $context.armed_utc = [DateTimeOffset]::UtcNow.ToString('o')
    Save-Context $paths $context
    return [ordered]@{
        schema='comfy-quest-validation-lap-arm/v1';run_id=$RunId;verdict='armed'
        private_world_confirmed=$true
        next='Launch Valheim and enter the explicitly confirmed private solo world. Do not press F10 yet.'
    }
}

function Invoke-Monitor {
    if ([string]::IsNullOrWhiteSpace($Expectation)) { throw 'Monitor requires -Expectation.' }
    $paths = Get-RunPaths
    $context = Read-Context $paths
    $deadline = [DateTimeOffset]::UtcNow.AddSeconds($MachineTimeoutSeconds)
    do {
        $rows = @(Read-NewReceipts $context)
        $proof = Find-Expectation $Expectation $context $rows
        if ($null -ne $proof) {
            if ($Expectation -in @('load_r1','load_r2')) {
                $label = if ($Expectation -eq 'load_r2') { 'r2' } else { 'r1' }
                if ($label -eq 'r2') {
                    $r1Property = $context.activations.PSObject.Properties['r1']
                    $r1Activation = if ($null -ne $r1Property) { $r1Property.Value } else { $null }
                    if ($r1Activation -and $proof.activation_id -eq $r1Activation) { throw 'r2 reused the r1 activation epoch.' }
                }
                $context.activations | Add-Member -NotePropertyName $label -NotePropertyValue $proof.activation_id -Force
            }
            if ($Expectation -eq 'startup') { $context.state = 'running' }
            Save-Context $paths $context
            return [ordered]@{schema='comfy-quest-validation-lap-monitor/v1';run_id=$RunId;verdict='proved';expectation=$Expectation;proof=$proof}
        }
        if (-not $ActionObserved) {
            return [ordered]@{
                schema='comfy-quest-validation-lap-monitor/v1';run_id=$RunId;verdict='waiting_for_player'
                expectation=$Expectation;machine_timer_started=$false
                note='Human pacing has no timeout. Re-run with -ActionObserved only after the action is complete.'
            }
        }
        if ([DateTimeOffset]::UtcNow -ge $deadline) { break }
        Start-Sleep -Milliseconds 250
    } while ($true)
    throw "Machine receipt timeout after $MachineTimeoutSeconds seconds for $Expectation. Stop; do not repeat the player action."
}

function Invoke-Status {
    $paths = Get-RunPaths
    $context = Read-Context $paths
    $parseError = $null
    $receiptCount = 0
    try { $receiptCount = @(Read-NewReceipts $context).Count } catch { $parseError = $_.Exception.Message }
    return [ordered]@{
        schema='comfy-quest-validation-lap-status/v1';run_id=$RunId;state=$context.state
        valheim_running=(Test-ValheimRunning);private_world_confirmed=(Get-PrivateConfirmation $configPath)
        inbox=@(if(Test-Path (Join-Path $runtimeRoot 'inbox')){Get-ChildItem (Join-Path $runtimeRoot 'inbox') -Filter *.questpack -File | ForEach-Object Name}else{@()})
        new_receipt_count=$receiptCount;strict_parse_error=$parseError
        revisions=$context.revisions;activations=$context.activations
    }
}

Assert-SafeLayout
Assert-FixtureSeams
if ($PlanOnly -or $Action -eq 'Plan') {
    [ordered]@{
        schema='comfy-quest-validation-lap-plan/v1';topology='omen_private_solo'
        actions=@('Preflight','Prepare','ValidateRevision','ArmPrivateWorld','Monitor','Status','Cleanup')
        safety='PrivateWorldConfirmed stays false through preparation and returns false on cleanup.'
        player='No keyboard action is requested until the preceding machine proof passes.'
        experience='Speak to wake the Charm, offer Wood twice within 30 seconds, then reclaim Wood to seal the rite.'
    } | ConvertTo-Json -Depth 8
    exit 0
}

& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }

$result = switch ($Action) {
    'Preflight' { Invoke-Preflight }
    'Prepare' { Invoke-Prepare }
    'ValidateRevision' { Invoke-ValidateRevision }
    'ArmPrivateWorld' { Invoke-ArmPrivateWorld }
    'Monitor' { Invoke-Monitor }
    'Status' { Invoke-Status }
    'Cleanup' {
        $paths = Get-RunPaths
        $context = Read-Context $paths
        Restore-Run $paths $context
    }
}
$result | ConvertTo-Json -Depth 20
