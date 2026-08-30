#Requires -Version 5.1
<#
.SYNOPSIS
Build and verify the complete CreatorOS Beta 1 release directory.

.DESCRIPTION
Freezes one exact CreatorOSBeta1 world pair, campaign, Runtime, NetworkSense projection, and
packaged loopback Studio. The default requires a clean Quest checkout. A dirty-tree candidate is
available only through an explicit switch and is labeled candidate-dirty; it cannot verify as a
frozen release.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $WorldDb,
    [Parameter(Mandatory = $true)][string] $WorldFwl,
    [Parameter(Mandatory = $true)][string] $WorldUid,
    [Parameter(Mandatory = $true)][string] $NetworkSenseDll,
    [Parameter(Mandatory = $true)][string] $NetworkSenseSourceRevision,
    [string] $NetworkSenseRelease = 'beta-local',
    [string] $DiscoverlayHud,
    [string] $DiscoverlaySourceRevision,
    [string] $DotNetPath,
    [string] $OutDir,
    [switch] $CandidateFromDirtyTree
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$contentRoot = Join-Path $root 'creatoros\beta1'
$utf8 = New-Object Text.UTF8Encoding($false)
if (-not $DotNetPath) {
    $workspaceDotNet = Join-Path (Split-Path -Parent $root) 'dotnet9\dotnet.exe'
    $candidates = @(
        $env:COMFY_QUEST_DOTNET,
        $(if ($env:DOTNET_ROOT) { Join-Path $env:DOTNET_ROOT 'dotnet.exe' }),
        $(if (Test-Path -LiteralPath $workspaceDotNet -PathType Leaf) { $workspaceDotNet }),
        $(if (Get-Command dotnet -ErrorAction SilentlyContinue) { (Get-Command dotnet).Source })
    ) | Where-Object { $_ } | Select-Object -Unique
    foreach ($candidate in $candidates) {
        try {
            $resolved = (Get-Item -LiteralPath $candidate -ErrorAction Stop).FullName
            if ([int]((& $resolved --version) -split '\.')[0] -ge 9) { $DotNetPath = $resolved; break }
        } catch { }
    }
}
if (-not $DotNetPath -or -not (Test-Path -LiteralPath $DotNetPath -PathType Leaf)) {
    throw 'A .NET 9 executable is required. Set COMFY_QUEST_DOTNET or pass -DotNetPath.'
}
if ([int]((& $DotNetPath --version) -split '\.')[0] -lt 9) {
    throw "DotNetPath must point to a .NET 9 SDK: $DotNetPath"
}

function Invoke-Checked {
    param([string] $Program, [string[]] $Arguments)
    & $Program @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Program exited with $LASTEXITCODE" }
}

function Hash-File([string] $Path) {
    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Hash-NamedFiles([hashtable] $Files) {
    $sha = [Security.Cryptography.SHA256]::Create()
    $buffer = New-Object byte[] (1024 * 1024)
    try {
        foreach ($name in @($Files.Keys | Sort-Object)) {
            $prefix = [Text.Encoding]::UTF8.GetBytes(([string]$name) + "`n")
            [void]$sha.TransformBlock($prefix, 0, $prefix.Length, $prefix, 0)
            $stream = [IO.File]::OpenRead([string]$Files[$name])
            try {
                while (($count = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
                    [void]$sha.TransformBlock($buffer, 0, $count, $buffer, 0)
                }
            }
            finally { $stream.Dispose() }
        }
        [void]$sha.TransformFinalBlock((New-Object byte[] 0), 0, 0)
        return ([BitConverter]::ToString($sha.Hash)).Replace('-', '').ToLowerInvariant()
    }
    finally { $sha.Dispose() }
}

function Write-Json([string] $Path, $Value, [int] $Depth = 12) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth $Depth) + [Environment]::NewLine, $utf8)
}

function Copy-Required([string] $Source, [string] $Destination) {
    if (-not (Test-Path -LiteralPath $Source -PathType Leaf)) { throw "Required file missing: $Source" }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    Copy-Item -LiteralPath $Source -Destination $Destination
}

