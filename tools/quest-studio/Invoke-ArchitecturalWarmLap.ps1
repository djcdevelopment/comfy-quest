#Requires -Version 5.1
[CmdletBinding()]
param(
    [string]$SshTarget = 'homebase',
    [string]$RemoteValheimRoot = '/home/derek/valheim',
    [string]$RemoteUnityRoot = '/home/derek/.config/unity3d/IronGate/Valheim',
    [string]$ExpectedMachine = 'am4',
    [string]$ExpectedWorld = 'ComfyQuestDemo',
    [string]$ExpectedWorldDisplayName = 'Comfy Quest Demo',
    [string]$ExpectedWorldUid = '-7600395338659582326',
    [string]$ExpectedCharacter = 'questyfour',
    [string]$SessionId = 'architectural-tn0304-warm-rnd',
    [string]$DotNet = 'dotnet',
    [int]$WorldTimeoutSeconds = 600,
    [int]$RequestTimeoutSeconds = 90,
    [switch]$SkipTests,
    [switch]$Close
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$sourceEvidencePath = Join-Path $repoRoot 'docs\evidence\architectural-build-tn0304-20260829-r1.json'
$probe = Join-Path $repoRoot 'tools\quest-studio\architectural_live_probe.py'
$labProject = Join-Path $repoRoot 'network\mod\ComfyQuestLab\ComfyQuestLab.csproj'
$labTests = Join-Path $repoRoot 'network\mod\ComfyQuestLab.Tests\ComfyQuestLab.Tests.csproj'

function Assert-Exit([string]$Label) {
    if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE." }
}

function Get-Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Write-Json([string]$Path, $Value) {
    [IO.File]::WriteAllText(
        $Path,
        (($Value | ConvertTo-Json -Depth 40) + "`n"),
        [Text.UTF8Encoding]::new($false))
}

function Invoke-Ssh([string]$Command, [string]$Label = 'AM4 command') {
    $result = & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget $Command
    if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE." }
    return $result
}

function Test-Ssh([string]$Command) {
    & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget "$Command 2>/dev/null" | Out-Null
    $code = $LASTEXITCODE
    return $code -eq 0
}

function Format-Number([double]$Value) {
    $Value.ToString('0.######', [Globalization.CultureInfo]::InvariantCulture)
}

function Start-SteamIfNeeded([string]$RemoteLap) {
    if (Test-Ssh 'pgrep -x steam >/dev/null') { return $false }
    Write-Host 'Starting Steam once for the warm AM4 session...'
    $priorText = (@(Invoke-Ssh "awk '/processing complete/{n++} END{print n+0}' '/home/derek/.local/share/Steam/logs/connection_log.txt'" 'Steam login baseline') -join '').Trim()
    $prior = [int]$priorText
    Invoke-Ssh "DISPLAY=:0 XDG_RUNTIME_DIR=/run/user/1000 DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus '/home/derek/valheim-capture/start-steam-session.sh' > '$RemoteLap/steam-session.log' 2>&1" 'Steam startup' | Out-Null
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        if (Test-Ssh 'pgrep -x steam >/dev/null') {
            $currentText = (@(Invoke-Ssh "awk '/processing complete/{n++} END{print n+0}' '/home/derek/.local/share/Steam/logs/connection_log.txt'" 'Steam login readiness') -join '').Trim()
            if ([int]$currentText -gt $prior) {
                Start-Sleep -Seconds 2
                return $true
            }
        }
        Start-Sleep -Seconds 1
    }
    throw 'Steam did not reach a fresh logged-on state.'
}

if (-not (Test-Path -LiteralPath $sourceEvidencePath -PathType Leaf)) {
    throw "Architectural staging evidence is missing: $sourceEvidencePath"
}
if (-not (Test-Path -LiteralPath $probe -PathType Leaf)) { throw "Warm probe is missing: $probe" }
& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
Assert-Exit 'Repository identity'

