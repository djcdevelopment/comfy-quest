#Requires -Version 5.1
<#
.SYNOPSIS
Build and bind the exact CreatorOSBeta1 release world on the AM4 automation seat.

.DESCRIPTION
Starts from the fresh vanilla CreatorOSBeta1 pair generated locally, replays the accepted
40-piece Field Lodge at natural ground, stages the fixed 17-object Signature Hunt fixture,
activates the exact beta questpack, and binds/starts Air Drop on the fixture-owned loadout sign.
The game is then stopped through Valheim's save path, final bytes and receipts are downloaded,
and AM4's plugins, Runtime/Lab state, character, and absence of this beta world are restored.
#>
[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'High')]
param(
    [string]$SshTarget = 'homebase',
    [string]$RemoteValheimRoot = '/home/derek/valheim',
    [string]$RemoteUnityRoot = '/home/derek/.config/unity3d/IronGate/Valheim',
    [string]$ExpectedMachine = 'am4',
    [string]$ExpectedCharacter = 'questyfour',
    [string]$SeedWorldRoot,
    [string]$OutDir,
    [string]$DotNet = 'dotnet',
    [int]$WorldTimeoutSeconds = 600,
    [int]$RequestTimeoutSeconds = 90,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $root 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }

$worldName = 'CreatorOSBeta1'
$worldUid = '4257656027'
$packId = 'slayers-signature-hunt'
$packVersion = '1.0.0'
$entryExperience = 'slayers-air-drop'
$contentHash = 'ad94f708efa9afbb8267cae6336226fc2c59c9d86c777d2309b932842f09c734'
$blueprint = 'tn0304-6ue2ukrad7ntjfoa7cvdmvwkcvsccusli5wrfvhq2rnkhtm2ehuq'
$pieceCount = 40
$evidencePath = Join-Path $root 'docs\evidence\architectural-build-tn0304-20260829-r1.json'
$baseProbe = Join-Path $root 'tools\quest-studio\architectural_live_probe.py'
$betaProbe = Join-Path $root 'tools\quest-studio\creatoros_beta_world_probe.py'
$labProject = Join-Path $root 'network\mod\ComfyQuestLab\ComfyQuestLab.csproj'
$runtimeProject = Join-Path $root 'network\mod\ComfyQuestRuntime\ComfyQuestRuntime.csproj'
$labTests = Join-Path $root 'network\mod\ComfyQuestLab.Tests\ComfyQuestLab.Tests.csproj'
$questpack = Join-Path $root 'creatoros\beta1\slayers-signature-hunt-1.0.0.questpack'

if (-not $SeedWorldRoot) {
    $SeedWorldRoot = Join-Path $root 'artifacts\creatoros-beta1-world\seed-20260830\config\worlds_local'
}
$SeedWorldRoot = [IO.Path]::GetFullPath($SeedWorldRoot)
$seedDb = Join-Path $SeedWorldRoot ($worldName + '.db')
$seedFwl = Join-Path $SeedWorldRoot ($worldName + '.fwl')
foreach ($required in @($evidencePath, $baseProbe, $betaProbe, $questpack, $seedDb, $seedFwl)) {
    if (-not (Test-Path -LiteralPath $required -PathType Leaf)) { throw "Required beta seed input missing: $required" }
}

function Assert-Exit([string]$Label) {
    if ($LASTEXITCODE -ne 0) { throw ("{0} failed with exit code {1}." -f $Label, $LASTEXITCODE) }
}

function Get-Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-StableHash([string]$Path) {
    $first = Get-Hash $Path
    Start-Sleep -Milliseconds 750
    $second = Get-Hash $Path
    if ($first -ne $second) { throw "Build artifact was still changing: $Path" }
    return $second
}

function Get-WorldMetadata([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 8) { throw 'world_metadata_too_short' }
        $count = $reader.ReadInt32()
        if ($count -le 0 -or $count -gt 1MB -or $count -gt ($stream.Length - $stream.Position)) {
            throw 'world_metadata_payload_invalid'
        }
        $payload = $reader.ReadBytes($count)
        $memory = [IO.MemoryStream]::new($payload, $false)
        $package = [IO.BinaryReader]::new($memory)
        try {
            $version = $package.ReadInt32()
            $displayName = $package.ReadString()
            $seedName = $package.ReadString()
            $seed = $package.ReadInt32()
            $uid = $package.ReadInt64().ToString([Globalization.CultureInfo]::InvariantCulture)
            return [ordered]@{ version = $version; display_name = $displayName; seed_name = $seedName; seed = $seed; uid = $uid }
        } finally { $package.Dispose(); $memory.Dispose() }
    } finally { $reader.Dispose(); $stream.Dispose() }
}