& (Join-Path $root 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }
foreach ($world in @($WorldDb, $WorldFwl)) {
    if (-not (Test-Path -LiteralPath $world -PathType Leaf)) { throw "World artifact missing: $world" }
}
if ((Split-Path -Leaf $WorldDb) -ne 'CreatorOSBeta1.db' -or
    (Split-Path -Leaf $WorldFwl) -ne 'CreatorOSBeta1.fwl') {
    throw 'World files must be named CreatorOSBeta1.db and CreatorOSBeta1.fwl.'
}
if ($WorldUid -notmatch '^-?[1-9][0-9]*$') { throw 'WorldUid must be a non-zero integer string.' }
if (-not (Test-Path -LiteralPath $NetworkSenseDll -PathType Leaf)) { throw 'NetworkSense DLL is missing.' }
if ($NetworkSenseSourceRevision -notmatch '^[0-9a-f]{40}$') { throw 'NetworkSenseSourceRevision must be a full lowercase commit.' }
if ($DiscoverlayHud) {
    if (-not (Test-Path -LiteralPath $DiscoverlayHud -PathType Leaf)) { throw 'Discoverlay HUD is missing.' }
    if ($DiscoverlaySourceRevision -notmatch '^[0-9a-f]{40}$') { throw 'DiscoverlaySourceRevision must accompany DiscoverlayHud.' }
}

$dirtyRows = @(& git -C $root status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0) { throw 'Unable to inspect Quest repository status.' }
$clean = $dirtyRows.Count -eq 0
if (-not $clean -and -not $CandidateFromDirtyTree) {
    throw 'A frozen CreatorOS beta release requires a clean Quest checkout. Use -CandidateFromDirtyTree only for a local candidate.'
}
$revision = (& git -C $root rev-parse HEAD).Trim().ToLowerInvariant()
if ($LASTEXITCODE -ne 0 -or $revision -notmatch '^[0-9a-f]{40}$') { throw 'Unable to resolve Quest revision.' }
if (-not $OutDir) { $OutDir = Join-Path $root 'artifacts\releases\creatoros-beta1' }
if (-not [IO.Path]::IsPathRooted($OutDir)) { $OutDir = Join-Path $root $OutDir }
$OutDir = [IO.Path]::GetFullPath($OutDir)
if (Test-Path -LiteralPath $OutDir) {
    if (@(Get-ChildItem -LiteralPath $OutDir -Force).Count -ne 0) { throw "Output directory must be absent or empty: $OutDir" }
}
else { New-Item -ItemType Directory -Path $OutDir | Out-Null }

Invoke-Checked 'powershell.exe' @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File',
    (Join-Path $root 'tools\creatoros\Build-CreatorOsBeta1.ps1'), '-Check')
Invoke-Checked $DotNetPath @('build', (Join-Path $root 'network\mod\ComfyQuestRuntime\ComfyQuestRuntime.csproj'),
    '-c', 'Release', '--no-restore', '-v:minimal')

$studioRoot = Join-Path $OutDir 'creator-kit\studio'
New-Item -ItemType Directory -Force -Path $studioRoot | Out-Null
$contractsPackage = Join-Path $root 'packages-local\Comfy.Quest.Contracts.0.9.2-local.nupkg'
if (-not (Test-Path -LiteralPath $contractsPackage -PathType Leaf)) {
    throw "Pinned Contracts package is missing: $contractsPackage"
}
$contractsCacheKey = (Hash-File $contractsPackage).Substring(0, 16)
$previousNugetPackages = $env:NUGET_PACKAGES
$env:NUGET_PACKAGES = Join-Path $root ("artifacts\creatoros-release\nuget-cache\contracts-" + $contractsCacheKey)
try {
    Invoke-Checked $DotNetPath @('publish', (Join-Path $root 'src\Quest.Studio.Host\Quest.Studio.Host.csproj'),
        '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:PublishSingleFile=false', '-o', $studioRoot, '-v:minimal')
}
finally {
    if ($null -eq $previousNugetPackages) { Remove-Item Env:NUGET_PACKAGES -ErrorAction SilentlyContinue }
    else { $env:NUGET_PACKAGES = $previousNugetPackages }
}

$clientRoot = Join-Path $OutDir 'client'
$payloadRoot = Join-Path $clientRoot 'payload'
$serverRoot = Join-Path $OutDir 'server'
$contentOut = Join-Path $OutDir 'content'
$evidenceOut = Join-Path $OutDir 'evidence'
foreach ($directory in @($clientRoot, $payloadRoot, $serverRoot, $contentOut, $evidenceOut)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
}

