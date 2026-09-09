#Requires -Version 5.1
<#
.SYNOPSIS
Establish the exact local Creator Session and Slayers fixture preconditions for Play Campaign.

.DESCRIPTION
This is the fixed machine-owned front half of the first Guild campaign lap. It may prepare or
resume the repository-owned Creator Session, launch the pinned ComfyQuestDemo world when the
game is closed, arm Runtime, and ask Quest Lab for the parameter-free Signature Hunt fixture.
It cannot accept a world, character, prefab, quest, binding, command, or filesystem output path.
Studio consumes the returned exact fixture-owned binding ZDO, then publishes, binds, and starts.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^campaign-play-[A-Za-z0-9T._-]{1,60}$')]
    [string]$OperationId,

    [Parameter(Mandatory = $true)]
    [string]$ValheimRoot
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }

$ValheimRoot = [IO.Path]::GetFullPath($ValheimRoot).TrimEnd('\', '/')
if ([string]::IsNullOrWhiteSpace($ValheimRoot) -or
    [IO.Path]::GetPathRoot($ValheimRoot) -eq $ValheimRoot -or
    -not (Test-Path -LiteralPath (Join-Path $ValheimRoot 'valheim.exe') -PathType Leaf)) {
    throw "Campaign play Valheim root is unsafe or unavailable: $ValheimRoot"
}

$creatorScript = Join-Path $repoRoot 'tools\creator-session\Invoke-CreatorSession.ps1'
$labScript = Join-Path $repoRoot 'tools\questlab-batch\Invoke-I5QuestLabBatch.ps1'
$sessionPath = Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-creator\session.json'
$runtimeRoot = Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-runtime'
$operationRoot = Join-Path $repoRoot ("captures\campaign-play\" + $OperationId)
New-Item -ItemType Directory -Force -Path $operationRoot | Out-Null

function Invoke-BoundedEntryPoint([string]$Script, [string[]]$Arguments) {
    $output = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Script @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    if ($exitCode -ne 0) {
        $detail = (@($output) | Select-Object -Last 12) -join [Environment]::NewLine
        throw "Bounded entrypoint $(Split-Path $Script -Leaf) failed ($exitCode): $detail"
    }
}

function Read-Session {
    if (-not (Test-Path -LiteralPath $sessionPath -PathType Leaf)) { return $null }
    try { return [IO.File]::ReadAllText($sessionPath) | ConvertFrom-Json }
    catch { throw 'Creator Session manifest is unreadable.' }
}

function Test-ValheimRunning {
    return $null -ne (Get-Process valheim -ErrorAction SilentlyContinue | Select-Object -First 1)
}

$session = Read-Session
if ($null -eq $session -or [string]$session.state -ne 'active') {
    if (Test-ValheimRunning) {
        throw 'campaign_play_prepare_requires_valheim_closed'
    }
    $sessionId = 'creator-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' +
        [Guid]::NewGuid().ToString('N').Substring(0, 8)
    Invoke-BoundedEntryPoint $creatorScript @(
        'Prepare', '-SessionId', $sessionId, '-ValheimRoot', $ValheimRoot)
    $session = Read-Session
    if ($null -eq $session -or [string]$session.state -ne 'active') {
        throw 'campaign_play_creator_session_prepare_missing'
    }
} else {
    $sessionRepo = [IO.Path]::GetFullPath([string]$session.repo_root).TrimEnd('\', '/')
    if (-not [string]::Equals($sessionRepo, $repoRoot, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'campaign_play_creator_session_repository_mismatch'
    }
}

$sessionId = [string]$session.session_id
$machine = [string]$session.expected_machine
$worldUid = [string]$session.world_uid
if ([string]::IsNullOrWhiteSpace($sessionId) -or
    [string]::IsNullOrWhiteSpace($machine) -or
    $worldUid -ne '-7600395338659582326' -or
    [string]$session.world_name -ne 'ComfyQuestDemo' -or
    [string]$session.character_profile -ne 'questyfour') {
    throw 'campaign_play_creator_session_identity_invalid'
}

# Status is the executable resume contract. It revalidates the retained manifest and
# installed plugin/support hashes before we trust either a stopped or running session.
Invoke-BoundedEntryPoint $creatorScript @(
    'Status', '-SessionId', $sessionId, '-ValheimRoot', $ValheimRoot)

$resumed = Test-ValheimRunning
if (-not $resumed) {
    Invoke-BoundedEntryPoint $creatorScript @(
        'Launch', '-SessionId', $sessionId, '-ValheimRoot', $ValheimRoot)
} else {
    $worldEntryPath = Join-Path $runtimeRoot 'status\world-entry.json'
    if (-not (Test-Path -LiteralPath $worldEntryPath -PathType Leaf)) {
        throw 'campaign_play_running_world_entry_missing'
    }
    try { $worldEntry = [IO.File]::ReadAllText($worldEntryPath) | ConvertFrom-Json }
    catch { throw 'campaign_play_running_world_entry_unreadable' }
    if ([string]$worldEntry.schema -ne 'comfy-quest-world-entry-receipt/v1' -or
        [string]$worldEntry.state -ne 'entered' -or
        [string]$worldEntry.creator_session_id -ne $sessionId -or
        [string]$worldEntry.machine -ne $machine -or
        [string]$worldEntry.world_uid -ne $worldUid) {
        throw 'campaign_play_running_world_entry_mismatch'
    }
}

Invoke-BoundedEntryPoint $creatorScript @(
    'Arm', '-SessionId', $sessionId, '-ValheimRoot', $ValheimRoot)
Invoke-BoundedEntryPoint $labScript @(
    'showcase_prepare', '-Lane', 'omen', '-ExpectedMachine', $machine,
    '-ExpectedWorldUid', $worldUid, '-CreatorSessionId', $sessionId,
    '-OmenValheimRoot', $ValheimRoot, '-WaitSeconds', '60',
    '-OutputDirectory', (Join-Path $operationRoot 'showcase'))
Invoke-BoundedEntryPoint $labScript @(
    'signature_hunt_prepare',
    '-Lane', 'omen',
    '-ExpectedMachine', $machine,
    '-ExpectedWorldUid', $worldUid,
    '-CreatorSessionId', $sessionId,
    '-OmenValheimRoot', $ValheimRoot,
    '-WaitSeconds', '60',
    '-OutputDirectory', $operationRoot)

$fixtureFiles = @(Get-ChildItem -LiteralPath $operationRoot -Filter '*-fixture.json' -File)
$requestReceiptFiles = @(Get-ChildItem -LiteralPath $operationRoot -Filter '*-receipt.json' -File)
if ($fixtureFiles.Count -ne 1 -or $requestReceiptFiles.Count -ne 1) {
    throw 'campaign_play_fixture_evidence_missing_or_ambiguous'
}
$fixture = [IO.File]::ReadAllText($fixtureFiles[0].FullName) | ConvertFrom-Json
$requestReceipt = [IO.File]::ReadAllText($requestReceiptFiles[0].FullName) | ConvertFrom-Json
if ([string]$requestReceipt.state -ne 'completed' -or
    [string]$requestReceipt.operation -ne 'signature_hunt_prepare' -or
    [string]$fixture.schema -ne 'comfy-questlab-signature-hunt-fixture/v2' -or
    [string]$fixture.fixture_id -ne 'slayers-signature-hunt' -or
    [int]$fixture.fixture_revision -ne 5 -or
    [string]$fixture.state -ne 'ready' -or
    [string]$fixture.proof_level -ne 'fixture-preparation' -or
    [string]$fixture.request_id -ne [string]$requestReceipt.request_id -or
    [string]$fixture.machine -ne $machine -or
    [string]$fixture.world_name -ne 'ComfyQuestDemo' -or
    [string]$fixture.world_uid -ne $worldUid -or
    [int]$fixture.objects.expected -ne 3 -or
    [int]$fixture.objects.standing_at_capture -ne 3 -or
    @($fixture.targets).Count -ne 0 -or
    [string]$fixture.target_lifecycle -ne 'runtime-stage-entry' -or
    [string]$fixture.binding_anchor.role -ne 'marker-loadout-sign' -or
    [string]$fixture.binding_anchor.target_kind -ne 'sign' -or
    [string]$fixture.binding_anchor.zdo_id -notmatch '^-?[0-9]+:[0-9]+$') {
    throw 'campaign_play_fixture_evidence_invalid'
}
$fixtureReceiptSha256 = (Get-FileHash -LiteralPath $fixtureFiles[0].FullName -Algorithm SHA256).Hash.ToLowerInvariant()

$result = [ordered]@{
    schema = 'comfy-quest-studio-campaign-play-prerequisites/v1'
    operation_id = $OperationId
    state = 'ready'
    creator_session_id = $sessionId
    resumed_running_session = [bool]$resumed
    machine = $machine
    world_uid = $worldUid
    world_name = [string]$session.world_name
    character_profile = [string]$session.character_profile
    fixture_request_id = [string]$requestReceipt.request_id
    fixture_receipt_path = $fixtureFiles[0].FullName
    fixture_receipt_sha256 = $fixtureReceiptSha256
    fixture = $fixture
    limitations = @(
        [string]$fixture.disclaimer,
        'Campaign publication, binding, start, and completion are separate evidence.'
    )
}
$resultPath = Join-Path $operationRoot 'prerequisites.json'
[IO.File]::WriteAllText(
    $resultPath,
    (($result | ConvertTo-Json -Depth 16) + [Environment]::NewLine),
    (New-Object System.Text.UTF8Encoding($false)))
[Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
[Console]::Write([IO.File]::ReadAllText($resultPath))
