#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z0-9._-]{1,80}$')][string]$OperationId,
    [Parameter(Mandatory)][string]$ValheimRoot,
    [Parameter(Mandatory)][ValidatePattern('^signature-hunt-[A-Za-z0-9._-]{1,80}$')][string]$PreparationId
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
& (Join-Path $repoRoot 'tools/Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }
$ValheimRoot = [IO.Path]::GetFullPath($ValheimRoot).TrimEnd('\','/')
if (-not (Test-Path -LiteralPath "$ValheimRoot/valheim.exe" -PathType Leaf)) { throw 'valheim_not_found' }
$session = Get-Content -LiteralPath "$ValheimRoot/BepInEx/config/comfy-quest-creator/session.json" -Raw | ConvertFrom-Json
$entry = Get-Content -LiteralPath "$ValheimRoot/BepInEx/config/comfy-quest-runtime/status/world-entry.json" -Raw | ConvertFrom-Json
if ($session.state -ne 'active' -or $session.world_uid -ne '-7600395338659582326' -or
    [IO.Path]::GetFullPath($session.repo_root).TrimEnd('\','/') -ne $repoRoot.TrimEnd('\','/') -or
    $entry.state -ne 'entered' -or $entry.creator_session_id -ne $session.session_id -or
    $entry.machine -ne $session.expected_machine -or $entry.world_uid -ne $session.world_uid) {
    throw 'campaign_fixture_clear_session_mismatch'
}
$outputRoot = Join-Path $repoRoot "captures/campaign-reset/$OperationId"
if (Test-Path -LiteralPath $outputRoot) { throw 'campaign_fixture_clear_operation_already_exists' }
New-Item -ItemType Directory -Path $outputRoot | Out-Null
$creatorScript = Join-Path $repoRoot 'tools/creator-session/Invoke-CreatorSession.ps1'
$batchScript = Join-Path $repoRoot 'tools/questlab-batch/Invoke-I5QuestLabBatch.ps1'
function Invoke-Child([string]$Script,[string[]]$Arguments) {
    $result = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Script @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw ((@($result) | Select-Object -Last 10) -join [Environment]::NewLine) }
}
Invoke-Child $creatorScript @('Status','-SessionId',[string]$session.session_id,'-ValheimRoot',$ValheimRoot)
Invoke-Child $creatorScript @('Arm','-SessionId',[string]$session.session_id,'-ValheimRoot',$ValheimRoot)
function Invoke-Fixture([string]$Operation,[string]$Folder) {
    $directory = Join-Path $outputRoot $Folder
    Invoke-Child $batchScript @($Operation,'-Lane','omen','-ExpectedMachine',[string]$session.expected_machine,
        '-ExpectedWorldUid',[string]$session.world_uid,'-CreatorSessionId',[string]$session.session_id,
        '-OmenValheimRoot',$ValheimRoot,'-WaitSeconds','40','-OutputDirectory',$directory)
    $files = @(Get-ChildItem -LiteralPath $directory -Filter '*-receipt.json' -File)
    if ($files.Count -ne 1) { throw 'campaign_fixture_clear_receipt_missing' }
    $receipt = Get-Content -LiteralPath $files[0].FullName -Raw | ConvertFrom-Json
    if ($receipt.schema -ne 'comfy-questlab-batch-request-receipt/v1' -or $receipt.state -ne 'completed' -or
        $receipt.operation -ne $Operation -or $receipt.creator_session_id -ne $session.session_id -or
        $receipt.machine -ne $session.expected_machine -or $receipt.world_uid -ne $session.world_uid) {
        throw 'campaign_fixture_clear_receipt_invalid'
    }
    return $directory
}
$statusRoot = Invoke-Fixture 'signature_hunt_status' 'status'
$fixtures = @(Get-ChildItem -LiteralPath $statusRoot -Filter '*-fixture.json' -File)
if ($fixtures.Count -ne 1) { throw 'campaign_fixture_ownership_missing' }
$fixture = Get-Content -LiteralPath $fixtures[0].FullName -Raw | ConvertFrom-Json
if ($fixture.schema -notin @('comfy-questlab-signature-hunt-fixture/v1', 'comfy-questlab-signature-hunt-fixture/v2') -or
    $fixture.preparation_id -ne $PreparationId -or $fixture.world_uid -ne $session.world_uid -or
    $fixture.machine -ne $session.expected_machine) { throw 'campaign_fixture_ownership_changed' }
Invoke-Fixture 'signature_hunt_clear' 'clear' | Out-Null
[Console]::OutputEncoding = New-Object Text.UTF8Encoding($false)
[Console]::Write((@{state='cleared';preparation_id=$PreparationId} | ConvertTo-Json -Compress))