function Write-Json([string]$Path, $Value, [int]$Depth = 20) {
    [IO.File]::WriteAllText(
        $Path, (($Value | ConvertTo-Json -Depth $Depth) + [Environment]::NewLine),
        [Text.UTF8Encoding]::new($false))
}

function Invoke-Ssh([string]$Command, [string]$Label = 'AM4 command') {
    $output = @(& ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget $Command 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $tail = ($output | Select-Object -Last 12) -join [Environment]::NewLine
        throw ("{0} failed with exit code {1}: {2}" -f $Label, $LASTEXITCODE, $tail)
    }
    return $output
}

function Test-Ssh([string]$Command) {
    & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget "$Command 2>/dev/null" | Out-Null
    return $LASTEXITCODE -eq 0
}

function Get-RemoteHash([string]$Path, [string]$Label) {
    $line = (@(Invoke-Ssh "sha256sum '$Path'" $Label) -join '').Trim()
    if ($line -notmatch '^([0-9a-f]{64})\s+') { throw "$Label returned no SHA256." }
    return $Matches[1]
}

function Start-SteamSession([string]$RemoteLog) {
    if (Test-Ssh 'pgrep -x steam >/dev/null') { return $false }
    $priorText = (@(Invoke-Ssh "awk '/processing complete/{n++} END{print n+0}' '/home/derek/.local/share/Steam/logs/connection_log.txt'" 'Steam login baseline') -join '').Trim()
    $prior = [int]$priorText
    Invoke-Ssh "DISPLAY=:0 XDG_RUNTIME_DIR=/run/user/1000 DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus '/home/derek/valheim-capture/start-steam-session.sh' > '$RemoteLog' 2>&1" 'Steam startup' | Out-Null
    for ($attempt = 0; $attempt -lt 90; $attempt++) {
        if (Test-Ssh 'pgrep -x steam >/dev/null') {
            $currentText = (@(Invoke-Ssh "awk '/processing complete/{n++} END{print n+0}' '/home/derek/.local/share/Steam/logs/connection_log.txt'" 'Steam login readiness') -join '').Trim()
            if ([int]$currentText -gt $prior) { Start-Sleep -Seconds 2; return $true }
        }
        Start-Sleep -Seconds 1
    }
    throw 'Steam did not reach a fresh logged-on state.'
}

$metadata = Get-WorldMetadata $seedFwl
if ($metadata.display_name -ne $worldName -or $metadata.uid -ne $worldUid) {
    throw "Fresh world identity mismatch: $($metadata.display_name) UID $($metadata.uid)."
}
$campaign = Get-Content -LiteralPath (Join-Path $root 'creatoros\beta1\campaign.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$campaign.campaign_id -ne $packId -or [string]$campaign.version -ne $packVersion -or
    [string]$campaign.pack.content_hash -ne $contentHash -or
    (Get-Hash $questpack) -ne [string]$campaign.pack.sha256) {
    throw 'Generated beta campaign/questpack identity mismatch.'
}
$architecture = Get-Content -LiteralPath $evidencePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$architecture.result -ne 'passed' -or [string]$architecture.fixture -ne 'tn0304' -or
    [int]$architecture.architecture.pieces.total -ne $pieceCount) {
    throw 'Accepted Field Lodge architecture evidence mismatch.'
}
$captureHash = [string]$architecture.canonical_artifacts.capture.sha256
$blueprintHash = [string]$architecture.canonical_artifacts.blueprint.sha256
$piecesHash = [string]$architecture.source_hashes.canonical_pieces_sha256

if (-not $SkipTests) {
    & python -m unittest tests.test_creatoros_beta_world_probe tests.test_architectural_live_harness -v
    Assert-Exit 'CreatorOS world probe tests'
    & $DotNet test $labTests -c Release --filter FullyQualifiedName~LabSignatureHuntContractTests --no-restore
    Assert-Exit 'Signature Hunt contract tests'
}
& powershell.exe -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'tools\creatoros\Build-CreatorOsBeta1.ps1') -Check
Assert-Exit 'CreatorOS content check'
& $DotNet build $labProject -c Release --no-restore -v:minimal
Assert-Exit 'Quest Lab release build'
& $DotNet build $runtimeProject -c Release --no-restore -v:minimal
Assert-Exit 'Quest Runtime release build'

