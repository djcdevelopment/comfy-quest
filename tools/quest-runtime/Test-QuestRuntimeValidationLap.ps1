#Requires -Version 5.1
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }

$harness = Join-Path $PSScriptRoot 'Invoke-QuestRuntimeValidationLap.ps1'
$parseErrors = $null
$tokens = $null
[Management.Automation.Language.Parser]::ParseFile($harness,[ref]$tokens,[ref]$parseErrors) | Out-Null
if ($parseErrors.Count) { throw "Harness parser errors: $($parseErrors.Message -join '; ')" }

$plan = & $harness -PlanOnly | ConvertFrom-Json
if ($plan.schema -ne 'comfy-quest-validation-lap-plan/v1') { throw 'Harness plan schema mismatch.' }
foreach ($action in @('Preflight','Prepare','ValidateRevision','ArmPrivateWorld','Monitor','ConfirmActivation','Cleanup')) {
    if ($action -notin @($plan.actions)) { throw "Harness plan is missing $action." }
}

$captures = Join-Path $repoRoot 'captures\quest-runtime-validation-selftest'
$fixtureRoot = Join-Path $captures ([guid]::NewGuid().ToString('N'))
$evidenceRoot = Join-Path $fixtureRoot 'evidence'

function Write-Bytes([string] $Path, [string] $Value) {
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    [IO.File]::WriteAllBytes($Path,[Text.Encoding]::UTF8.GetBytes($Value))
}