$runtimeBuild = Join-Path $root 'network\mod\ComfyQuestRuntime\bin\Release\net48'
$runtimeTarget = Join-Path $payloadRoot 'BepInEx\plugins\ComfyQuestRuntime.dll'
$contractsTarget = Join-Path $payloadRoot 'BepInEx\plugins\ComfyQuestContracts.dll'
$newtonsoftTarget = Join-Path $payloadRoot 'BepInEx\plugins\Newtonsoft.Json.dll'
$networkClientTarget = Join-Path $payloadRoot 'BepInEx\plugins\ComfyNetworkSense.dll'
$networkServerTarget = Join-Path $serverRoot 'BepInEx\plugins\ComfyNetworkSense.dll'
Copy-Required (Join-Path $runtimeBuild 'ComfyQuestRuntime.dll') $runtimeTarget
Copy-Required (Join-Path $runtimeBuild 'ComfyQuestContracts.dll') $contractsTarget
Copy-Required (Join-Path $runtimeBuild 'Newtonsoft.Json.dll') $newtonsoftTarget
Copy-Required $NetworkSenseDll $networkClientTarget
Copy-Required $NetworkSenseDll $networkServerTarget

$campaignSource = Join-Path $contentRoot 'campaign.json'
$campaign = Get-Content -LiteralPath $campaignSource -Raw -Encoding UTF8 | ConvertFrom-Json
$contentHash = [string]$campaign.pack.content_hash
if ($contentHash -notmatch '^[0-9a-f]{64}$') { throw 'Generated campaign has no valid content hash.' }
$questpackName = [string]$campaign.pack.path
$questpackTarget = Join-Path $payloadRoot "BepInEx\config\comfy-quest-runtime\inbox\$questpackName"
$viewTarget = Join-Path $payloadRoot 'BepInEx\config\comfy-network-sense\quest-view.json'
$runtimeConfigTarget = Join-Path $payloadRoot 'BepInEx\config\djcdevelopment.valheim.comfyquestruntime.cfg'
$networkClientConfigTarget = Join-Path $payloadRoot 'BepInEx\config\djcdevelopment.valheim.comfynetworksense.cfg'
$networkServerConfigTarget = Join-Path $serverRoot 'BepInEx\config\djcdevelopment.valheim.comfynetworksense.cfg'
Copy-Required (Join-Path $contentRoot $questpackName) $questpackTarget
Copy-Required (Join-Path $contentRoot 'quest-view.json') $viewTarget
foreach ($name in @('venue.json', 'campaign.json', 'content-manifest.json')) {
    Copy-Required (Join-Path $contentRoot $name) (Join-Path $contentOut $name)
}

$runtimeConfig = @"
[Runtime]
CreatorBarHotkey = F9
CheckHotkey = F10
LoadLatestHotkey = F11
CharmGestureHotkey = BackQuote

[Safety]
PrivateWorldConfirmed = false

[DedicatedPersonalProgression]
Enabled = true
WorldUid = $WorldUid
ContentHash = $contentHash
"@
[IO.File]::WriteAllText($runtimeConfigTarget, $runtimeConfig.TrimStart() + [Environment]::NewLine, $utf8)
$networkClientConfig = @"
[Lumberjacks]
lumberjacksCutoverMode = native
zdoAuthoritativeConsumerEnabled = false
lumberjacksMotionEnabled = false
lumberjacksTelemetryHeartbeatEnabled = false

[LumberjacksGameSession]
lumberjacksGameSessionEnabled = false

[Gameplay]
gameplayEventProducerEnabled = true
gameplayEventVerboseLogging = false
questEvaluatorEnabled = true

[Netcode]
zdoRedirectEnabled = false
handshakeResponderEnabled = false

[NativeCutover]
directControlCutoverEnabled = false
routedRpcCutoverEnabled = false
zdoJournalCutoverEnabled = false
zdoJournalCanonicalSessionEnabled = false
ownershipLeaseCutoverEnabled = false
worldZoneCutoverEnabled = false
motionAuthorityCutoverEnabled = false
socketQuarantineCutoverEnabled = false
logicalPeerCutoverEnabled = false
"@
$networkServerConfig = @"
[Lumberjacks]
lumberjacksGatewayUrl = http://gateway:4000
lumberjacksCutoverMode = native
zdoAuthoritativeConsumerEnabled = false
lumberjacksMotionEnabled = false
lumberjacksTelemetryHeartbeatEnabled = true