$source = [IO.File]::ReadAllText($sourceEvidencePath) | ConvertFrom-Json
if ($source.schema -ne 'comfy-quest-architectural-build-evidence-index/v1' -or
    $source.result -ne 'passed' -or $source.fixture -ne 'tn0304' -or
    $source.identity.machine -ne $ExpectedMachine -or
    $source.identity.world -ne $ExpectedWorld -or
    $source.identity.world_uid -ne $ExpectedWorldUid -or
    $source.identity.character -ne $ExpectedCharacter -or
    $source.architecture.pieces.total -ne 40 -or
    $source.placement_intent.revision -ne 2) {
    throw 'Architectural staging evidence identity or acceptance envelope differs.'
}
$captureName = [string]$source.canonical_artifacts.capture.name
$blueprintName = [string]$source.canonical_artifacts.blueprint.name
$derivedName = $captureName.Substring(0, $captureName.Length - '.capture.json'.Length)
if ($captureName -ne "$derivedName.capture.json" -or
    $blueprintName -ne "$derivedName.blueprint" -or
    $derivedName -notmatch '^[a-z0-9_-]{1,64}$') {
    throw 'Canonical staged pair names disagree.'
}
$captureHash = [string]$source.canonical_artifacts.capture.sha256
$blueprintHash = [string]$source.canonical_artifacts.blueprint.sha256
$piecesHash = [string]$source.source_hashes.canonical_pieces_sha256
$placementX = [double]$source.placement_intent.x
$placementY = [double]$source.placement_intent.y
$placementZ = [double]$source.placement_intent.z
$placementYaw = [double]$source.placement_intent.yaw

$machine = (@(Invoke-Ssh 'hostname' 'AM4 identity probe') -join '').Trim()
if ($machine -ne $ExpectedMachine) { throw "SSH target is $machine, expected $ExpectedMachine." }
$remoteWarmRoot = "/home/derek/valheim-capture/architectural-warm/$derivedName"
$remotePackage = "$remoteWarmRoot/package"
$remoteSession = "$remoteWarmRoot/session"
$remoteProbe = "$remotePackage/architectural_live_probe.py"
Invoke-Ssh "mkdir -p '$remotePackage' '$remoteWarmRoot/laps'" 'Warm root creation' | Out-Null
& scp -q $probe "${SshTarget}:$remoteProbe"
Assert-Exit 'Warm probe upload'

$common = "--valheim-root '$RemoteValheimRoot' --run-root '$remoteSession' --unity-root '$RemoteUnityRoot' " +
    "--machine '$ExpectedMachine' --world '$ExpectedWorld' --world-uid '$ExpectedWorldUid' " +
    "--character '$ExpectedCharacter' --session '$SessionId' --blueprint '$derivedName'"

if ($Close) {
    if (-not (Test-Ssh "test -f '$remoteSession/prepare.json'")) {
        Write-Host 'No open architectural warm session exists on AM4.'
        return
    }
    $stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
    $localClose = Join-Path $repoRoot "artifacts\architectural-warm\close-$stamp"
    New-Item -ItemType Directory -Path $localClose -Force | Out-Null
    & scp -q "${SshTarget}:$remoteSession/prepare.json" (Join-Path $localClose 'prepare.json')
    Assert-Exit 'Warm prepare receipt download'
    $prepare = [IO.File]::ReadAllText((Join-Path $localClose 'prepare.json')) | ConvertFrom-Json
    if (Test-Ssh 'pgrep -x valheim.x86_64 >/dev/null') {
        Write-Host 'Explicit close: stopping the warm Valheim client...'
        & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget "python3 '$remoteProbe' stop --valheim-root '$RemoteValheimRoot' --run-root '$remoteSession' --timeout 150"
        Assert-Exit 'Warm Valheim shutdown'
    }
    if (-not [bool]$prepare.steam_was_running -and (Test-Ssh 'pgrep -x steam >/dev/null')) {
        Write-Host 'Explicit close: returning Steam to the pre-session state...'
        & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget "DISPLAY=:0 XDG_RUNTIME_DIR=/run/user/1000 DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus steam -shutdown > '$remoteSession/steam-shutdown.log' 2>&1" 2>$null | Out-Null
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            if (-not (Test-Ssh 'pgrep -x steam >/dev/null')) { break }
            Start-Sleep -Seconds 1
        }
        if (Test-Ssh 'pgrep -x steam >/dev/null') { throw 'Steam did not stop during explicit close.' }
    }
    Write-Host 'Explicit close: restoring the pre-session plugin, runtime, Lab, world, and character bytes...'
    & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget "python3 '$remoteProbe' restore $common"
    Assert-Exit 'Warm session restoration'
    & scp -q "${SshTarget}:$remoteSession/restoration.json" (Join-Path $localClose 'restoration.json')
    Assert-Exit 'Warm restoration receipt download'
    $restoration = [IO.File]::ReadAllText((Join-Path $localClose 'restoration.json')) | ConvertFrom-Json
    if ($restoration.status -ne 'PASS') { throw 'Warm session restoration did not pass.' }
    Invoke-Ssh "mv '$remoteSession' '$remoteWarmRoot/closed-$stamp'" 'Warm session archive' | Out-Null
    Write-Host "Architectural warm session CLOSED and restored: $localClose"
    return
}