$candidateLab = Join-Path $root 'network\mod\ComfyQuestLab\bin\Release\ComfyQuestLab.dll'
$runtimeBuild = Join-Path $root 'network\mod\ComfyQuestRuntime\bin\Release\net48'
$candidatePlugins = [ordered]@{
    'ComfyQuestRuntime.dll' = Join-Path $runtimeBuild 'ComfyQuestRuntime.dll'
    'ComfyQuestContracts.dll' = Join-Path $runtimeBuild 'ComfyQuestContracts.dll'
    'Newtonsoft.Json.dll' = Join-Path $runtimeBuild 'Newtonsoft.Json.dll'
}
foreach ($path in @($candidateLab) + @($candidatePlugins.Values)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Candidate plugin missing: $path" }
}
$candidateLabHash = Get-StableHash $candidateLab
$questpackHash = Get-StableHash $questpack
$seedDbHash = Get-StableHash $seedDb
$seedFwlHash = Get-StableHash $seedFwl
$candidatePluginHashes = @{}
foreach ($name in $candidatePlugins.Keys) {
    $candidatePluginHashes[$name] = Get-StableHash $candidatePlugins[$name]
}

$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$sessionId = 'creatoros-beta1-seed-' + $stamp
if (-not $OutDir) { $OutDir = Join-Path $root "artifacts\creatoros-beta1-world\final-$stamp" }
$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path -LiteralPath $OutDir) {
    if (@(Get-ChildItem -LiteralPath $OutDir -Force).Count -ne 0) { throw "Output directory is not empty: $OutDir" }
} else { New-Item -ItemType Directory -Path $OutDir | Out-Null }

$remoteRoot = "/home/derek/valheim-capture/creatoros-beta1-seed/$stamp"
$remotePackage = "$remoteRoot/package"
$remoteSession = "$remoteRoot/session"
$remoteBaseProbe = "$remotePackage/architectural_live_probe.py"
$remoteBetaProbe = "$remotePackage/creatoros_beta_world_probe.py"
$remoteQuestpack = "$remotePackage/creatoros-beta1.questpack"
$remoteLab = "$remotePackage/ComfyQuestLab.dll"
$remoteWorldRoot = "$RemoteUnityRoot/worlds_local"
$remotePluginRoot = "$RemoteValheimRoot/BepInEx/plugins"
$originalPlugins = "$remotePackage/original-plugins"
$steamWasRunning = Test-Ssh 'pgrep -x steam >/dev/null'
$prepared = $false
$pluginsDeployed = $false
$gameStoppedCleanly = $false
$restoration = $null
$primaryFailure = $null
$cleanupErrors = @()

if (-not $PSCmdlet.ShouldProcess(
        "$ExpectedMachine via $SshTarget",
        "Seed $worldName UID $worldUid, save/download it, then restore AM4")) {
    Write-Host 'CreatorOS beta world seed cancelled before remote mutation.'
    return
}