[LumberjacksGameSession]
lumberjacksGameSessionEnabled = false

[Gameplay]
gameplayEventProducerEnabled = true
gameplayEventVerboseLogging = false
questEvaluatorEnabled = true

[Netcode]
zdoRedirectEnabled = false
handshakeResponderEnabled = true
handshakeResponderEndpoint = http://gateway:4000
handshakeResponderStrictMode = true
handshakeResponderWindowId = creatoros-beta1
handshakeResponderActiveSeconds = 0

[NativeCutover]
directControlCutoverEnabled = false
routedRpcCutoverEnabled = false
zdoJournalCutoverEnabled = false
zdoJournalCanonicalSessionEnabled = false
ownershipLeaseCutoverEnabled = false
worldZoneCutoverEnabled = false
motionAuthorityCutoverEnabled = false
socketQuarantineCutoverEnabled = false
logicalPeerCutoverEnabled = false
"@
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $networkClientConfigTarget) | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $networkServerConfigTarget) | Out-Null
[IO.File]::WriteAllText($networkClientConfigTarget, $networkClientConfig.TrimStart() + [Environment]::NewLine, $utf8)
[IO.File]::WriteAllText($networkServerConfigTarget, $networkServerConfig.TrimStart() + [Environment]::NewLine, $utf8)

Copy-Required $WorldDb (Join-Path $serverRoot 'worlds_local\CreatorOSBeta1.db')
Copy-Required $WorldFwl (Join-Path $serverRoot 'worlds_local\CreatorOSBeta1.fwl')
Copy-Required (Join-Path $contentRoot 'venue.json') (Join-Path $serverRoot 'creatoros\venue.json')
Copy-Required (Join-Path $contentRoot 'campaign.json') (Join-Path $serverRoot 'creatoros\campaign.json')
Copy-Required (Join-Path $contentRoot 'quest-view.json') (Join-Path $serverRoot 'BepInEx\config\comfy-network-sense\quest-view.json')

