#Requires -Version 5.1
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BaselineRoot,
    [Parameter(Mandatory = $true)][string]$PlatformRoot,
    [string]$SshTarget = 'homebase',
    [string]$RemoteValheimRoot = '/home/derek/valheim',
    [string]$ExpectedMachine = 'am4',
    [string]$ExpectedWorld = 'ComfyQuestDemo',
    [string]$ExpectedWorldUid = '-7600395338659582326',
    [string]$ExpectedCharacter = 'questyfour',
    [int]$Port = 18086,
    [string]$DotNet,
    [switch]$AllowWarmClient,
    [switch]$SkipBrowserInstall
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$baseline = (Resolve-Path -LiteralPath $BaselineRoot).Path
$platform = (Resolve-Path -LiteralPath $PlatformRoot).Path
if (-not $DotNet) {
    $workspaceDotNet = Join-Path (Split-Path -Parent $repoRoot) 'dotnet9\dotnet.exe'
    $candidates = @(
        $env:COMFY_QUEST_DOTNET,
        $(if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' }),
        $(if (Test-Path -LiteralPath $workspaceDotNet -PathType Leaf) { $workspaceDotNet }),
        $(if (Get-Command dotnet -ErrorAction SilentlyContinue) { (Get-Command dotnet).Source })
    ) | Where-Object { $_ } | Select-Object -Unique
    foreach ($candidate in $candidates) {
        try {
            $resolved = (Get-Item -LiteralPath $candidate -ErrorAction Stop).FullName
            if ([int]((& $resolved --version) -split '\.')[0] -ge 9) { $DotNet = $resolved; break }
        } catch { }
    }
}
if (-not $DotNet -or -not (Test-Path -LiteralPath $DotNet -PathType Leaf)) {
    throw '.NET 9 executable missing. Set COMFY_QUEST_DOTNET or pass -DotNet.'
}
if ($Port -lt 1024 -or $Port -gt 65535) { throw 'Port must be in 1024..65535.' }

function Get-Hash([string]$Path) {
    (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Get-BytesHash([byte[]]$Bytes) {
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-','').ToLowerInvariant() }
    finally { $algorithm.Dispose() }
}

function Assert-Exit([string]$Label) {
    if ($LASTEXITCODE -ne 0) { throw "$Label failed with exit code $LASTEXITCODE." }
}

function Invoke-Identity([string]$Root, [string]$Repository) {
    $top = (& git -C $Root rev-parse --show-toplevel).Trim().Replace('/','\')
    Assert-Exit "Git root for $Root"
    if ([IO.Path]::GetFullPath($top).TrimEnd('\') -ne [IO.Path]::GetFullPath($Root).TrimEnd('\')) {
        throw "Repository root differs: expected $Root, observed $top"
    }
    $remote = (& git -C $Root remote get-url origin).Trim()
    Assert-Exit "Git remote for $Root"
    if ($remote -notmatch ('(?i)(?:github\.com[:/])' + [regex]::Escape($Repository) + '(?:\.git)?$')) {
        throw "Repository remote differs: expected $Repository, observed $remote"
    }
    $script = Join-Path $Root 'tools\Assert-RepoIdentity.ps1'
    if (Test-Path -LiteralPath $script -PathType Leaf) {
        & $script | Out-Null
        Assert-Exit "Repository identity for $Root"
    }
}

function Invoke-Ssh([string]$Command) {
    & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget $Command
    Assert-Exit "ssh $SshTarget"
}

Invoke-Identity $repoRoot 'djcdevelopment/comfy-quest'
Invoke-Identity $baseline 'djcdevelopment/baseline'
Invoke-Identity $platform 'djcdevelopment/lumberjacks-platform'

$sourceRevision = (& git -C $repoRoot rev-parse HEAD).Trim()
Assert-Exit 'Comfy Quest source revision'
$baselineRevision = (& git -C $baseline rev-parse HEAD).Trim()
Assert-Exit 'Baseline source revision'
$platformRevision = (& git -C $platform rev-parse HEAD).Trim()
Assert-Exit 'Lumberjacks source revision'
$sourceDirty = @(& git -C $repoRoot status --porcelain=v1 --untracked-files=all).Count -gt 0
$stamp = [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssZ')
$runRoot = Join-Path $repoRoot "artifacts\architectural-build\runs\$stamp"
$packageRoot = Join-Path $runRoot 'package'
$publishRoot = Join-Path $packageRoot 'payload'
$browserRoot = Join-Path $runRoot 'browser'
New-Item -ItemType Directory -Path $runRoot,$packageRoot,$publishRoot,$browserRoot -Force | Out-Null

$capsuleA = Join-Path $runRoot 'tn0304-a.zip'
$capsuleB = Join-Path $runRoot 'tn0304-b.zip'
$envelopeTool = Join-Path $baseline 'tools\selfie-stick\probe_architectural_constraint_envelope.py'
Push-Location $baseline
try {
    & python $envelopeTool capsule --output $capsuleA
    Assert-Exit 'First Baseline capsule export'
    & python $envelopeTool capsule --output $capsuleB
    Assert-Exit 'Second Baseline capsule export'
} finally { Pop-Location }
Push-Location (Join-Path $baseline 'tools\selfie-stick')
try {
    & python -m unittest test_architectural_curriculum.ArchitecturalCurriculumTests.test_architectural_build_capsule_is_deterministic_and_cross_checked -v
    Assert-Exit 'Baseline capsule contract test'
} finally { Pop-Location }
if ((Get-Hash $capsuleA) -ne (Get-Hash $capsuleB) -or
    (Get-Item $capsuleA).Length -ne (Get-Item $capsuleB).Length) {
    throw 'Repeated Baseline exports were not byte-identical.'
}
$capsule = Join-Path $runRoot 'tn0304-architectural-build-capsule.zip'
Copy-Item -LiteralPath $capsuleA -Destination $capsule
Copy-Item -LiteralPath $capsule -Destination (Join-Path $repoRoot 'artifacts\architectural-build\tn0304-architectural-build-capsule.zip') -Force
$capsuleHash = Get-Hash $capsule

$nuget = Join-Path $runRoot 'nuget-cache'
$previousNuget = $env:NUGET_PACKAGES
try {
    $env:NUGET_PACKAGES = $nuget
    & $DotNet test (Join-Path $repoRoot 'src\Quest.Studio.Tests\Quest.Studio.Tests.csproj') `
        -c Release --filter 'FullyQualifiedName~QuestStudioArchitecturalBuildTests'
    Assert-Exit 'Studio architectural contract tests'
    Push-Location $repoRoot
    try {
        & python -m unittest tests.test_godbuild_import tests.test_architectural_build_slice -v
        Assert-Exit 'Importer and static Build contracts'
    } finally { Pop-Location }

    & $DotNet publish (Join-Path $repoRoot 'src\Quest.Studio.Host\Quest.Studio.Host.csproj') `
        -c Release -r linux-x64 --self-contained true -o $publishRoot
    Assert-Exit 'Self-contained Linux Studio publish'
} finally { $env:NUGET_PACKAGES = $previousNuget }

$bundleImporter = Join-Path $publishRoot 'tools\blueprints\import_capture.py'
$bundleProbe = Join-Path $publishRoot 'tools\quest-studio\probe_architectural_stage.py'
New-Item -ItemType Directory -Path (Split-Path -Parent $bundleImporter),(Split-Path -Parent $bundleProbe) -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'tools\blueprints\import_capture.py') -Destination $bundleImporter
Copy-Item -LiteralPath (Join-Path $repoRoot 'tools\quest-studio\probe_architectural_stage.py') -Destination $bundleProbe

$sourcePins = [ordered]@{}
foreach ($relative in @(
    'src\Quest.Studio\IQuestStudioHost.cs',
    'src\Quest.Studio\QuestStudioBuilds.cs',
    'src\Quest.Studio\QuestStudioEndpoints.cs',
    'src\Quest.Studio\QuestStudioPage.cs',
    'src\Quest.Studio.Host\Program.cs',
    'tools\blueprints\import_capture.py',
    'tools\quest-studio\probe_architectural_stage.py'
)) {
    $path = Join-Path $repoRoot $relative
    $sourcePins[$relative.Replace('\','/')] = Get-Hash $path
}
$treeMaterial = $sourceRevision + "`n" + (($sourcePins.GetEnumerator() | ForEach-Object {
    $_.Key + "`t" + $_.Value
}) -join "`n")
$treeBytes = [Text.Encoding]::UTF8.GetBytes($treeMaterial)
$treeHash = Get-BytesHash $treeBytes

$hostBundle = Join-Path $packageRoot 'quest-studio-linux-x64.zip'
if (Test-Path -LiteralPath $hostBundle) { throw "Owned bundle unexpectedly exists: $hostBundle" }
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.IO.Compression
$bundleStream = [IO.File]::Open($hostBundle,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
try {
    $archive = [IO.Compression.ZipArchive]::new($bundleStream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try {
        foreach ($file in Get-ChildItem -LiteralPath $publishRoot -File -Recurse | Sort-Object FullName) {
            $relative = $file.FullName.Substring($publishRoot.Length).TrimStart('\','/').Replace('\','/')
            $entry = $archive.CreateEntry($relative,[IO.Compression.CompressionLevel]::Optimal)
            $entry.LastWriteTime = [DateTimeOffset]::new(1980,1,1,0,0,0,[TimeSpan]::Zero)
            $input = [IO.File]::OpenRead($file.FullName)
            $output = $entry.Open()
            try { $input.CopyTo($output) }
            finally { $output.Dispose(); $input.Dispose() }
        }
    } finally { $archive.Dispose() }
} finally { $bundleStream.Dispose() }
$hostBundleHash = Get-Hash $hostBundle
$importerBytes = [IO.File]::ReadAllBytes($bundleImporter)
$manifest = [ordered]@{
    schema = 'comfy-quest-standalone-rnd-bundle/v1'
    repository_id = 'djcdevelopment/comfy-quest'
    source_revision = $sourceRevision
    source_dirty = $sourceDirty
    source_tree_sha256 = $treeHash
    host_bundle_sha256 = $hostBundleHash
    host_bundle_bytes = (Get-Item -LiteralPath $hostBundle).Length
    files = [ordered]@{
        'tools/blueprints/import_capture.py' = [ordered]@{
            bytes = $importerBytes.Length
            sha256 = Get-Hash $bundleImporter
        }
    }
}
$bundleManifest = Join-Path $packageRoot 'standalone-rnd-bundle.json'
[IO.File]::WriteAllText($bundleManifest, (($manifest | ConvertTo-Json -Depth 8) + "`n"), [Text.UTF8Encoding]::new($false))
$manifestHash = Get-Hash $bundleManifest
$probeHash = Get-Hash $bundleProbe

$remoteRoot = "/home/derek/valheim-capture/architectural-build/$stamp-$($capsuleHash.Substring(0,12))"
$remoteBundle = "$remoteRoot/quest-studio-linux-x64.zip"
$remoteManifest = "$remoteRoot/standalone-rnd-bundle.json"
$remoteCapsule = "$remoteRoot/tn0304-architectural-build-capsule.zip"
$remotePayload = "$remoteRoot/bundle"
$remoteState = "$remoteRoot/studio-state"
$remoteBefore = "$remoteRoot/before.json"
$remoteProof = "$remoteRoot/am4-proof.json"
$tunnel = $null
$remoteHostStarted = $false
$tunnelOut = Join-Path $runRoot 'ssh-tunnel.stdout.log'
$tunnelErr = Join-Path $runRoot 'ssh-tunnel.stderr.log'
try {
    $machine = (& ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget 'hostname').Trim()
    Assert-Exit 'AM4 identity probe'
    if ($machine -ne $ExpectedMachine) { throw "SSH target is $machine, expected $ExpectedMachine." }
    Invoke-Ssh "test -d '$RemoteValheimRoot' && test ! -e '$remoteRoot' && mkdir -p '$remoteRoot'"
    & scp -q $hostBundle "${SshTarget}:$remoteBundle"
    Assert-Exit 'Studio bundle upload'
    & scp -q $bundleManifest "${SshTarget}:$remoteManifest"
    Assert-Exit 'Bundle manifest upload'
    & scp -q $capsule "${SshTarget}:$remoteCapsule"
    Assert-Exit 'Capsule upload'
    $remoteHashes = @(Invoke-Ssh "sha256sum '$remoteBundle' '$remoteManifest' '$remoteCapsule'")
    $remoteHashText = $remoteHashes -join "`n"
    foreach ($expected in @($hostBundleHash,$manifestHash,$capsuleHash)) {
        if ($remoteHashText -notmatch [regex]::Escape($expected)) { throw "AM4 upload hash missing: $expected" }
    }
    Invoke-Ssh "mkdir -p '$remotePayload' '$remoteState' && python3 -m zipfile -e '$remoteBundle' '$remotePayload' && cp '$remoteManifest' '$remotePayload/standalone-rnd-bundle.json' && chmod 700 '$remotePayload/Comfy.Quest.Studio.Host'"
    Invoke-Ssh "python3 '$remotePayload/tools/quest-studio/probe_architectural_stage.py' snapshot --valheim-root '$RemoteValheimRoot' --output '$remoteBefore'" | Out-Null
    Invoke-Ssh "cd '$remoteRoot'; COMFY_QUEST_STUDIO_STATE='$remoteState' COMFY_VALHEIM_DIR='$RemoteValheimRoot' COMFY_QUEST_REPO_ROOT='$remotePayload' COMFY_QUEST_PYTHON='/usr/bin/python3' COMFY_QUEST_RND_BUNDLE_MANIFEST='$remotePayload/standalone-rnd-bundle.json' COMFY_QUEST_STUDIO_PORT='$Port' nohup '$remotePayload/Comfy.Quest.Studio.Host' --port '$Port' > '$remoteRoot/studio.stdout.log' 2> '$remoteRoot/studio.stderr.log' < /dev/null & echo `$! > '$remoteRoot/studio.pid'"
    $remoteHostStarted = $true
    $healthy = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        & ssh -o BatchMode=yes $SshTarget "curl --silent --fail 'http://127.0.0.1:$Port/health'" 2>$null | Out-Null
        if ($LASTEXITCODE -eq 0) { $healthy = $true; break }
        Start-Sleep -Milliseconds 250
    }
    if (-not $healthy) { throw 'AM4 Studio host did not become healthy.' }

    $tunnel = Start-Process -FilePath 'ssh' -ArgumentList @(
        '-N','-o','BatchMode=yes','-o','ExitOnForwardFailure=yes',
        '-L',"${Port}:127.0.0.1:${Port}",$SshTarget
    ) -RedirectStandardOutput $tunnelOut -RedirectStandardError $tunnelErr -WindowStyle Hidden -PassThru
    $localHealthy = $false
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($tunnel.HasExited) { throw "SSH tunnel exited with code $($tunnel.ExitCode)." }
        try {
            $health = Invoke-RestMethod "http://127.0.0.1:$Port/health" -TimeoutSec 2
            if ($health.status -eq 'ok') { $localHealthy = $true; break }
        } catch {}
        Start-Sleep -Milliseconds 250
    }
    if (-not $localHealthy) { throw 'Local AM4 Studio tunnel did not become healthy.' }

    $savedEnvironment = @{}
    foreach ($name in @('COMFY_QUEST_ARCHITECTURAL_CAPSULE','COMFY_QUEST_ARCHITECTURAL_STUDIO_URL',
                          'COMFY_QUEST_E2E_ARTIFACT_ROOT','COMFY_QUEST_E2E_KEEP_ARTIFACTS')) {
        $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name,'Process')
    }
    try {
        $env:COMFY_QUEST_ARCHITECTURAL_CAPSULE = $capsule
        $env:COMFY_QUEST_ARCHITECTURAL_STUDIO_URL = "http://127.0.0.1:$Port/quest-studio"
        $env:COMFY_QUEST_E2E_ARTIFACT_ROOT = $browserRoot
        $env:COMFY_QUEST_E2E_KEEP_ARTIFACTS = '1'
        $e2e = Join-Path $repoRoot 'tools\quest-studio\Test-QuestStudioE2E.ps1'
        $e2eArguments = @{
            DotNet = $DotNet
            Filter = 'FullyQualifiedName~Architectural_capsule_is_inspected_placed_and_staged_without_world_entry'
            KeepArtifacts = $true
            SkipBrowserInstall = [bool]$SkipBrowserInstall
        }
        & $e2e @e2eArguments
        Assert-Exit 'AM4 architectural browser journey'
    } finally {
        foreach ($name in $savedEnvironment.Keys) {
            [Environment]::SetEnvironmentVariable($name,$savedEnvironment[$name],'Process')
        }
    }

    $warmVerifyArgument = if ($AllowWarmClient) { ' --allow-warm-client' } else { '' }
    Invoke-Ssh "python3 '$remotePayload/tools/quest-studio/probe_architectural_stage.py' verify --before '$remoteBefore' --valheim-root '$RemoteValheimRoot' --studio-state '$remoteState' --expected-world '$ExpectedWorld' --expected-character '$ExpectedCharacter'$warmVerifyArgument --output '$remoteProof'" | Out-Null
    foreach ($name in @('before.json','am4-proof.json','studio.stdout.log','studio.stderr.log')) {
        & scp -q "${SshTarget}:$remoteRoot/$name" (Join-Path $runRoot $name)
        Assert-Exit "AM4 evidence download $name"
    }
    $stageRemote = (& ssh -o BatchMode=yes $SshTarget "find '$remoteState/quest-studio/builds' -mindepth 2 -maxdepth 2 -name latest-stage.json -print").Trim()
    Assert-Exit 'AM4 stage receipt discovery'
    if ([string]::IsNullOrWhiteSpace($stageRemote)) { throw 'AM4 stage receipt was not found.' }
    & scp -q "${SshTarget}:$stageRemote" (Join-Path $runRoot 'latest-stage.json')
    Assert-Exit 'AM4 stage receipt download'

    $browserReceipt = Get-ChildItem -LiteralPath $browserRoot -Filter 'architectural-browser-receipt.json' -File -Recurse |
        Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
    if (-not $browserReceipt) { throw 'Browser evidence receipt was not found.' }
    $proof = Get-Content -Raw -LiteralPath (Join-Path $runRoot 'am4-proof.json') | ConvertFrom-Json
    if ($proof.status -ne 'PASS') { throw "AM4 proof failed: $($proof.failures -join ', ')" }
    $stage = Get-Content -Raw -LiteralPath (Join-Path $runRoot 'latest-stage.json') | ConvertFrom-Json
    $acceptance = [ordered]@{
        schema = 'creator-os-architectural-build-journey/v1'
        status = 'PASS'
        completed_utc = [DateTime]::UtcNow.ToString('o')
        identity = [ordered]@{
            machine = $ExpectedMachine
            world = $ExpectedWorld
            world_uid = $ExpectedWorldUid
            character = $ExpectedCharacter
            remote_run_root = $remoteRoot
        }
        sources = [ordered]@{
            comfy_quest_revision = $sourceRevision
            comfy_quest_dirty = $sourceDirty
            baseline_revision = $baselineRevision
            lumberjacks_platform_revision = $platformRevision
            source_tree_sha256 = $treeHash
        }
        capsule = [ordered]@{ path=$capsule; bytes=(Get-Item $capsule).Length; sha256=$capsuleHash; deterministic_rebuild=$true }
        standalone_host = [ordered]@{ path=$hostBundle; bytes=(Get-Item $hostBundle).Length; sha256=$hostBundleHash; manifest_sha256=$manifestHash; importer_sha256=$manifest.files.'tools/blueprints/import_capture.py'.sha256; probe_sha256=$probeHash }
        browser = [ordered]@{ receipt=$browserReceipt.FullName; sha256=Get-Hash $browserReceipt.FullName }
        stage = $stage
        safety = $proof.assertions
        scope = [ordered]@{
            creator_session_started = $false
            mailbox_request_written = $false
            world_mutation_performed = $false
            valheim_started = $false
            valheim_process_reused = [bool]$proof.assertions.valheim_process_reused
            warm_client_allowed = [bool]$AllowWarmClient
            next_attack = 'consume the exact staged pair and placement intent for check -> apply -> diff -> clear'
        }
    }
    $acceptancePath = Join-Path $runRoot 'acceptance-receipt.json'
    [IO.File]::WriteAllText($acceptancePath, (($acceptance | ConvertTo-Json -Depth 30) + "`n"), [Text.UTF8Encoding]::new($false))
    Copy-Item -LiteralPath $acceptancePath -Destination (Join-Path $repoRoot 'artifacts\architectural-build\latest-acceptance-receipt.json') -Force
    Write-Host "Architectural Build journey PASS: $acceptancePath"
} finally {
    if ($tunnel) {
        if (-not $tunnel.HasExited) { Stop-Process -Id $tunnel.Id -Force }
        $tunnel.Dispose()
    }
    if ($remoteHostStarted) {
        & ssh -o BatchMode=yes -o ConnectTimeout=8 $SshTarget "test -f '$remoteRoot/studio.pid' && xargs -r kill < '$remoteRoot/studio.pid' || true" 2>$null | Out-Null
    }
}