function Hash([string] $Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-True([bool] $Value, [string] $Message) {
    if (-not $Value) { throw $Message }
}

function Test-ChildPathSafe([string] $Parent, [string] $Child) {
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\','/') + [IO.Path]::DirectorySeparatorChar
    $childFull = [IO.Path]::GetFullPath($Child)
    return $childFull.StartsWith($parentFull,[StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath (Join-Path $childFull 'Valheim-normal\.comfy-quest-validation-fixture'))
}

function New-WoodboundPack([string] $RuntimeRoot, [string] $Version, [string] $FinalMessage) {
    $jsonDll = Join-Path $repoRoot 'network\mod\ComfyQuestRuntime\bin\Release\net48\Newtonsoft.Json.dll'
    $contractsDll = Join-Path $repoRoot 'network\mod\ComfyQuestContracts\bin\Release\netstandard2.0\ComfyQuestContracts.dll'
    if (-not ('Newtonsoft.Json.JsonConvert' -as [type])) { Add-Type -Path $jsonDll }
    if (-not ('ComfyQuestContracts.QuestPackContent' -as [type])) { Add-Type -Path $contractsDll }
    $message1 = 'The charm wakes. Two offerings of wood, before the moment passes.'
    $message2 = 'The offering is heard. Reclaim one piece to seal the rite.'
    $experience = [ordered]@{
        schema='comfy-quest-experience/v1';id='woodbound-selftest';title='The Woodbound Signal';entry_stage='speak'
        stages=@(
            [ordered]@{id='speak';entry_actions=@();transitions=@([ordered]@{
                id='advance-speak';priority=100;when=[ordered]@{op='EVENT';event='chat_sent';target='normal'}
                actions=@([ordered]@{id='message-wake';type='message';text=$message1});next_stage='offer';outcome=$null})},
            [ordered]@{id='offer';entry_actions=@();transitions=@([ordered]@{
                id='advance-offer';priority=100;when=[ordered]@{op='COUNT';count=2;within_seconds=30;children=@([ordered]@{op='EVENT';event='item_dropped';target='Wood'})}
                actions=@([ordered]@{id='message-offering';type='message';text=$message2});next_stage='reclaim';outcome=$null})},
            [ordered]@{id='reclaim';entry_actions=@();transitions=@([ordered]@{
                id='advance-reclaim';priority=100;when=[ordered]@{op='EVENT';event='item_picked_up';target='Wood'}
                actions=@([ordered]@{id='message-seal';type='message';text=$FinalMessage});next_stage=$null;outcome='complete'})}
        )
        bindings=@([ordered]@{id='default';experience_id='woodbound-selftest';target_kinds=@('sign','player_built_piece','item_stand','dedicated_charm')})
    }
    $experienceJson = ($experience | ConvertTo-Json -Depth 30)
    $experienceBytes = [Text.Encoding]::UTF8.GetBytes($experienceJson)
    $pair = [System.Collections.Generic.KeyValuePair[string,byte[]]]::new('experiences/woodbound.json',$experienceBytes)
    $listType = [System.Collections.Generic.List[System.Collections.Generic.KeyValuePair[string,byte[]]]]
    $content = [Activator]::CreateInstance($listType)
    $content.Add($pair)
    $contentHash = [ComfyQuestContracts.QuestPackContent]::ComputeHash($content)
    $manifestJson = ([ordered]@{schema='comfy-quest-pack/v2';pack_id='woodbound-selftest';version=$Version;content_hash=$contentHash} | ConvertTo-Json -Depth 5)
    $inbox = Join-Path $RuntimeRoot 'inbox'
    New-Item -ItemType Directory -Force -Path $inbox | Out-Null
    $pack = Join-Path $inbox ("woodbound-selftest-$Version.questpack")
    Add-Type -AssemblyName System.IO.Compression
    $output = [IO.File]::Open($pack,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    $archive = [IO.Compression.ZipArchive]::new($output,[IO.Compression.ZipArchiveMode]::Create,$false)
    try {
        foreach ($item in @(
            [pscustomobject]@{Name='manifest.json';Bytes=[Text.Encoding]::UTF8.GetBytes($manifestJson)},
            [pscustomobject]@{Name='experiences/woodbound.json';Bytes=$experienceBytes})) {
            $entry = $archive.CreateEntry($item.Name,[IO.Compression.CompressionLevel]::Optimal)
            $entryStream = $entry.Open()
            try { $entryStream.Write($item.Bytes,0,$item.Bytes.Length) } finally { $entryStream.Dispose() }
        }
    } finally { $archive.Dispose(); $output.Dispose() }
    return $pack
}

function New-Fixture([string] $Name) {
    $valheim = Join-Path $fixtureRoot $Name
    New-Item -ItemType Directory -Force -Path $valheim | Out-Null
    Write-Bytes (Join-Path $valheim '.comfy-quest-validation-fixture') "owned fixture`n"
    $plugins = Join-Path $valheim 'BepInEx\plugins'
    foreach ($name in @('ComfyQuestRuntime.dll','ComfyQuestContracts.dll','Newtonsoft.Json.dll')) {
        Write-Bytes (Join-Path $plugins $name) ("original-$Name-$name`n")
    }
    $config = Join-Path $valheim 'BepInEx\config\djcdevelopment.valheim.comfyquestruntime.cfg'
    Write-Bytes $config "[Safety]`nPrivateWorldConfirmed = true`n[Studio]`nUrl = http://127.0.0.1:8085/quest-studio`n"
    $runtime = Join-Path $valheim 'BepInEx\config\comfy-quest-runtime'
    Write-Bytes (Join-Path $runtime 'active\active-set.json') "original-active-$Name`n"
    Write-Bytes (Join-Path $runtime 'active\active-set.previous.json') "original-previous-$Name`n"
    Write-Bytes (Join-Path $runtime 'inbox\historical-9.9.9.questpack') "original-pack-$Name`n"
    # state/ carries pending workflow transitions plus the atomic writer's .previous sibling;
    # session 1 of the Phase 3 exit lap proved leaving them live replays them at startup.
    Write-Bytes (Join-Path $runtime 'state\workflow-states.json') "original-state-$Name`n"
    Write-Bytes (Join-Path $runtime 'state\workflow-states.json.previous') "original-state-previous-$Name`n"
    Write-Bytes (Join-Path $runtime 'inbox-dev\historical-dev-0.0.1.questpack') "original-dev-pack-$Name`n"
    Write-Bytes (Join-Path $runtime 'receipts\baseline.json') "baseline-$Name`n"
    return [pscustomobject]@{
        Valheim=$valheim; Plugins=$plugins; Config=$config; Runtime=$runtime
        Hashes=[ordered]@{
            Runtime=Hash (Join-Path $plugins 'ComfyQuestRuntime.dll')
            Contracts=Hash (Join-Path $plugins 'ComfyQuestContracts.dll')
            Json=Hash (Join-Path $plugins 'Newtonsoft.Json.dll')
            Active=Hash (Join-Path $runtime 'active\active-set.json')
            Previous=Hash (Join-Path $runtime 'active\active-set.previous.json')
            Pack=Hash (Join-Path $runtime 'inbox\historical-9.9.9.questpack')
            State=Hash (Join-Path $runtime 'state\workflow-states.json')
            StatePrevious=Hash (Join-Path $runtime 'state\workflow-states.json.previous')
            DevPack=Hash (Join-Path $runtime 'inbox-dev\historical-dev-0.0.1.questpack')
        }
    }
}

function Assert-Restored([object] $Fixture) {
    Assert-True ((Hash (Join-Path $Fixture.Plugins 'ComfyQuestRuntime.dll')) -eq $Fixture.Hashes.Runtime) 'Runtime DLL was not restored byte-for-byte.'
    Assert-True ((Hash (Join-Path $Fixture.Plugins 'ComfyQuestContracts.dll')) -eq $Fixture.Hashes.Contracts) 'Contracts DLL was not restored byte-for-byte.'
    Assert-True ((Hash (Join-Path $Fixture.Plugins 'Newtonsoft.Json.dll')) -eq $Fixture.Hashes.Json) 'Newtonsoft DLL was not restored byte-for-byte.'
    Assert-True ((Hash (Join-Path $Fixture.Runtime 'active\active-set.json')) -eq $Fixture.Hashes.Active) 'Active set was not restored byte-for-byte.'
    Assert-True ((Hash (Join-Path $Fixture.Runtime 'active\active-set.previous.json')) -eq $Fixture.Hashes.Previous) 'Previous active set was not restored byte-for-byte.'
    Assert-True ((Hash (Join-Path $Fixture.Runtime 'inbox\historical-9.9.9.questpack')) -eq $Fixture.Hashes.Pack) 'Historical questpack was not restored byte-for-byte.'
    Assert-True ((Hash (Join-Path $Fixture.Runtime 'state\workflow-states.json')) -eq $Fixture.Hashes.State) 'Workflow state was not restored byte-for-byte.'
    Assert-True ((Hash (Join-Path $Fixture.Runtime 'state\workflow-states.json.previous')) -eq $Fixture.Hashes.StatePrevious) 'Workflow state .previous sibling was not restored byte-for-byte.'
    Assert-True ((Hash (Join-Path $Fixture.Runtime 'inbox-dev\historical-dev-0.0.1.questpack')) -eq $Fixture.Hashes.DevPack) 'Dev-lane questpack was not restored byte-for-byte.'
    $configText = [IO.File]::ReadAllText($Fixture.Config)
    Assert-True ($configText -match '(?m)^PrivateWorldConfirmed = false$') 'Safety was not normalized to false.'
    Assert-True ($configText -match '(?m)^Url = http://127\.0\.0\.1:8085/quest-studio$') 'Unrelated config content was not preserved.'
}

$succeeded = $false
try {
    $normal = New-Fixture 'Valheim-normal'
    $normalRun = 'normal'
    $prepare = & $harness -Action Prepare -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode | ConvertFrom-Json
    Assert-True ($prepare.verdict -eq 'prepared') 'Fixture Prepare did not pass.'
    Assert-True ($prepare.quarantined_inbox_count -eq 1) 'Prepare did not quarantine the historical pack.'
    Assert-True (-not (Test-Path (Join-Path $normal.Runtime 'inbox\historical-9.9.9.questpack'))) 'Historical pack remained live after Prepare.'
    Assert-True (-not (Test-Path (Join-Path $normal.Runtime 'active\active-set.json'))) 'Historical active state remained live after Prepare.'
    Assert-True ($prepare.quarantined_state_file_count -eq 2) 'Prepare did not quarantine the workflow state files.'
    Assert-True (-not (Test-Path (Join-Path $normal.Runtime 'state\workflow-states.json'))) 'Historical workflow state remained live after Prepare.'
    Assert-True (-not (Test-Path (Join-Path $normal.Runtime 'state\workflow-states.json.previous'))) 'Historical workflow state sibling remained live after Prepare.'
    Assert-True ($prepare.quarantined_inbox_dev_count -eq 1) 'Prepare did not quarantine the dev-lane pack.'
    Assert-True (-not (Test-Path (Join-Path $normal.Runtime 'inbox-dev\historical-dev-0.0.1.questpack'))) 'Historical dev-lane pack remained live after Prepare.'
    Assert-True (-not ([IO.File]::ReadAllText($normal.Config) -match '(?m)^PrivateWorldConfirmed = true$')) 'Prepare left safety true.'
    Assert-True ((Hash (Join-Path $normal.Plugins 'ComfyQuestRuntime.dll')) -eq (Hash (Join-Path $repoRoot 'network\mod\ComfyQuestRuntime\bin\Release\net48\ComfyQuestRuntime.dll'))) 'Prepare deployed the wrong Runtime bytes.'

    New-WoodboundPack $normal.Runtime '1.0.0' 'The circuit closes. The charm remembers this telling.' | Out-Null
    $r1 = & $harness -Action ValidateRevision -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode -ExpectedVersion 1.0.0 | ConvertFrom-Json
    Assert-True ($r1.verdict -eq 'valid' -and $r1.label -eq 'r1') 'Exact r1 contract validation failed.'
    $armed = & $harness -Action ArmPrivateWorld -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode | ConvertFrom-Json
    Assert-True ($armed.verdict -eq 'armed') 'Fixture ArmPrivateWorld did not pass.'
    Assert-True ([IO.File]::ReadAllText($normal.Config) -match '(?m)^PrivateWorldConfirmed = true$') 'ArmPrivateWorld did not open the safety window.'
    # Session 1 of the Phase 3 exit lap staged the second revision before the first had
    # ever been activated and the update beat collapsed. The machine now refuses that
    # order: no r2 blessing, and no staging go-ahead, until the r1 activation is proven.
    New-WoodboundPack $normal.Runtime '1.0.1' 'The circuit closes. The Charm remembers a new telling.' | Out-Null
    $confirmEarly = & $harness -Action ConfirmActivation -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode | ConvertFrom-Json
    Assert-True ($confirmEarly.verdict -eq 'no_activation' -and -not $confirmEarly.stage_second_revision) 'ConfirmActivation approved staging before any r1 activation.'
    $prestaged = $false
    try {
        & $harness -Action ValidateRevision -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode -ExpectedVersion 1.0.1 | Out-Null
    } catch { $prestaged = $_.Exception.Message -match 'waits on r1 activation evidence' }
    Assert-True $prestaged 'r2 validation did not wait for r1 activation evidence.'

    $waitingStarted = Get-Date
    $waiting = & $harness -Action Monitor -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode -Expectation startup | ConvertFrom-Json
    Assert-True ($waiting.verdict -eq 'waiting_for_player') 'Monitor did not preserve unbounded human pacing.'
    Assert-True (((Get-Date) - $waitingStarted).TotalSeconds -lt 1) 'Human pacing unexpectedly started a machine timeout.'

    Write-Bytes (Join-Path $normal.Runtime 'receipts\valid.json') @"
{
  "schema": "comfy-quest-runtime-receipt/v1",
  "id": "selftest-valid-receipt",
  "at_utc": "2026-08-19T13:47:13.1491434+00:00",
  "operation": "transition",
  "status": "rejected",
  "error": "active_set_missing",
  "diagnostics": []
}
"@
    $validStatus = & $harness -Action Status -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode | ConvertFrom-Json
    Assert-True ($validStatus.new_receipt_count -eq 1 -and -not $validStatus.strict_parse_error) 'Monitor did not preserve a valid receipt as one JSON object.'

    Write-Bytes (Join-Path $normal.Runtime 'receipts\malformed.json') "{`n  `"schema`": `"comfy-quest-runtime-receipt/v1`",`n  `"schema`": `"duplicate`"`n}`n"
    $strictStopped = $false
    try {
        & $harness -Action Monitor -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode -Expectation startup | Out-Null
    } catch { $strictStopped = $_.Exception.Message -match 'Strict JSON parse failed' }
    Assert-True $strictStopped 'Monitor did not stop on duplicate JSON properties.'
    Remove-Item -LiteralPath (Join-Path $normal.Runtime 'receipts\malformed.json') -Force

    $timedOut = $false
    try {
        & $harness -Action Monitor -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode -Expectation startup -ActionObserved -MachineTimeoutSeconds 1 | Out-Null
    } catch { $timedOut = $_.Exception.Message -match 'Machine receipt timeout' }
    Assert-True $timedOut 'Observed-action monitoring did not stop on a machine timeout.'

    $fixtureLog = Join-Path $normal.Valheim 'BepInEx\LogOutput.log'
    Write-Bytes $fixtureLog "[Info] Runtime ready.`n"
    $liveWriter = [IO.File]::Open($fixtureLog,[IO.FileMode]::Open,[IO.FileAccess]::Write,[IO.FileShare]::ReadWrite)
    try {
        $startup = & $harness -Action Monitor -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode -Expectation startup -ActionObserved -MachineTimeoutSeconds 1 | ConvertFrom-Json
        Assert-True ($startup.verdict -eq 'proved' -and $startup.proof.proof -eq 'fresh Runtime ready log file') 'Monitor could not hash a writer-held live log.'
    } finally { $liveWriter.Dispose() }

    # A synthetic r1 activation — matching receipt plus agreeing active set — opens the gate.
    $activationId = 'act-20260820T123456789Z-0badcafe'
    Write-Bytes (Join-Path $normal.Runtime 'active\active-set.json') (([ordered]@{
        schema='comfy-quest-active-set/v1';pack_id=$r1.identity.pack_id;version='1.0.0'
        content_hash=$r1.identity.content_hash;package_sha256='';source='fixture'
        activated_utc='2026-08-20T12:34:56+00:00';activation_id=$activationId
    } | ConvertTo-Json -Depth 5) + "`n")
    Write-Bytes (Join-Path $normal.Runtime 'receipts\load-r1.json') (([ordered]@{
        schema='comfy-quest-runtime-receipt/v1';id='selftest-load-r1';at_utc='2026-08-20T12:34:56.0000000+00:00'
        operation='load';status='activated';pack_id=$r1.identity.pack_id;version='1.0.0'
        content_hash=$r1.identity.content_hash;activation_id=$activationId;diagnostics=@()
    } | ConvertTo-Json -Depth 5) + "`n")
    $loadProof = & $harness -Action Monitor -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode -Expectation load_r1 -ActionObserved -MachineTimeoutSeconds 1 | ConvertFrom-Json
    Assert-True ($loadProof.verdict -eq 'proved' -and $loadProof.proof.activation_id -eq $activationId) 'Monitor did not prove the synthetic r1 activation.'
    $confirm = & $harness -Action ConfirmActivation -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode | ConvertFrom-Json
    Assert-True ($confirm.verdict -eq 'activation_confirmed' -and $confirm.stage_second_revision) 'ConfirmActivation did not confirm the proven r1 activation.'
    $r2 = & $harness -Action ValidateRevision -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode -ExpectedVersion 1.0.1 | ConvertFrom-Json
    Assert-True ($r2.verdict -eq 'valid' -and $r2.label -eq 'r2') 'Exact r2 contract validation failed.'
    Assert-True ($r1.identity.content_hash -ne $r2.identity.content_hash) 'r2 content hash did not rotate.'

    Write-Bytes (Join-Path $normal.Runtime 'active\active-set.json') "lap-active`n"
    $cleanupInterrupted = $false
    try {
        & $harness -Action Cleanup -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode -TestFaultDuringCleanup | Out-Null
    } catch { $cleanupInterrupted = $_.Exception.Message -match 'fixture_fault_during_cleanup' }
    Assert-True $cleanupInterrupted 'Injected Cleanup interruption did not stop.'
    Assert-True (-not ([IO.File]::ReadAllText($normal.Config) -match '(?m)^PrivateWorldConfirmed = true$')) 'Interrupted Cleanup did not close the safety window first.'
    $cleanup = & $harness -Action Cleanup -RunId $normalRun -ValheimRoot $normal.Valheim -EvidenceRoot $evidenceRoot -FixtureMode | ConvertFrom-Json
    Assert-True ($cleanup.verdict -eq 'restored') 'Fixture Cleanup did not pass.'
    Assert-Restored $normal

    $interrupted = New-Fixture 'Valheim-interrupted'
    $faultStopped = $false
    try {
        & $harness -Action Prepare -RunId 'interrupted' -ValheimRoot $interrupted.Valheim -EvidenceRoot $evidenceRoot -FixtureMode -TestFaultAfter Quarantine | Out-Null
    } catch { $faultStopped = $_.Exception.Message -match 'fixture_fault_after_quarantine' }
    Assert-True $faultStopped 'Injected Prepare interruption did not stop.'
    Assert-Restored $interrupted
    $interruptedContext = Get-Content (Join-Path $evidenceRoot 'interrupted\context.json') -Raw | ConvertFrom-Json
    Assert-True ($interruptedContext.state -eq 'cleaned_after_prepare_failure') 'Interrupted Prepare did not record automatic cleanup.'

    $running = New-Fixture 'Valheim-running-guard'
    $runningStopped = $false
    try {
        & $harness -Action Prepare -RunId 'running-guard' -ValheimRoot $running.Valheim -EvidenceRoot $evidenceRoot -FixtureMode -TestPretendValheimRunning | Out-Null
    } catch { $runningStopped = $_.Exception.Message -match 'Valheim must be closed' }
    Assert-True $runningStopped 'Prepare did not refuse a running Valheim process.'
    Assert-True (-not (Test-Path (Join-Path $evidenceRoot 'running-guard'))) 'Running-game refusal created a run capture.'

    $succeeded = $true
    [ordered]@{
        schema='comfy-quest-validation-lap-self-test/v1';result='passed'
        checks=@('parser','plan','running-game refusal','quarantine','state quarantine','inbox-dev quarantine','deployed hashes','safety false','exact r1 contract','private-world arm','staging gate','activation confirm','r2-only final message','human pacing','valid receipt object','strict JSON','machine timeout','shared live log','interrupted cleanup','byte-exact restore')
    } | ConvertTo-Json -Depth 6
} finally {
    if ($succeeded -and (Test-ChildPathSafe $captures $fixtureRoot)) {
        Remove-Item -LiteralPath $fixtureRoot -Recurse -Force
    }
}