$creatorAssets = Join-Path $contentRoot 'release-assets'
Copy-Required (Join-Path $creatorAssets 'Start-CreatorStudio.ps1') (Join-Path $OutDir 'creator-kit\Start-CreatorStudio.ps1')
Copy-Required (Join-Path $creatorAssets 'CREATOR-KIT.md') (Join-Path $OutDir 'creator-kit\README.md')
Copy-Required (Join-Path $creatorAssets 'review-template.json') (Join-Path $OutDir 'creator-kit\review-template.json')
Copy-Required (Join-Path $creatorAssets 'Install-CreatorOsBeta1.ps1') (Join-Path $clientRoot 'Install-CreatorOsBeta1.ps1')
if ($DiscoverlayHud) {
    Copy-Required $DiscoverlayHud (Join-Path $clientRoot 'optional-overlay\hud.exe')
    Copy-Required (Join-Path $creatorAssets 'Start-CreatorOsOverlay.ps1') `
        (Join-Path $clientRoot 'optional-overlay\Start-CreatorOsOverlay.ps1')
}

$installFiles = @()
Get-ChildItem -LiteralPath $payloadRoot -File -Recurse | Sort-Object FullName | ForEach-Object {
    $sourceRelative = $_.FullName.Substring($clientRoot.Length + 1).Replace('\', '/')
    $targetRelative = $_.FullName.Substring($payloadRoot.Length + 1).Replace('\', '/')
    $installFiles += [ordered]@{
        source = $sourceRelative
        target = $targetRelative
        sha256 = Hash-File $_.FullName
        bytes = $_.Length
    }
}
$installManifest = [ordered]@{
    schema = 'creatoros-beta-install-manifest/v1'
    release_id = 'creatoros-beta1'
    player_label = 'UNASSIGNED'
    world_name = 'CreatorOSBeta1'
    world_uid = $WorldUid
    pack_content_hash = $contentHash
    admission_credentials = 'out-of-band-one-time-invite'
    files = $installFiles
}
Write-Json (Join-Path $clientRoot 'install-manifest.json') $installManifest
$receiptTemplate = [ordered]@{
    schema = 'creatoros-beta-install-receipt/v1'
    release_id = 'creatoros-beta1'
    player_label = '<local-label-not-a-Steam-id>'
    installed_utc = '<written-by-installer>'
    world_name = 'CreatorOSBeta1'
    world_uid = $WorldUid
    pack_content_hash = $contentHash
    files = @()
}
Write-Json (Join-Path $evidenceOut 'install-receipt-template.json') $receiptTemplate

$worldDbTarget = Join-Path $serverRoot 'worlds_local\CreatorOSBeta1.db'
$worldFwlTarget = Join-Path $serverRoot 'worlds_local\CreatorOSBeta1.fwl'
$worldPairHash = Hash-NamedFiles @{
    'CreatorOSBeta1.db' = $worldDbTarget
    'CreatorOSBeta1.fwl' = $worldFwlTarget
}
$platformManifest = [ordered]@{
    schema = 'creatoros-beta-platform-manifest/v1'
    release_id = 'creatoros-beta1'
    server_mode = 'native-valheim'
    world = [ordered]@{
        name = 'CreatorOSBeta1'
        uid = $WorldUid
        pair_hash = $worldPairHash
        db_sha256 = Hash-File $worldDbTarget
        fwl_sha256 = Hash-File $worldFwlTarget
    }
    campaign = [ordered]@{
        id = [string]$campaign.campaign_id
        composition_hash = [string]$campaign.composition_hash
        pack_content_hash = $contentHash
    }
    plugins = [ordered]@{
        comfy_network_sense_sha256 = Hash-File $networkServerTarget
        comfy_quest_runtime_client_sha256 = Hash-File $runtimeTarget
        comfy_quest_contracts_client_sha256 = Hash-File $contractsTarget
        newtonsoft_json_client_sha256 = Hash-File $newtonsoftTarget
    }
    controls = [ordered]@{
        lumberjacks_custom_transport = 'off'
        native_valheim_networking = 'on'
        enrollment_admission = 'on-fail-closed'
        eventlog = 'on'
        dedicated_personal_progression = 'client-only-message-actions'
    }
}
Write-Json (Join-Path $serverRoot 'platform-manifest.json') $platformManifest

$statusText = ($dirtyRows -join "`n")
$statusHash = if ($statusText) {
    $bytes = [Text.Encoding]::UTF8.GetBytes($statusText)
    $sha = [Security.Cryptography.SHA256]::Create()
    try { ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace('-', '').ToLowerInvariant() }
    finally { $sha.Dispose() }
} else { $null }
$provenance = [ordered]@{
    schema = 'creatoros-beta-provenance/v1'
    release_id = 'creatoros-beta1'
    created_utc = (Get-Date).ToUniversalTime().ToString('o')
    comfy_quest = [ordered]@{
        repository = 'djcdevelopment/comfy-quest'
        revision = $revision
        clean = $clean
        working_tree_status_sha256 = $statusHash
    }
    comfy_network_sense = [ordered]@{
        revision = $NetworkSenseSourceRevision
        release = $NetworkSenseRelease
        dll_sha256 = Hash-File $NetworkSenseDll
    }
    discoverlay = if ($DiscoverlayHud) { [ordered]@{
        revision = $DiscoverlaySourceRevision
        hud_sha256 = Hash-File $DiscoverlayHud
        integration = 'optional-dumb-renderer'
    }} else { $null }
    content = [ordered]@{
        composition_hash = [string]$campaign.composition_hash
        pack_content_hash = $contentHash
    }
}
Write-Json (Join-Path $evidenceOut 'provenance.json') $provenance

$roleByPath = @{
    'server/worlds_local/CreatorOSBeta1.db' = 'world_db'
    'server/worlds_local/CreatorOSBeta1.fwl' = 'world_fwl'
    'content/venue.json' = 'venue'
    'content/campaign.json' = 'campaign'
    ('client/payload/BepInEx/config/comfy-quest-runtime/inbox/' + $questpackName) = 'questpack'
    'client/payload/BepInEx/config/comfy-network-sense/quest-view.json' = 'quest_view'
    'client/payload/BepInEx/plugins/ComfyQuestRuntime.dll' = 'runtime_dll'
    'client/payload/BepInEx/plugins/ComfyQuestContracts.dll' = 'contracts_dll'
    'client/payload/BepInEx/plugins/Newtonsoft.Json.dll' = 'newtonsoft_dll'
    'client/payload/BepInEx/plugins/ComfyNetworkSense.dll' = 'networksense_dll'
    'server/BepInEx/plugins/ComfyNetworkSense.dll' = 'server_networksense_dll'
    'client/payload/BepInEx/config/djcdevelopment.valheim.comfyquestruntime.cfg' = 'runtime_config'
    'client/payload/BepInEx/config/djcdevelopment.valheim.comfynetworksense.cfg' = 'networksense_client_config'
    'server/BepInEx/config/djcdevelopment.valheim.comfynetworksense.cfg' = 'networksense_server_config'
    'server/BepInEx/config/comfy-network-sense/quest-view.json' = 'server_quest_view'
    'server/creatoros/venue.json' = 'server_venue'
    'server/creatoros/campaign.json' = 'server_campaign'
    'creator-kit/studio/Comfy.Quest.Studio.Host.exe' = 'studio_host'
    'client/Install-CreatorOsBeta1.ps1' = 'install_script'
    'evidence/provenance.json' = 'provenance'
    'server/platform-manifest.json' = 'platform_manifest'
    'client/install-manifest.json' = 'install_manifest'
    'evidence/install-receipt-template.json' = 'install_receipt_template'
    'content/content-manifest.json' = 'content_manifest'
}
if ($DiscoverlayHud) {
    $roleByPath['client/optional-overlay/hud.exe'] = 'overlay_hud'
    $roleByPath['client/optional-overlay/Start-CreatorOsOverlay.ps1'] = 'overlay_driver'
}
$artifacts = @()
Get-ChildItem -LiteralPath $OutDir -File -Recurse | Sort-Object FullName | ForEach-Object {
    $relative = $_.FullName.Substring($OutDir.Length + 1).Replace('\', '/')
    $role = if ($roleByPath.ContainsKey($relative)) { [string]$roleByPath[$relative] }
        elseif ($relative.StartsWith('creator-kit/studio/', [StringComparison]::Ordinal)) { 'creator_studio_file' }
        elseif ($relative.StartsWith('creator-kit/', [StringComparison]::Ordinal)) { 'creator_kit_file' }
        elseif ($relative.StartsWith('server/', [StringComparison]::Ordinal)) { 'server_support_file' }
        elseif ($relative.StartsWith('client/', [StringComparison]::Ordinal)) { 'client_support_file' }
        elseif ($relative.StartsWith('content/', [StringComparison]::Ordinal)) { 'content_support_file' }
        else { 'evidence_support_file' }
    $artifacts += [ordered]@{
        path = $relative
        role = $role
        sha256 = Hash-File $_.FullName
        bytes = $_.Length
    }
}
$releaseManifest = [ordered]@{
    schema = 'creatoros-beta-release/v1'
    release_id = 'creatoros-beta1'
    release_state = if ($clean) { 'frozen' } else { 'candidate-dirty' }
    created_utc = (Get-Date).ToUniversalTime().ToString('o')
    source = [ordered]@{ repository = 'djcdevelopment/comfy-quest'; revision = $revision; clean = $clean }
    world = [ordered]@{ name = 'CreatorOSBeta1'; uid = $WorldUid; pair_hash = $worldPairHash }
    campaign = [ordered]@{
        campaign_id = [string]$campaign.campaign_id
        composition_hash = [string]$campaign.composition_hash
        pack_content_hash = $contentHash
    }
    network = [ordered]@{
        transport = 'native-valheim'
        lumberjacks_custom_transport = 'off'
        admission_credentials = 'out-of-band-one-time-invite'
    }
    compatibility = [ordered]@{
        built_for = 'pre-valheim-1.0'
        valheim_1_0_release_date = '2026-09-09'
        post_1_0_revalidation_required = $true
    }
    artifacts = $artifacts
}
Write-Json (Join-Path $OutDir 'release-manifest.json') $releaseManifest 16
$sumLines = $artifacts | ForEach-Object { [string]$_.sha256 + '  ' + [string]$_.path }
[IO.File]::WriteAllText((Join-Path $OutDir 'SHA256SUMS'), ($sumLines -join [Environment]::NewLine) + [Environment]::NewLine, $utf8)

Invoke-Checked 'python' @((Join-Path $root 'tools\release\verify_creatoros_beta_release.py'),
    '--release-dir', $OutDir)
Write-Output $OutDir