try {
    $machine = (@(Invoke-Ssh 'hostname' 'AM4 identity') -join '').Trim()
    if ($machine -ne $ExpectedMachine) { throw "SSH target is $machine, expected $ExpectedMachine." }
    if (Test-Ssh 'pgrep -x valheim.x86_64 >/dev/null') { throw 'AM4 Valheim must be stopped before beta seeding.' }
    if (Test-Ssh "test -e '$remoteWorldRoot/$worldName.db' -o -e '$remoteWorldRoot/$worldName.fwl'") {
        throw 'AM4 already has a CreatorOSBeta1 world pair; refusing to overwrite it.'
    }
    Invoke-Ssh "mkdir -p '$remotePackage' '$remoteSession' '$originalPlugins'" 'Remote seed directories' | Out-Null
    & scp -q $baseProbe ($SshTarget + ':' + $remoteBaseProbe); Assert-Exit 'Base probe upload'
    & scp -q $betaProbe ($SshTarget + ':' + $remoteBetaProbe); Assert-Exit 'Beta probe upload'
    & scp -q $candidateLab ($SshTarget + ':' + $remoteLab); Assert-Exit 'Lab candidate upload'
    & scp -q $questpack ($SshTarget + ':' + $remoteQuestpack); Assert-Exit 'Questpack upload'
    & scp -q $seedDb ($SshTarget + ':' + $remotePackage + '/' + $worldName + '.db'); Assert-Exit 'Seed DB upload'
    & scp -q $seedFwl ($SshTarget + ':' + $remotePackage + '/' + $worldName + '.fwl'); Assert-Exit 'Seed FWL upload'
    foreach ($name in $candidatePlugins.Keys) {
        & scp -q $candidatePlugins[$name] ($SshTarget + ':' + $remotePackage + '/' + $name)
        Assert-Exit "$name candidate upload"
    }
    $uploaded = @(
        [pscustomobject]@{ path = "$remotePackage/$worldName.db"; sha256 = $seedDbHash },
        [pscustomobject]@{ path = "$remotePackage/$worldName.fwl"; sha256 = $seedFwlHash },
        [pscustomobject]@{ path = $remoteLab; sha256 = $candidateLabHash },
        [pscustomobject]@{ path = $remoteQuestpack; sha256 = $questpackHash }
    )
    foreach ($pair in $uploaded) {
        if ((Get-RemoteHash $pair.path 'Remote upload verification') -ne $pair.sha256) {
            throw "Remote upload hash mismatch: $($pair.path)"
        }
    }

    $common = "--valheim-root '$RemoteValheimRoot' --run-root '$remoteSession' --unity-root '$RemoteUnityRoot' " +
        "--machine '$ExpectedMachine' --world '$worldName' --world-uid '$worldUid' " +
        "--character '$ExpectedCharacter' --session '$sessionId' --blueprint '$blueprint'"
    $prepareCommand = "python3 '$remoteBaseProbe' prepare $common --world-display-name '$worldName' " +
        "--piece-count '$pieceCount' --capture-sha256 '$captureHash' --blueprint-sha256 '$blueprintHash' " +
        "--canonical-pieces-sha256 '$piecesHash' --candidate-lab '$remoteLab' " +
        "--candidate-lab-sha256 '$candidateLabHash'"
    Invoke-Ssh $prepareCommand 'AM4 beta preparation' | Write-Output
    $prepared = $true

    foreach ($name in $candidatePlugins.Keys) {
        Invoke-Ssh "cp -p '$remotePluginRoot/$name' '$originalPlugins/$name'" "$name backup" | Out-Null
    }
    # Once every original is durable, every later candidate-copy failure must take the exact
    # restore path; do not wait until all three deployments have succeeded to arm cleanup.
    $pluginsDeployed = $true
    foreach ($name in $candidatePlugins.Keys) {
        Invoke-Ssh "install -m 0644 '$remotePackage/$name' '$remotePluginRoot/$name'" "$name deployment" | Out-Null
        $expectedHash = $candidatePluginHashes[$name]
        if ((Get-RemoteHash "$remotePluginRoot/$name" "$name deployed hash") -ne $expectedHash) {
            throw "$name deployed hash mismatch."
        }
    }
    Invoke-Ssh "install -m 0644 '$remotePackage/$worldName.db' '$remoteWorldRoot/$worldName.db'; install -m 0644 '$remotePackage/$worldName.fwl' '$remoteWorldRoot/$worldName.fwl'" 'Fresh beta world deployment' | Out-Null

    Start-SteamSession "$remoteSession/steam-session.log" | Out-Null
    Invoke-Ssh "python3 '$remoteBaseProbe' launch --valheim-root '$RemoteValheimRoot' --run-root '$remoteSession' --display ':0'" 'AM4 Valheim launch' | Write-Output
    $betaCommand = "DISPLAY=:0 python3 '$remoteBetaProbe' $common --piece-count '$pieceCount' " +
        "--capture-sha256 '$captureHash' --blueprint-sha256 '$blueprintHash' " +
        "--canonical-pieces-sha256 '$piecesHash' --questpack '$remoteQuestpack' " +
        "--questpack-sha256 '$questpackHash' --pack-id '$packId' --pack-version '$packVersion' " +
        "--content-hash '$contentHash' --entry-experience '$entryExperience' " +
        "--world-timeout '$WorldTimeoutSeconds' --request-timeout '$RequestTimeoutSeconds'"
    Invoke-Ssh $betaCommand 'CreatorOS beta world seed' | Write-Output

    Invoke-Ssh "python3 '$remoteBaseProbe' stop --valheim-root '$RemoteValheimRoot' --run-root '$remoteSession' --timeout 180" 'AM4 Valheim graceful stop' | Write-Output
    $gameStoppedCleanly = $true
    & scp -q ($SshTarget + ':' + $remoteSession + '/creatoros-beta1-world-seed.json') (Join-Path $OutDir 'creatoros-beta1-world-seed.json')
    Assert-Exit 'World-seed receipt download'
    & scp -q ($SshTarget + ':' + $remoteSession + '/signature-hunt-fixture.json') (Join-Path $OutDir 'signature-hunt-fixture.json')
    Assert-Exit 'Fixture receipt download'
    & scp -q ($SshTarget + ':' + $RemoteUnityRoot + '/Player.log') (Join-Path $OutDir 'Player.log')
    Assert-Exit 'Player log download'
    & scp -q ($SshTarget + ':' + $remoteWorldRoot + '/' + $worldName + '.db') (Join-Path $OutDir ($worldName + '.db'))
    Assert-Exit 'Final world DB download'
    & scp -q ($SshTarget + ':' + $remoteWorldRoot + '/' + $worldName + '.fwl') (Join-Path $OutDir ($worldName + '.fwl'))
    Assert-Exit 'Final world FWL download'
    Invoke-Ssh "tar -C '$remoteSession' --exclude='./backup' -czf '$remotePackage/evidence.tar.gz' ." 'World evidence archive' | Out-Null
    & scp -q ($SshTarget + ':' + $remotePackage + '/evidence.tar.gz') (Join-Path $OutDir 'evidence.tar.gz')
    Assert-Exit 'World evidence archive download'

    $seedResult = Get-Content -LiteralPath (Join-Path $OutDir 'creatoros-beta1-world-seed.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    if ([string]$seedResult.schema -ne 'creatoros-beta1-world-seed/v1' -or [string]$seedResult.status -ne 'PASS' -or
        -not [bool]$seedResult.assertions.field_lodge_exact_match -or
        -not [bool]$seedResult.assertions.fixture_exact_count -or
        -not [bool]$seedResult.assertions.entry_anchor_bound -or
        -not [bool]$seedResult.assertions.entry_experience_started -or
        -not [bool]$seedResult.assertions.creator_build_disabled) {
        throw 'Downloaded CreatorOS world-seed receipt did not pass its exact assertions.'
    }
    $playerLog = [IO.File]::ReadAllText((Join-Path $OutDir 'Player.log'))
    $quitAt = $playerLog.LastIndexOf('Game - OnApplicationQuit', [StringComparison]::Ordinal)
    $savedAt = $playerLog.LastIndexOf('World saved', [StringComparison]::Ordinal)
    if ($quitAt -lt 0 -or $savedAt -lt $quitAt) { throw 'Player log has no post-quit World saved marker.' }
    $finalDb = Join-Path $OutDir ($worldName + '.db')
    $finalFwl = Join-Path $OutDir ($worldName + '.fwl')
    $finalMetadata = Get-WorldMetadata $finalFwl
    if ($finalMetadata.display_name -ne $worldName -or $finalMetadata.uid -ne $worldUid -or
        (Get-Hash $finalDb) -eq (Get-Hash $seedDb) -or (Get-Item -LiteralPath $finalDb).Length -le 0) {
        throw 'Final world pair did not preserve identity and record a changed database.'
    }
} catch {
    $primaryFailure = $_
} finally {
    if (Test-Ssh 'pgrep -x valheim.x86_64 >/dev/null') {
        try {
            Invoke-Ssh "python3 '$remoteBaseProbe' stop --valheim-root '$RemoteValheimRoot' --run-root '$remoteSession' --timeout 180" 'Failure-path Valheim stop' | Out-Null
        } catch { $cleanupErrors += 'valheim_stop:' + $_.Exception.Message }
    }
    if ($pluginsDeployed) {
        foreach ($name in $candidatePlugins.Keys) {
            try {
                Invoke-Ssh "install -m 0644 '$originalPlugins/$name' '$remotePluginRoot/$name'" "$name restoration" | Out-Null
                if ((Get-RemoteHash "$remotePluginRoot/$name" "$name restored hash") -ne
                    (Get-RemoteHash "$originalPlugins/$name" "$name backup hash")) {
                    throw "$name restoration hash mismatch."
                }
            } catch { $cleanupErrors += 'plugin_restore:' + $name + ':' + $_.Exception.Message }
        }
    }
    if ($prepared) {
        try {
            Invoke-Ssh "python3 '$remoteBaseProbe' restore $common" 'AM4 byte-exact restoration' | Write-Output
            & scp -q ($SshTarget + ':' + $remoteSession + '/restoration.json') (Join-Path $OutDir 'am4-restoration.json')
            Assert-Exit 'AM4 restoration receipt download'
            $restoration = Get-Content -LiteralPath (Join-Path $OutDir 'am4-restoration.json') -Raw -Encoding UTF8 | ConvertFrom-Json
            if ([string]$restoration.status -ne 'PASS') { throw 'AM4 restoration receipt failed.' }
            if (Test-Ssh "test -e '$remoteWorldRoot/$worldName.db' -o -e '$remoteWorldRoot/$worldName.fwl'") {
                throw 'AM4 beta world pair remained after restoration.'
            }
        } catch { $cleanupErrors += 'state_restore:' + $_.Exception.Message }
    }
    if (-not $steamWasRunning -and (Test-Ssh 'pgrep -x steam >/dev/null')) {
        try {
            Invoke-Ssh "DISPLAY=:0 XDG_RUNTIME_DIR=/run/user/1000 DBUS_SESSION_BUS_ADDRESS=unix:path=/run/user/1000/bus steam -shutdown > '$remoteSession/steam-shutdown.log' 2>&1" 'Steam shutdown' | Out-Null
            for ($attempt = 0; $attempt -lt 60 -and (Test-Ssh 'pgrep -x steam >/dev/null'); $attempt++) { Start-Sleep -Seconds 1 }
            if (Test-Ssh 'pgrep -x steam >/dev/null') { throw 'Steam did not stop.' }
        } catch { $cleanupErrors += 'steam_restore:' + $_.Exception.Message }
    }
}

