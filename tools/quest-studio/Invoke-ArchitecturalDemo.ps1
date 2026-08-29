#Requires -Version 5.1
[CmdletBinding()]
param(
    [ValidateSet('Open', 'Status', 'StopStudio')]
    [string]$Action = 'Open',

    [string]$BaselineRoot = 'C:\work\baseline',
    [string]$PlatformRoot = 'C:\work\lumberjacks-platform',
    [string]$SshTarget = 'homebase',
    [string]$RemoteValheimRoot = '/home/derek/valheim',
    [string]$ExpectedMachine = 'am4',
    [int]$Port = 18087,
    [switch]$NoBrowser
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$artifactsRoot = Join-Path $repoRoot 'artifacts\architectural-demo'
$manifestPath = Join-Path $artifactsRoot 'control-plane.json'
$latestReceiptPath = Join-Path $artifactsRoot 'latest-demo-receipt.json'
$buildAcceptancePath = Join-Path $repoRoot 'artifacts\architectural-build\latest-acceptance-receipt.json'
$warmScript = Join-Path $repoRoot 'tools\quest-studio\Invoke-ArchitecturalWarmLap.ps1'
$expectedCapsuleHash = 'f509aa2a201fdb3495c0f8aa3656ca156421524b476d12d4f0d45aa3cd9a21e9'
$expectedLabHash = '44d971dc1ca75c6d9b1775d552c5f86ad33b428208e5cf9347ebca24f6dfb45c'
$expectedCaptureHash = '5d466cdaa5a213ef958d07636325b9398eee9a74584019da8dc16a5603654251'
$expectedBlueprintHash = '02201382e57635f4e945229836443d2fdbf75e243973281d1f9806cd770ece5f'
$expectedPiecesHash = '0b4a62bcd3d0baa914f264081657b99649dd0f25a5973e1258b4e3920acbb578'

function Assert-Exit([string]$Label) {
    if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE." }
}

function Get-Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Read-Json([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { throw "Required JSON is missing: $Path" }
    try { [IO.File]::ReadAllText($Path) | ConvertFrom-Json }
    catch { throw "Required JSON is unreadable: $Path" }
}

function Write-Json([string]$Path, $Value) {
    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }
    [IO.File]::WriteAllText(
        $Path,
        (($Value | ConvertTo-Json -Depth 40) + [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))
}

function Invoke-Ssh([string]$Command, [string]$Label = 'AM4 command') {
    $output = & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget $Command
    if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE." }
    return $output
}

function Test-Ssh([string]$Command) {
    & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget "$Command 2>/dev/null" | Out-Null
    return $LASTEXITCODE -eq 0
}

function Assert-Repository([string]$Root, [string]$ExpectedRepository) {
    $resolved = (Resolve-Path -LiteralPath $Root).Path
    $top = (& git -C $resolved rev-parse --show-toplevel).Trim().Replace('/', '\')
    Assert-Exit "Git root for $resolved"
    if ([IO.Path]::GetFullPath($top).TrimEnd('\') -ne [IO.Path]::GetFullPath($resolved).TrimEnd('\')) {
        throw "Repository root differs: expected $resolved, observed $top"
    }
    $remote = (& git -C $resolved remote get-url origin).Trim()
    Assert-Exit "Git remote for $resolved"
    if ($remote -notmatch ('(?i)(?:github\.com[:/])' + [regex]::Escape($ExpectedRepository) + '(?:\.git)?$')) {
        throw "Repository remote differs: expected $ExpectedRepository, observed $remote"
    }
    return (& git -C $resolved rev-parse HEAD).Trim()
}

function Assert-RemoteRoot([string]$RemoteRoot) {
    if ($RemoteRoot -notmatch '^/home/derek/valheim-capture/architectural-build/[A-Za-z0-9._-]+$') {
        throw "Architectural demo remote root is unsafe: $RemoteRoot"
    }
}

function Read-ControlPlane {
    if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { return $null }
    $manifest = Read-Json $manifestPath
    if ([string]$manifest.schema -ne 'comfy-quest-architectural-demo-control-plane/v1') {
        throw 'Architectural demo control-plane manifest has an unsupported schema.'
    }
    Assert-RemoteRoot ([string]$manifest.remote_root)
    return $manifest
}

function Test-LocalTunnel($Manifest) {
    if ($null -eq $Manifest -or -not $Manifest.tunnel_pid) { return $false }
    $process = Get-Process -Id ([int]$Manifest.tunnel_pid) -ErrorAction SilentlyContinue
    return $null -ne $process -and $process.ProcessName -eq 'ssh'
}

function Test-RemoteStudio($Manifest) {
    if ($null -eq $Manifest) { return $false }
    return Test-Ssh "test -f '$($Manifest.remote_root)/studio-demo.pid' && pid=`$(cat '$($Manifest.remote_root)/studio-demo.pid') && kill -0 `$pid && ps -p `$pid -o args= | grep -F 'Comfy.Quest.Studio.Host' >/dev/null"
}

function Stop-OwnedControlPlane($Manifest) {
    if ($null -eq $Manifest) { return }
    if (Test-LocalTunnel $Manifest) {
        Stop-Process -Id ([int]$Manifest.tunnel_pid) -Force
    }
    if (Test-RemoteStudio $Manifest) {
        Invoke-Ssh "pid=`$(cat '$($Manifest.remote_root)/studio-demo.pid'); kill `$pid; for n in `$(seq 1 30); do kill -0 `$pid 2>/dev/null || exit 0; sleep .1; done; exit 1" 'Studio host stop' | Out-Null
    }
}

function Get-DemoStatus {
    $manifest = Read-ControlPlane
    $machine = (@(Invoke-Ssh 'hostname' 'AM4 identity') -join '').Trim()
    $labPin = (@(Invoke-Ssh "sha256sum '$RemoteValheimRoot/BepInEx/plugins/ComfyQuestLab.dll'" 'AM4 Lab pin') -join '').Split(' ')[0].Trim()
    return [ordered]@{
        schema = 'comfy-quest-architectural-demo-status/v1'
        checked_utc = [DateTime]::UtcNow.ToString('o')
        machine = $machine
        machine_matches = $machine -eq $ExpectedMachine
        steam_running = Test-Ssh 'pgrep -x steam >/dev/null'
        valheim_running = Test-Ssh 'pgrep -x valheim.x86_64 >/dev/null'
        warm_session_present = Test-Ssh "test -f '/home/derek/valheim-capture/architectural-warm/tn0304-6ue2ukrad7ntjfoa7cvdmvwkcvsccusli5wrfvhq2rnkhtm2ehuq/session/prepare.json'"
        installed_lab_sha256 = $labPin
        installed_lab_matches = $labPin -eq $expectedLabHash
        studio_running = Test-RemoteStudio $manifest
        tunnel_running = Test-LocalTunnel $manifest
        studio_url = if ($manifest) { [string]$manifest.studio_url } else { $null }
        warm_close_is_separate = 'tools\quest-studio\Invoke-ArchitecturalWarmLap.ps1 -Close'
    }
}

function Invoke-Api([string]$Uri, [string]$Method, [hashtable]$Headers, [string]$Body = $null) {
    $arguments = @{
        Uri = $Uri
        Method = $Method
        Headers = $Headers
        UseBasicParsing = $true
        TimeoutSec = 30
    }
    if (-not [string]::IsNullOrEmpty($Body)) {
        $arguments.ContentType = 'application/json'
        $arguments.Body = $Body
    }
    $response = Invoke-WebRequest @arguments
    return $response.Content | ConvertFrom-Json
}

if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Port must be in 1024..65535.' }
New-Item -ItemType Directory -Path $artifactsRoot -Force | Out-Null

if ($Action -eq 'Status') {
    Get-DemoStatus | ConvertTo-Json -Depth 10
    return
}

if ($Action -eq 'StopStudio') {
    $manifest = Read-ControlPlane
    Stop-OwnedControlPlane $manifest
    $status = Get-DemoStatus
    if ($status.studio_running -or $status.tunnel_running) { throw 'Architectural demo Studio control plane did not stop.' }
    Write-Host 'Architectural demo Studio/tunnel stopped. AM4 Valheim and the standing build were left untouched.'
    return
}

if (-not (Test-Path -LiteralPath $warmScript -PathType Leaf)) { throw "Warm driver missing: $warmScript" }
if (-not (Test-Path -LiteralPath $buildAcceptancePath -PathType Leaf)) {
    throw 'architectural_demo_requires_build_acceptance'
}

$comfyRevision = Assert-Repository $repoRoot 'djcdevelopment/comfy-quest'
$baselineRevision = Assert-Repository $BaselineRoot 'djcdevelopment/baseline'
$platformRevision = Assert-Repository $PlatformRoot 'djcdevelopment/lumberjacks-platform'
$buildAcceptance = Read-Json $buildAcceptancePath
if ([string]$buildAcceptance.schema -ne 'creator-os-architectural-build-journey/v1' -or
    [string]$buildAcceptance.status -ne 'PASS') {
    throw 'architectural_demo_build_acceptance_invalid'
}
$remoteRoot = [string]$buildAcceptance.identity.remote_run_root
Assert-RemoteRoot $remoteRoot
$remotePayload = "$remoteRoot/bundle"
$remoteState = "$remoteRoot/studio-state"
$capsulePath = [string]$buildAcceptance.capsule.path
if (-not (Test-Path -LiteralPath $capsulePath -PathType Leaf)) {
    $capsulePath = Join-Path $repoRoot 'artifacts\architectural-build\tn0304-architectural-build-capsule.zip'
}
if (-not (Test-Path -LiteralPath $capsulePath -PathType Leaf) -or
    (Get-Hash $capsulePath) -ne $expectedCapsuleHash) {
    throw 'architectural_demo_capsule_pin_mismatch'
}
if (-not (Test-Ssh "test -x '$remotePayload/Comfy.Quest.Studio.Host' && test -f '$remotePayload/standalone-rnd-bundle.json'")) {
    throw 'architectural_demo_standalone_host_missing'
}

$statusBefore = Get-DemoStatus
if (-not $statusBefore.machine_matches -or -not $statusBefore.steam_running -or
    -not $statusBefore.valheim_running -or -not $statusBefore.warm_session_present -or
    -not $statusBefore.installed_lab_matches) {
    throw 'architectural_demo_warm_precondition_failed'
}

# This call is the bounded warm proof. On an exact retained identity it performs only
# status/check/count/diff/status and leaves the client and marked pieces running.
& $warmScript -SshTarget $SshTarget -RemoteValheimRoot $RemoteValheimRoot
Assert-Exit 'Architectural warm proof'
$warmLap = Get-ChildItem -LiteralPath (Join-Path $repoRoot 'artifacts\architectural-warm\laps') `
        -Filter 'warm-lap.json' -File -Recurse |
    Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $warmLap) { throw 'architectural_demo_warm_receipt_missing' }
$warm = Read-Json $warmLap.FullName
$warmAcceptancePath = Join-Path $warmLap.Directory.FullName 'acceptance-receipt.json'
$warmAcceptance = Read-Json $warmAcceptancePath
$forbiddenOperations = @('blueprint_build', 'blueprint_clear', 'stop', 'restore', 'deploy')
$observedOperations = @($warm.sequence | ForEach-Object { [string]$_.operation })
if ([string]$warm.status -ne 'PASS' -or [string]$warm.build_action -ne 'reused' -or
    [bool]$warmAcceptance.game_launched -or [bool]$warmAcceptance.session_opened -or
    ($observedOperations -join '|') -ne 'status|blueprint_check|blueprint_count|blueprint_diff|status' -or
    @($observedOperations | Where-Object { $_ -in $forbiddenOperations }).Count -ne 0) {
    throw 'architectural_demo_warm_reuse_contract_failed'
}
$liveScreenshot = Join-Path $warmLap.Directory.FullName 'architectural-live-applied.png'
if (-not (Test-Path -LiteralPath $liveScreenshot -PathType Leaf) -or
    [string]$warm.screenshot.capture_kind -ne 'x11-window' -or
    [string]$warm.screenshot.window.class -ne 'valheim.x86_64' -or
    [int]$warm.screenshot.window.width -ne 1920 -or [int]$warm.screenshot.window.height -ne 1080 -or
    (Get-Hash $liveScreenshot) -ne [string]$warm.screenshot.sha256) {
    throw 'architectural_demo_live_screenshot_invalid'
}

$priorManifest = Read-ControlPlane
if ($priorManifest -and (([string]$priorManifest.remote_root -ne $remoteRoot) -or ([int]$priorManifest.port -ne $Port))) {
    Stop-OwnedControlPlane $priorManifest
    $priorManifest = $null
}
$remoteLogRoot = "$remoteRoot/demo-control"
Invoke-Ssh "mkdir -p '$remoteLogRoot'" 'Studio demo log root' | Out-Null
$remoteStudioReused = Test-RemoteStudio $priorManifest
if (-not $remoteStudioReused) {
    Invoke-Ssh "cd '$remoteRoot'; COMFY_QUEST_STUDIO_STATE='$remoteState' COMFY_VALHEIM_DIR='$RemoteValheimRoot' COMFY_QUEST_REPO_ROOT='$remotePayload' COMFY_QUEST_PYTHON='/usr/bin/python3' COMFY_QUEST_RND_BUNDLE_MANIFEST='$remotePayload/standalone-rnd-bundle.json' COMFY_QUEST_STUDIO_PORT='$Port' nohup '$remotePayload/Comfy.Quest.Studio.Host' --port '$Port' > '$remoteLogRoot/studio.stdout.log' 2> '$remoteLogRoot/studio.stderr.log' < /dev/null & echo `$! > '$remoteRoot/studio-demo.pid'" 'Studio demo startup' | Out-Null
}
$remoteHealthy = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    if (Test-Ssh "curl --silent --fail 'http://127.0.0.1:$Port/health' >/dev/null") {
        $remoteHealthy = $true
        break
    }
    Start-Sleep -Milliseconds 250
}
if (-not $remoteHealthy) { throw 'architectural_demo_remote_studio_unhealthy' }

$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$runRoot = Join-Path $artifactsRoot "runs\$stamp"
New-Item -ItemType Directory -Path $runRoot -Force | Out-Null
$tunnelReused = Test-LocalTunnel $priorManifest
$tunnel = if ($tunnelReused) {
    Get-Process -Id ([int]$priorManifest.tunnel_pid)
} else {
    Start-Process -FilePath 'ssh' -ArgumentList @(
        '-N', '-o', 'BatchMode=yes', '-o', 'ExitOnForwardFailure=yes',
        '-L', "${Port}:127.0.0.1:${Port}", $SshTarget
    ) -RedirectStandardOutput (Join-Path $runRoot 'ssh-tunnel.stdout.log') `
      -RedirectStandardError (Join-Path $runRoot 'ssh-tunnel.stderr.log') `
      -WindowStyle Hidden -PassThru
}
$studioBase = "http://127.0.0.1:$Port"
$localHealthy = $false
for ($attempt = 0; $attempt -lt 60; $attempt++) {
    if ($tunnel.HasExited) { throw "Architectural demo tunnel exited with code $($tunnel.ExitCode)." }
    try {
        $health = Invoke-RestMethod "$studioBase/health" -TimeoutSec 2
        if ($health.status -eq 'ok') { $localHealthy = $true; break }
    } catch {}
    Start-Sleep -Milliseconds 250
}
if (-not $localHealthy) { throw 'architectural_demo_local_tunnel_unhealthy' }

$controlPlane = [ordered]@{
    schema = 'comfy-quest-architectural-demo-control-plane/v1'
    created_utc = if ($remoteStudioReused -and $tunnelReused -and $priorManifest.created_utc) {
        [string]$priorManifest.created_utc
    } else { [DateTime]::UtcNow.ToString('o') }
    last_verified_utc = [DateTime]::UtcNow.ToString('o')
    remote_root = $remoteRoot
    port = $Port
    tunnel_pid = $tunnel.Id
    remote_studio_reused = $remoteStudioReused
    tunnel_reused = $tunnelReused
    studio_url = "$studioBase/quest-studio"
    warm_close_is_separate = 'tools\quest-studio\Invoke-ArchitecturalWarmLap.ps1 -Close'
}
Write-Json $manifestPath $controlPlane

$security = Invoke-Api "$studioBase/api/v1/workbench/security" 'GET' @{}
$headers = @{ 'X-Workbench-Token' = [string]$security.browser_token }
$importResponse = Invoke-WebRequest -Uri "$studioBase/api/v2/quest-studio/builds/import" `
    -Method POST -Headers $headers -ContentType 'application/zip' -InFile $capsulePath `
    -UseBasicParsing -TimeoutSec 60
$imported = $importResponse.Content | ConvertFrom-Json
$build = $imported.build
if (-not $build -or [string]$build.capsule_sha256 -ne $expectedCapsuleHash -or
    [string]$build.canonical_capture_sha256 -ne $expectedCaptureHash -or
    [string]$build.canonical_blueprint_sha256 -ne $expectedBlueprintHash -or
    [string]$build.canonical_pieces_sha256 -ne $expectedPiecesHash -or
    [int]$build.piece_count -ne 40 -or [double]$build.placement.x -ne 12.5 -or
    [double]$build.placement.y -ne 1.25 -or [double]$build.placement.z -ne -3.75 -or
    [double]$build.placement.yaw -ne 22.5) {
    throw 'architectural_demo_import_identity_drift'
}
$stageResult = Invoke-Api "$studioBase/api/v2/quest-studio/builds/$($build.build_id)/stage" 'POST' $headers '{}'
$stage = $stageResult.receipt
if (-not $stage -or [string]$stage.artifacts.capture.sha256 -ne $expectedCaptureHash -or
    [string]$stage.artifacts.blueprint.sha256 -ne $expectedBlueprintHash -or
    [bool]$stage.creator_session_started -or [bool]$stage.mailbox_request_written -or
    [bool]$stage.world_mutation_performed -or [bool]$stage.placement_applied -or
    -not [bool]$stageResult.already_present) {
    throw 'architectural_demo_stage_identity_drift'
}

$browserReceiptPath = [string]$buildAcceptance.browser.receipt
$browserScreenshotPath = if (Test-Path -LiteralPath $browserReceiptPath -PathType Leaf) {
    Join-Path (Split-Path -Parent $browserReceiptPath) 'architectural-build-staged.png'
} else { $null }
$deepLink = "$studioBase/quest-studio?workspace=build&build=$([Uri]::EscapeDataString([string]$build.build_id))&view=architecture"
$statusAfter = Get-DemoStatus
$receipt = [ordered]@{
    schema = 'comfy-quest-architectural-demo/v1'
    status = 'PASS'
    completed_utc = [DateTime]::UtcNow.ToString('o')
    sources = [ordered]@{
        comfy_quest_revision = $comfyRevision
        baseline_revision = $baselineRevision
        lumberjacks_platform_revision = $platformRevision
        standalone_source_revision = $buildAcceptance.sources.comfy_quest_revision
        standalone_source_tree_sha256 = $buildAcceptance.sources.source_tree_sha256
    }
    identity = [ordered]@{
        machine = $ExpectedMachine
        world = 'ComfyQuestDemo'
        world_uid = '-7600395338659582326'
        character = 'questyfour'
        build_id = [string]$build.build_id
        derived_name = [string]$build.derived_name
        studio_url = $deepLink
    }
    capsule = [ordered]@{ path = $capsulePath; sha256 = $expectedCapsuleHash; bytes = (Get-Item -LiteralPath $capsulePath).Length }
    canonical = [ordered]@{
        graph_sha256 = [string]$build.graph_sha256
        pieces_sha256 = $expectedPiecesHash
        capture_sha256 = $expectedCaptureHash
        blueprint_sha256 = $expectedBlueprintHash
        importer_sha256 = [string]$build.importer_sha256
    }
    architecture = [ordered]@{
        footprint_m = @(7.953375, 7.4676)
        wall_datum_m = 2.2225
        ridge_m = 5.8166
        pitch_degrees = 43.907838
        ridge_reconciliation_m = -0.029171
        piece_count = 40
        prefab_counts = $build.prefab_counts
    }
    placement = $build.placement
    stage = [ordered]@{ receipt = $stage; already_present = [bool]$stageResult.already_present }
    warm = [ordered]@{
        acceptance_receipt = $warmAcceptancePath
        acceptance_sha256 = Get-Hash $warmAcceptancePath
        build_action = [string]$warm.build_action
        operations = $observedOperations
        screenshot = [ordered]@{
            path = $liveScreenshot
            sha256 = Get-Hash $liveScreenshot
            capture_kind = [string]$warm.screenshot.capture_kind
            window = $warm.screenshot.window
        }
    }
    browser = [ordered]@{
        proof = $browserReceiptPath
        proof_sha256 = if (Test-Path -LiteralPath $browserReceiptPath -PathType Leaf) { Get-Hash $browserReceiptPath } else { $null }
        screenshot = $browserScreenshotPath
        screenshot_sha256 = if ($browserScreenshotPath -and (Test-Path -LiteralPath $browserScreenshotPath -PathType Leaf)) { Get-Hash $browserScreenshotPath } else { $null }
    }
    assertions = [ordered]@{
        existing_build_reused = [string]$warm.build_action -eq 'reused'
        exact_diff_match = [bool]$warm.assertions.placed_diff_match
        exact_read_only_sequence = ($observedOperations -join '|') -eq 'status|blueprint_check|blueprint_count|blueprint_diff|status'
        canonical_stage_idempotent = [bool]$stageResult.already_present
        control_plane_reused = $remoteStudioReused -and $tunnelReused
        game_launched = [bool]$warmAcceptance.game_launched
        session_opened = [bool]$warmAcceptance.session_opened
        plugin_deployed = $false
        blueprint_built = $false
        blueprint_cleared = $false
        valheim_stopped = $false
        state_restored = $false
        creator_session_started = [bool]$stage.creator_session_started
        mailbox_request_written = [bool]$stage.mailbox_request_written
        world_mutation_performed = [bool]$stage.world_mutation_performed
        valheim_left_running = [bool]$statusAfter.valheim_running
        build_authority_disabled = [bool]$warm.assertions.creator_build_disabled
        pending_runtime_mailbox = $false
        pending_lab_mailbox = $false
    }
    control_plane = $controlPlane
    limitations = @(
        'This operator demo depends on the configured OMEN/homebase/AM4 R&D lane.',
        'StopStudio stops only Studio and the SSH tunnel; the warm Valheim structure remains standing.',
        'World teardown is available only through the separate explicit warm close command.'
    )
}
Write-Json (Join-Path $runRoot 'demo-receipt.json') $receipt
Write-Json $latestReceiptPath $receipt

if (-not $NoBrowser) {
    Start-Process $deepLink | Out-Null
    Start-Process $liveScreenshot | Out-Null
}
Write-Host "Architectural demo READY: $deepLink"
Write-Host "Warm build reused; Valheim remains running with 40 proved pieces. Receipt: $(Join-Path $runRoot 'demo-receipt.json')"