if (-not (Get-Command $DotNet -ErrorAction SilentlyContinue)) { throw ".NET executable is missing: $DotNet" }
if (-not $SkipTests) {
    Write-Host 'Running warm-loop and placement-aware contract scars...'
    & $DotNet test $labTests -c Release --filter 'FullyQualifiedName~LabCaptureContractTests|FullyQualifiedName~LabBatchContractTests'
    Assert-Exit 'Placement-aware Lab tests'
    Push-Location $repoRoot
    try {
        & python -m unittest tests.test_architectural_live_harness -v
        Assert-Exit 'Architectural warm harness tests'
    } finally { Pop-Location }
}
& $DotNet build $labProject -c Release --no-restore
Assert-Exit 'Quest Lab release build'
$candidateLab = Join-Path $repoRoot 'network\mod\ComfyQuestLab\bin\Release\ComfyQuestLab.dll'
$candidateLabHash = Get-Hash $candidateLab

$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$localLap = Join-Path $repoRoot "artifacts\architectural-warm\laps\$stamp"
$remoteLap = "$remoteWarmRoot/laps/$stamp"
$remoteCandidate = "$remoteLap/ComfyQuestLab.dll"
New-Item -ItemType Directory -Path $localLap -Force | Out-Null
Invoke-Ssh "mkdir -p '$remoteLap'" 'Warm lap creation' | Out-Null
& scp -q $candidateLab "${SshTarget}:$remoteCandidate"
Assert-Exit 'Warm candidate upload'
$remoteCandidatePin = (@(Invoke-Ssh "sha256sum '$remoteCandidate'" 'Warm candidate verification') -join '')
if ($remoteCandidatePin -notmatch [regex]::Escape($candidateLabHash)) {
    throw 'Warm candidate hash verification disagrees.'
}

$gameWasRunning = Test-Ssh 'pgrep -x valheim.x86_64 >/dev/null'
$sessionExists = Test-Ssh "test -f '$remoteSession/prepare.json'"
$sessionOpened = $false
$gameLaunched = $false
if ($gameWasRunning) {
    if (-not $sessionExists) { throw 'Valheim is running outside the architectural warm session.' }
    $installedPin = (@(Invoke-Ssh "sha256sum '$RemoteValheimRoot/BepInEx/plugins/ComfyQuestLab.dll'" 'Installed warm Lab verification') -join '')
    if ($installedPin -notmatch [regex]::Escape($candidateLabHash)) {
        throw 'Warm candidate drift requires an explicit -Close before redeployment.'
    }
    Write-Host 'Reattaching to the running AM4 client; install and world entry are unchanged.'
} else {
    if (-not $sessionExists) {
        Write-Host 'Opening the AM4 warm session once: snapshot, candidate deploy, and bounded world entry...'
        Invoke-Ssh "mkdir '$remoteSession'" 'Warm session creation' | Out-Null
        $prepareCommand = "python3 '$remoteProbe' prepare $common " +
            "--world-display-name '$ExpectedWorldDisplayName' --piece-count 40 " +
            "--capture-sha256 '$captureHash' --blueprint-sha256 '$blueprintHash' " +
            "--canonical-pieces-sha256 '$piecesHash' --candidate-lab '$remoteCandidate' " +
            "--candidate-lab-sha256 '$candidateLabHash'"
        Invoke-Ssh $prepareCommand 'Warm session preparation' | Write-Output
        $sessionOpened = $true
    } else {
        Write-Host 'Warm installation exists; refreshing only the consumed world-entry request...'
        $reentryCommand = "python3 '$remoteProbe' reenter $common " +
            "--world-display-name '$ExpectedWorldDisplayName' --piece-count 40 " +
            "--capture-sha256 '$captureHash' --blueprint-sha256 '$blueprintHash' " +
            "--canonical-pieces-sha256 '$piecesHash' --candidate-lab-sha256 '$candidateLabHash'"
        Invoke-Ssh $reentryCommand 'Warm world reentry' | Write-Output
    }
    Start-SteamIfNeeded $remoteLap | Out-Null
    Invoke-Ssh "python3 '$remoteProbe' launch --valheim-root '$RemoteValheimRoot' --run-root '$remoteLap' --display ':0'" 'Warm Valheim launch' | Write-Output
    $gameLaunched = $true
}