if ($primaryFailure) {
    $cleanup = if ($cleanupErrors.Count) { ' Cleanup: ' + ($cleanupErrors -join ' | ') } else { '' }
    throw ($primaryFailure.Exception.Message + $cleanup)
}
if ($cleanupErrors.Count) { throw ('CreatorOS world passed but AM4 cleanup failed: ' + ($cleanupErrors -join ' | ')) }
if (-not $gameStoppedCleanly -or $null -eq $restoration) { throw 'CreatorOS world lifecycle did not complete.' }

$finalDb = Join-Path $OutDir ($worldName + '.db')
$finalFwl = Join-Path $OutDir ($worldName + '.fwl')
$receipt = [ordered]@{
    schema = 'creatoros-beta1-world-artifact/v1'
    status = 'PASS'
    completed_utc = [DateTimeOffset]::UtcNow.ToString('o')
    world = [ordered]@{
        name = $worldName
        uid = $worldUid
        seed_name = [string]$metadata.seed_name
        seed = [int]$metadata.seed
        db = [ordered]@{ bytes = (Get-Item -LiteralPath $finalDb).Length; sha256 = Get-Hash $finalDb }
        fwl = [ordered]@{ bytes = (Get-Item -LiteralPath $finalFwl).Length; sha256 = Get-Hash $finalFwl }
    }
    seed = [ordered]@{ db_sha256 = $seedDbHash; fwl_sha256 = $seedFwlHash }
    field_lodge = [ordered]@{
        capsule_id = 'tn0304'
        piece_count = $pieceCount
        canonical_pieces_sha256 = $piecesHash
        placement_mode = 'ground'
    }
    campaign = [ordered]@{
        pack_id = $packId; version = $packVersion; content_hash = $contentHash
        entry_experience = $entryExperience; questpack_sha256 = $questpackHash
    }
    evidence = [ordered]@{
        seed_receipt_sha256 = Get-Hash (Join-Path $OutDir 'creatoros-beta1-world-seed.json')
        fixture_receipt_sha256 = Get-Hash (Join-Path $OutDir 'signature-hunt-fixture.json')
        player_log_sha256 = Get-Hash (Join-Path $OutDir 'Player.log')
        archive_sha256 = Get-Hash (Join-Path $OutDir 'evidence.tar.gz')
        graceful_world_saved = $true
        am4_restoration_sha256 = Get-Hash (Join-Path $OutDir 'am4-restoration.json')
        am4_restored = $true
    }
}
Write-Json (Join-Path $OutDir 'world-artifact.json') $receipt
Write-Host "CreatorOSBeta1 world artifact PASS: $OutDir"
Write-Output $OutDir
