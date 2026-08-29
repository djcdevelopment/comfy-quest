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
    [string]$SessionId = 'architectural-tn0304-live-20260829-r1',
    [string]$DotNet = 'dotnet',
    [int]$WorldTimeoutSeconds = 600,
    [int]$RequestTimeoutSeconds = 90
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

function Get-BytesHash([byte[]]$Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-','').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
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
    # Expected negatives (no process / no tmux session) return 1 and may write a diagnostic.
    # Redirect that diagnostic on the target so PowerShell cannot promote it to an exception.
    & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget "$Command 2>/dev/null" | Out-Null
    $code = $LASTEXITCODE
    return $code -eq 0
}

function Format-Number([double]$Value) {
    $Value.ToString('0.######', [Globalization.CultureInfo]::InvariantCulture)
}

if (-not (Test-Path -LiteralPath $sourceEvidencePath -PathType Leaf)) {
    throw "Architectural staging evidence is missing: $sourceEvidencePath"
}
if (-not (Test-Path -LiteralPath $probe -PathType Leaf)) { throw "Live probe is missing: $probe" }
if (-not (Get-Command $DotNet -ErrorAction SilentlyContinue)) { throw ".NET executable is missing: $DotNet" }
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
    $source.placement_intent.revision -ne 2 -or
    $source.placement_intent.applied -ne $false) {
    throw 'Architectural staging evidence identity or acceptance envelope differs.'
}
$captureName = [string]$source.canonical_artifacts.capture.name
$blueprintName = [string]$source.canonical_artifacts.blueprint.name
if (-not $captureName.EndsWith('.capture.json') -or
    -not $blueprintName.EndsWith('.blueprint')) {
    throw 'Architectural staging evidence carries unsupported artifact names.'
}
$derivedName = $captureName.Substring(0, $captureName.Length - '.capture.json'.Length)
if ($blueprintName -ne "$derivedName.blueprint" -or
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
foreach ($hash in @($captureHash,$blueprintHash,$piecesHash)) {
    if ($hash -notmatch '^[0-9a-f]{64}$') { throw "Invalid source SHA-256: $hash" }
}

Write-Host 'Running placement-aware Lab contract scars...'
& $DotNet test $labTests -c Release --filter 'FullyQualifiedName~LabCaptureContractTests|FullyQualifiedName~LabBatchContractTests'
Assert-Exit 'Placement-aware Lab tests'
Push-Location $repoRoot
try {
    & python -m unittest tests.test_architectural_live_harness -v
    Assert-Exit 'Architectural live harness tests'
} finally { Pop-Location }
& $DotNet build $labProject -c Release --no-restore
Assert-Exit 'Quest Lab release build'
$candidateLab = Join-Path $repoRoot 'network\mod\ComfyQuestLab\bin\Release\ComfyQuestLab.dll'
if (-not (Test-Path -LiteralPath $candidateLab -PathType Leaf)) { throw 'Candidate Lab DLL is missing.' }
$candidateLabHash = Get-Hash $candidateLab

$sourceRevision = (& git -C $repoRoot rev-parse HEAD).Trim()
Assert-Exit 'Source revision'
$sourceDirty = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=all).Count -gt 0
$sourcePins = [ordered]@{}
foreach ($relative in @(
    'network/mod/ComfyQuestLab/Core/LabCaptureContract.cs',
    'network/mod/ComfyQuestLab/Core/LabBlueprintBuilder.cs',
    'network/mod/ComfyQuestLab/Core/LabBatchContract.cs',
    'network/mod/ComfyQuestLab/Core/LabBatchController.cs',
    'tools/quest-studio/architectural_live_probe.py',
    'tools/quest-studio/Invoke-ArchitecturalLiveJourney.ps1'
)) {
    $sourcePins[$relative] = Get-Hash (Join-Path $repoRoot $relative)
}
$treeText = $sourceRevision + "`n" + (($sourcePins.GetEnumerator() | ForEach-Object {
    $_.Key + "`t" + $_.Value
}) -join "`n")
$treeHash = Get-BytesHash ([Text.Encoding]::UTF8.GetBytes($treeText))

