#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$ValheimRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim',
    [ValidatePattern('^-?[0-9]{1,20}$')]
    [string]$WorldUid = '-7600395338659582326',
    [ValidatePattern('^[A-Za-z0-9._ -]{1,80}$')]
    [string]$WorldName = 'ComfyQuestDemo',
    [ValidatePattern('^[A-Za-z0-9._-]{1,80}$')]
    [string]$ExpectedMachine = $env:COMPUTERNAME,
    [ValidatePattern('^[A-Za-z0-9._-]{1,80}$')]
    [string]$SessionId,
    [string]$EvidenceRoot,
    [string]$DotNet,
    [switch]$Headed,
    [switch]$SkipBrowserInstall,
    [switch]$Resume
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
if (-not $Resume) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $creatorScript Prepare `
        -SessionId $SessionId `
        -ValheimRoot $ValheimRoot `
        -ExpectedMachine $ExpectedMachine `
        -WorldUid $WorldUid `
        -WorldName $WorldName `
        -EvidenceRoot $EvidenceRoot
    if ($LASTEXITCODE -ne 0) { throw "Creator Session Prepare failed with exit code $LASTEXITCODE." }
} else {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $creatorScript Status `
        -SessionId $SessionId `
        -ValheimRoot $ValheimRoot `
        -ExpectedMachine $ExpectedMachine `
        -WorldUid $WorldUid
    if ($LASTEXITCODE -ne 0) { throw "Creator Session resume preflight failed with exit code $LASTEXITCODE." }
}

$names = @(
    'COMFY_QUEST_INSTALLED_GUILD_E2E',
    'COMFY_QUEST_CREATOR_SESSION_ID',
    'COMFY_QUEST_E2E_EVIDENCE_ROOT',
    'COMFY_QUEST_E2E_VALHEIM_ROOT',
    'COMFY_QUEST_E2E_WORLD_UID',
    'COMFY_QUEST_E2E_MACHINE')
$previous = @{}
foreach ($name in $names) {
    $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process')
}

try {
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_INSTALLED_GUILD_E2E', '1', 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_CREATOR_SESSION_ID', $SessionId, 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_E2E_EVIDENCE_ROOT', $EvidenceRoot, 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_E2E_VALHEIM_ROOT', $ValheimRoot, 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_E2E_WORLD_UID', $WorldUid, 'Process')
    [Environment]::SetEnvironmentVariable('COMFY_QUEST_E2E_MACHINE', $ExpectedMachine, 'Process')

    $testParameters = @{
        Filter = 'FullyQualifiedName=Comfy.Quest.Studio.E2E.Tests.QuestStudioSyntheticE2ETests.Installed_guild_journey_proves_A_to_B_to_A_reset_retention_and_recovery'
        KeepArtifacts = $true
    }
    if ($DotNet) { $testParameters.DotNet = $DotNet }
    if ($Headed) { $testParameters.Headed = $true }
    if ($SkipBrowserInstall) { $testParameters.SkipBrowserInstall = $true }
    & (Join-Path $repoRoot 'tools\quest-studio\Test-QuestStudioE2E.ps1') @testParameters
    if ($LASTEXITCODE -ne 0) {
        throw "Installed guild journey failed with exit code $LASTEXITCODE. The Creator Session remains owned for evidence-preserving recovery."
    }
} finally {
    foreach ($name in $names) {
        [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process')
    }
}

[pscustomobject]@{
    schema = 'comfy-quest-installed-guild-driver-result/v1'
    state = 'machine_lap_complete'
    session_id = $SessionId
    evidence_root = $EvidenceRoot
    seat_gate = 'pending_human_judgment'
    close_command = "tools\creator-session\Invoke-CreatorSession.ps1 Close -SessionId $SessionId"
} | ConvertTo-Json -Depth 4