$lapCommon = "--valheim-root '$RemoteValheimRoot' --run-root '$remoteLap' --session-root '$remoteSession' --unity-root '$RemoteUnityRoot' " +
    "--machine '$ExpectedMachine' --world '$ExpectedWorld' --world-uid '$ExpectedWorldUid' " +
    "--character '$ExpectedCharacter' --session '$SessionId' --blueprint '$derivedName'"
$warmCommand = "DISPLAY=:0 python3 '$remoteProbe' warm $lapCommon --piece-count 40 " +
    "--capture-sha256 '$captureHash' --blueprint-sha256 '$blueprintHash' " +
    "--canonical-pieces-sha256 '$piecesHash' --candidate-lab-sha256 '$candidateLabHash' " +
    "--x '$(Format-Number $placementX)' --y '$(Format-Number $placementY)' " +
    "--z '$(Format-Number $placementZ)' --yaw '$(Format-Number $placementYaw)' " +
    "--world-timeout '$WorldTimeoutSeconds' --request-timeout '$RequestTimeoutSeconds'"
Write-Host 'Running warm check/diff; an exact existing build will be reused.'
& ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget $warmCommand
Assert-Exit 'AM4 architectural warm lap'

$remoteArchive = "$remoteWarmRoot/laps/$stamp-evidence.tar.gz"
Invoke-Ssh "tar -C '$remoteLap' -czf '$remoteArchive' ." 'Warm evidence archive' | Out-Null
& scp -q "${SshTarget}:$remoteArchive" (Join-Path $localLap 'evidence.tar.gz')
Assert-Exit 'Warm evidence download'
& tar -xzf (Join-Path $localLap 'evidence.tar.gz') -C $localLap
Assert-Exit 'Warm evidence extraction'
$warm = [IO.File]::ReadAllText((Join-Path $localLap 'warm-lap.json')) | ConvertFrom-Json
if ($warm.status -ne 'PASS' -or
    -not [bool]$warm.assertions.placed_diff_match -or
    -not [bool]$warm.assertions.marked_pieces_retained -or
    -not [bool]$warm.assertions.creator_build_disabled -or
    -not [bool]$warm.assertions.valheim_left_running) {
    throw 'Architectural warm lap did not retain a safe exact build.'
}

$acceptance = [ordered]@{
    schema = 'comfy-quest-architectural-warm-journey/v1'
    status = 'PASS'
    completed_utc = [DateTime]::UtcNow.ToString('o')
    identity = [ordered]@{
        machine = $ExpectedMachine
        world = $ExpectedWorld
        world_uid = $ExpectedWorldUid
        character = $ExpectedCharacter
        creator_session_id = $SessionId
        capsule_sha256 = [string]$source.identity.capsule_sha256
        stage_id = [string]$source.identity.stage_id
        blueprint_name = $derivedName
    }
    placement = [ordered]@{
        revision = [int]$source.placement_intent.revision
        x = $placementX; y = $placementY; z = $placementZ; yaw = $placementYaw
    }
    candidate_lab_sha256 = $candidateLabHash
    game_was_running = $gameWasRunning
    game_launched = $gameLaunched
    session_opened = $sessionOpened
    build_action = [string]$warm.build_action
    standing_piece_count = [int]$warm.standing_piece_count
    warm_lap_sha256 = Get-Hash (Join-Path $localLap 'warm-lap.json')
    assertions = $warm.assertions
    remote_warm_root = $remoteWarmRoot
    remote_lap_root = $remoteLap
    local_evidence_root = $localLap
    close_command = 'tools\quest-studio\Invoke-ArchitecturalWarmLap.ps1 -Close'
}
$acceptancePath = Join-Path $localLap 'acceptance-receipt.json'
Write-Json $acceptancePath $acceptance
Copy-Item -LiteralPath $acceptancePath -Destination (Join-Path $repoRoot 'artifacts\architectural-warm\latest-acceptance-receipt.json') -Force
Write-Host "Architectural warm lap PASS ($($warm.build_action)); AM4 remains running with 40 proved pieces: $acceptancePath"