$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$localRun = Join-Path $repoRoot "artifacts\architectural-live\runs\$stamp"
New-Item -ItemType Directory -Path $localRun -Force | Out-Null
$remoteRoot = "/home/derek/valheim-capture/architectural-live/$stamp-$($source.identity.capsule_sha256.Substring(0,12))"
$remotePackage = "$remoteRoot/package"
$remoteRun = "$remoteRoot/run"
$remoteProbe = "$remotePackage/architectural_live_probe.py"
$remoteCandidate = "$remotePackage/ComfyQuestLab.dll"
$remoteArchive = "$remoteRoot/evidence.tar.gz"
$machine = (@(Invoke-Ssh 'hostname' 'AM4 identity probe') -join '').Trim()
if ($machine -ne $ExpectedMachine) { throw "SSH target is $machine, expected $ExpectedMachine." }
if (Test-Ssh "test -e '$remoteRoot'") { throw "Remote evidence root already exists: $remoteRoot" }
Invoke-Ssh "mkdir -p '$remotePackage' '$remoteRun'" 'AM4 evidence root creation' | Out-Null
& scp -q $probe "${SshTarget}:$remoteProbe"
Assert-Exit 'Live probe upload'
& scp -q $candidateLab "${SshTarget}:$remoteCandidate"
Assert-Exit 'Candidate Lab upload'
$remotePins = @(Invoke-Ssh "sha256sum '$remoteProbe' '$remoteCandidate'" 'AM4 package hash verification') -join "`n"
if ($remotePins -notmatch [regex]::Escape((Get-Hash $probe)) -or
    $remotePins -notmatch [regex]::Escape($candidateLabHash)) {
    throw 'AM4 package hash verification disagrees.'
}

$common = "--valheim-root '$RemoteValheimRoot' --run-root '$remoteRun' --unity-root '$RemoteUnityRoot' " +
    "--machine '$ExpectedMachine' --world '$ExpectedWorld' --world-uid '$ExpectedWorldUid' " +
    "--character '$ExpectedCharacter' --session '$SessionId' --blueprint '$derivedName'"
$prepared = $false
$gameStarted = $false
$steamStarted = $false
$journeyFailure = $null
$shutdownGraceful = $true
try {
    Write-Host 'Preparing AM4 backup, candidate Lab, and bounded world entry...'
    $prepareCommand = "python3 '$remoteProbe' prepare $common " +
        "--world-display-name '$ExpectedWorldDisplayName' --piece-count 40 " +
        "--capture-sha256 '$captureHash' --blueprint-sha256 '$blueprintHash' " +
        "--canonical-pieces-sha256 '$piecesHash' --candidate-lab '$remoteCandidate' " +
        "--candidate-lab-sha256 '$candidateLabHash'"
    Invoke-Ssh $prepareCommand 'AM4 live preparation' | Write-Output
    $prepared = $true
    & scp -q "${SshTarget}:$remoteRun/prepare.json" (Join-Path $localRun 'prepare.json')
    Assert-Exit 'Prepare receipt download'
    $prepareReceipt = [IO.File]::ReadAllText((Join-Path $localRun 'prepare.json')) | ConvertFrom-Json
    if ($prepareReceipt.status -ne 'READY') { throw 'AM4 preparation did not reach READY.' }

    if (-not [bool]$prepareReceipt.steam_was_running) {
        Write-Host 'Starting AM4 Steam session owned by this journey...'
        $priorLoginCountText = (@(Invoke-Ssh "awk '/processing complete/{n++} END{print n+0}' '/home/derek/.local/share/Steam/logs/connection_log.txt'" 'AM4 Steam login baseline') -join '').Trim()
        $priorLoginCount = [int]$priorLoginCountText
        Invoke-Ssh "DISPLAY=:0 XDG_RUNTIME_DIR=/run/user/1000 DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus '/home/derek/valheim-capture/start-steam-session.sh' > '$remoteRun/steam-session.log' 2>&1" 'AM4 Steam startup' | Out-Null
        $steamStarted = $true
        $steamReady = $false
        for ($attempt = 0; $attempt -lt 90; $attempt++) {
            if (Test-Ssh 'pgrep -x steam >/dev/null') {
                $loginCountText = (@(Invoke-Ssh "awk '/processing complete/{n++} END{print n+0}' '/home/derek/.local/share/Steam/logs/connection_log.txt'" 'AM4 Steam login readiness') -join '').Trim()
                if ([int]$loginCountText -gt $priorLoginCount) {
                    $steamReady = $true
                    break
                }
            }
            Start-Sleep -Seconds 1
        }
        if (-not $steamReady) { throw 'AM4 Steam did not reach a fresh logged-on state.' }
        Start-Sleep -Seconds 2
    }

    Write-Host 'Launching Valheim; Runtime will select the pinned character and world...'
    Invoke-Ssh "python3 '$remoteProbe' launch --valheim-root '$RemoteValheimRoot' --run-root '$remoteRun' --display ':0'" 'AM4 Valheim launch' | Write-Output
    $gameStarted = $true

    $runCommand = "DISPLAY=:0 python3 '$remoteProbe' run $common --piece-count 40 " +
        "--capture-sha256 '$captureHash' --blueprint-sha256 '$blueprintHash' " +
        "--canonical-pieces-sha256 '$piecesHash' --x '$(Format-Number $placementX)' " +
        "--y '$(Format-Number $placementY)' --z '$(Format-Number $placementZ)' " +
        "--yaw '$(Format-Number $placementYaw)' --world-timeout '$WorldTimeoutSeconds' " +
        "--request-timeout '$RequestTimeoutSeconds'"
    Write-Host 'Executing check -> exact apply -> exact diff -> clear...'
    & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget $runCommand
    Assert-Exit 'AM4 architectural live replay'
} catch {
    $journeyFailure = $_
} finally {
    if ($gameStarted) {
        Write-Host 'Requesting graceful Valheim shutdown and save...'
        & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget "python3 '$remoteProbe' stop --valheim-root '$RemoteValheimRoot' --run-root '$remoteRun' --timeout 150"
        if ($LASTEXITCODE -ne 0) {
            $shutdownGraceful = $false
            if (-not $journeyFailure) { $journeyFailure = [Exception]::new('Valheim shutdown was not graceful.') }
        }
    }
    if ($steamStarted) {
        Write-Host 'Returning AM4 Steam to its pre-journey stopped state...'
        & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget "DISPLAY=:0 XDG_RUNTIME_DIR=/run/user/1000 DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus steam -shutdown > '$remoteRun/steam-shutdown.log' 2>&1" 2>$null | Out-Null
        for ($attempt = 0; $attempt -lt 60; $attempt++) {
            if (-not (Test-Ssh 'pgrep -x steam >/dev/null')) { break }
            Start-Sleep -Seconds 1
        }
        if (Test-Ssh 'pgrep -x steam >/dev/null') {
            if (-not $journeyFailure) { $journeyFailure = [Exception]::new('Steam did not return to its pre-journey stopped state.') }
        }
    }
    if ($prepared) {
        Write-Host 'Restoring prior Lab, Runtime, staged artifacts, world, and character bytes...'
        $restoreCommand = "python3 '$remoteProbe' restore $common"
        & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget $restoreCommand | Out-Null
        if ($LASTEXITCODE -ne 0 -and -not $journeyFailure) {
            $journeyFailure = [Exception]::new("AM4 restoration failed with exit code $LASTEXITCODE.")
        }
    }
}

