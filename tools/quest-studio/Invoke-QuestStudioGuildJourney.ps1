#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ValheimRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim',
    [ValidatePattern('^-?[0-9]{1,20}$')]
    [string]$WorldUid = '-7600395338659582326',
    [ValidatePattern('^[A-Za-z0-9._ -]{1,80}$')]
    [string]$WorldName = 'ComfyQuestDemo',
    [ValidatePattern('^[A-Za-z0-9._-]{1,80}$')]
    [string]$CharacterProfile = 'questyfour',
    [string]$SteamExe = 'C:\Program Files (x86)\Steam\steam.exe',
    [ValidatePattern('^[A-Za-z0-9._-]{1,80}$')]
    [string]$ExpectedMachine = $env:COMPUTERNAME,
    [ValidatePattern('^[A-Za-z0-9._-]{1,80}$')]
    [string]$SessionId,
    [string]$EvidenceRoot,
    [string]$DotNet,
    [switch]$Headed,
    [switch]$SkipBrowserInstall,
    [switch]$Resume,
    [switch]$HumanWorldEntry,
    [switch]$KeepSessionOnFailure
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }

$ValheimRoot = [IO.Path]::GetFullPath($ValheimRoot).TrimEnd('\', '/')
if (-not (Test-Path -LiteralPath (Join-Path $ValheimRoot 'valheim.exe') -PathType Leaf)) {
    throw "Valheim executable not found under: $ValheimRoot"
}
if (-not $SessionId) {
    $SessionId = 'queue-full-width-journey-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ')
}
if (-not $EvidenceRoot) {
    $EvidenceRoot = Join-Path $repoRoot "captures\queue.full-width-journey\$SessionId"
}
$EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)

$dirty = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Could not inspect repository state.' }
if ($dirty.Count -ne 0) {
    throw 'Installed proof requires a clean committed checkout so the deployed bytes have an immutable source revision.'
}

$creatorScript = Join-Path $repoRoot 'tools\creator-session\Invoke-CreatorSession.ps1'
function Invoke-CreatorAction([string]$CreatorAction, [string[]]$Extra = @()) {
    $creatorArguments = @(
        '-SessionId', $SessionId,
        '-ValheimRoot', $ValheimRoot,
        '-ExpectedMachine', $ExpectedMachine,
        '-WorldUid', $WorldUid,
        '-WorldName', $WorldName,
        '-CharacterProfile', $CharacterProfile,
        '-SteamExe', $SteamExe) + $Extra
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $creatorScript $CreatorAction @creatorArguments
    if ($LASTEXITCODE -ne 0) {
        throw "Creator Session $CreatorAction failed with exit code $LASTEXITCODE."
    }
}

$sessionPrepared = $false
if (-not $Resume) {
    Invoke-CreatorAction 'Prepare' -Extra @('-EvidenceRoot', $EvidenceRoot)
    $sessionPrepared = $true
} else {
    Invoke-CreatorAction 'Status'
    $sessionPrepared = $true
}

$names = @(
    'COMFY_QUEST_INSTALLED_GUILD_E2E',
    'COMFY_QUEST_CREATOR_SESSION_ID',
    'COMFY_QUEST_E2E_EVIDENCE_ROOT',
    'COMFY_QUEST_E2E_VALHEIM_ROOT',
    'COMFY_QUEST_E2E_WORLD_UID',
    'COMFY_QUEST_E2E_MACHINE',
    'COMFY_QUEST_E2E_AUTOMATE_WORLD_ENTRY')
$previous = @{}
foreach ($name in $names) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

$lapComplete = $false
$lapError = $null
try {
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_INSTALLED_GUILD_E2E', '1', 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_CREATOR_SESSION_ID', $SessionId, 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_E2E_EVIDENCE_ROOT', $EvidenceRoot, 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_E2E_VALHEIM_ROOT', $ValheimRoot, 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_E2E_WORLD_UID', $WorldUid, 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_E2E_MACHINE', $ExpectedMachine, 'Process')
    [Environment]::SetEnvironmentVariable(
        'COMFY_QUEST_E2E_AUTOMATE_WORLD_ENTRY', $(if ($HumanWorldEntry) { '0' } else { '1' }), 'Process')

    $testParameters = @{
        Filter = 'FullyQualifiedName=Comfy.Quest.Studio.E2E.Tests.QuestStudioSyntheticE2ETests.Installed_guild_journey_proves_A_to_B_to_A_reset_retention_and_recovery'
        KeepArtifacts = $true
    }
    if ($DotNet) { $testParameters.DotNet = $DotNet }
    if ($Headed) { $testParameters.Headed = $true }
    if ($SkipBrowserInstall) { $testParameters.SkipBrowserInstall = $true }
    & (Join-Path $repoRoot 'tools\quest-studio\Test-QuestStudioE2E.ps1') @testParameters
    if ($LASTEXITCODE -ne 0) {
        throw "Installed guild journey failed with exit code $LASTEXITCODE."
    }
    $lapComplete = $true
} catch {
    $lapError = $_.Exception.Message
} finally {
    foreach ($name in $names) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}

$cleanupErrors = @()
if ($sessionPrepared) {
    if (Get-Process valheim -ErrorAction SilentlyContinue) {
        foreach ($cleanupAction in @('BuildOff', 'Disarm')) {
            try { Invoke-CreatorAction $cleanupAction -Extra @('-WaitSeconds', '15') }
            catch { $cleanupErrors += "$cleanupAction`: $($_.Exception.Message)" }
        }
    }
    try { Invoke-CreatorAction 'Stop' }
    catch { $cleanupErrors += "Stop: $($_.Exception.Message)" }
    if ($lapComplete -or -not $KeepSessionOnFailure) {
        try { Invoke-CreatorAction 'Close' -Extra @('-Restore', '-RestoreGameState') }
        catch { $cleanupErrors += "Close/Restore: $($_.Exception.Message)" }
    }
}

if ($lapError) {
    $cleanup = if ($cleanupErrors.Count -eq 0) { 'cleanup completed' } else { 'cleanup errors: ' + ($cleanupErrors -join '; ') }
    throw "$lapError ($cleanup)"
}
if ($cleanupErrors.Count -ne 0) {
    throw 'Installed guild journey completed, but machine-owned recovery failed: ' + ($cleanupErrors -join '; ')
}

[pscustomobject]@{
    schema = 'comfy-quest-installed-guild-driver-result/v1'
    state = 'machine_lap_complete'
    session_id = $SessionId
    evidence_root = $EvidenceRoot
    world_entry = if ($HumanWorldEntry) { 'human_boundary' } else { 'machine_owned' }
    session_state = if ($KeepSessionOnFailure -and -not $lapComplete) { 'active' } else { 'closed_install_and_game_state_restored' }
    human_contribution = if ($HumanWorldEntry) { 'launch_and_world_entry_only' } else { 'none_technical_lap' }
    seat_gate = 'not_applicable_to_technical_lap'
    next_lane = 'prepared_guild_campaign_with_authorship_and_play_feel_questions'
} | ConvertTo-Json -Depth 4