if ($prepared) {
    Invoke-Ssh "tar -C '$remoteRun' --exclude='./backup' -czf '$remoteArchive' ." 'AM4 evidence archive' | Out-Null
    & scp -q "${SshTarget}:$remoteArchive" (Join-Path $localRun 'evidence.tar.gz')
    Assert-Exit 'AM4 evidence archive download'
    & tar -xzf (Join-Path $localRun 'evidence.tar.gz') -C $localRun
    Assert-Exit 'AM4 evidence extraction'
}
if ($journeyFailure) { throw $journeyFailure }

$live = [IO.File]::ReadAllText((Join-Path $localRun 'live-replay.json')) | ConvertFrom-Json
$restoration = [IO.File]::ReadAllText((Join-Path $localRun 'restoration.json')) | ConvertFrom-Json
if ($live.status -ne 'PASS' -or $restoration.status -ne 'PASS' -or -not $shutdownGraceful) {
    throw "Architectural live acceptance failed: live=$($live.status), restore=$($restoration.status), graceful=$shutdownGraceful"
}
$operations = @($live.sequence | ForEach-Object { $_.operation })
foreach ($required in @('blueprint_check','blueprint_build','blueprint_diff','blueprint_clear')) {
    if ($operations -notcontains $required) { throw "Live sequence omitted $required." }
}
$acceptance = [ordered]@{
    schema = 'comfy-quest-architectural-live-journey/v1'
    status = 'PASS'
    completed_utc = [DateTime]::UtcNow.ToString('o')
    source = [ordered]@{
        revision = $sourceRevision
        dirty = $sourceDirty
        tree_sha256 = $treeHash
        files = $sourcePins
        staged_evidence = 'docs/evidence/architectural-build-tn0304-20260829-r1.json'
        staged_evidence_sha256 = Get-Hash $sourceEvidencePath
    }
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
    architecture = $source.architecture
    canonical_artifacts = $source.canonical_artifacts
    canonical_pieces_sha256 = $piecesHash
    candidate_lab = [ordered]@{ sha256 = $candidateLabHash; bytes = (Get-Item $candidateLab).Length }
    live_replay = [ordered]@{
        receipt = 'live-replay.json'
        sha256 = Get-Hash (Join-Path $localRun 'live-replay.json')
        sequence = $live.sequence
        screenshot = $live.screenshot
    }
    restoration = [ordered]@{
        receipt = 'restoration.json'
        sha256 = Get-Hash (Join-Path $localRun 'restoration.json')
        assertions = $restoration.assertions
    }
    remote_evidence_root = $remoteRoot
    local_evidence_root = $localRun
    local_evidence_is_ignored = $true
    next_attack = 'promote the accepted staged pair and placement into a deliberate retained creator build only after human spatial review'
}
$acceptancePath = Join-Path $localRun 'acceptance-receipt.json'
Write-Json $acceptancePath $acceptance
Copy-Item -LiteralPath $acceptancePath -Destination (Join-Path $repoRoot 'artifacts\architectural-live\latest-acceptance-receipt.json') -Force
Write-Host "Architectural live journey PASS: $acceptancePath"
