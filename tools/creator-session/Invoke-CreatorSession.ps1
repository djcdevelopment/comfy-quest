<#
.SYNOPSIS
Operate one identity-pinned, receipt-backed Quest creator session.

.DESCRIPTION
This is the fleet-facing entrypoint for the scarce-seat workflow. Prepare runs while
Valheim is closed: it builds, backs up, deploys, opens the private-world safety setting,
and records exact install/inbox/world pins. After the creator enters that one world,
Gallery, blueprint, and Runtime operations use expiring bounded mailboxes; no F5 relay,
keystroke injection, arbitrary command, prefab, key, or caller-supplied artifact path.

Every invocation takes an exclusive lock inside the selected Valheim install. The active
session manifest is also install-local, so another checkout or process cannot silently
claim a different session through this entrypoint.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [ValidateSet('Prepare', 'Status', 'Launch', 'Stop', 'GalleryRebuild', 'Capture', 'Replay', 'Arm', 'Disarm', 'BuildOn', 'BuildOff', 'Close')]
    [string]$Action,

    [ValidatePattern('^[A-Za-z0-9._-]{1,80}$')]
    [string]$SessionId,

    [ValidateSet('omen', 'i5')]
    [string]$Lane = 'omen',

    [string]$ValheimRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim',

    [ValidatePattern('^[A-Za-z0-9._-]{1,80}$')]
    [string]$ExpectedMachine = $env:COMPUTERNAME,

    [ValidatePattern('^-?[0-9]{1,20}$')]
    [string]$WorldUid = '-7600395338659582326',

    [ValidatePattern('^[A-Za-z0-9._ -]{1,80}$')]
    [string]$WorldName = 'ComfyQuestDemo',

    [ValidatePattern('^[A-Za-z0-9._-]{1,80}$')]
    [string]$CharacterProfile = 'questyfour',

    [string]$SteamExe = 'C:\Program Files (x86)\Steam\steam.exe',

    [ValidateSet('classic', 'marble-wide', 'marble-grand')]
    [string]$Profile = 'marble-grand',

    [ValidatePattern('^[a-z0-9_-]{1,64}$')]
    [string]$BlueprintName,

    [ValidateRange(1, 40)]
    [double]$RadiusMetres = 20,

    [ValidateSet('mine', 'lab')]
    [string]$Selection = 'mine',

    [ValidateSet('ground', 'sky')]
    [string]$BuildMode = 'ground',

    [ValidateRange(1, 900)]
    [int]$WaitSeconds = 60,

    [string]$EvidenceRoot,

    [switch]$Replace,
    [switch]$Restore,
    [switch]$RestoreGameState,
    [switch]$NoBuild,
    [switch]$DryRun,
    [switch]$FixtureMode
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
& (Join-Path $repoRoot 'tools\Assert-RepoIdentity.ps1') | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Repository identity check failed.' }

function Resolve-ValheimRoot([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ([string]::IsNullOrWhiteSpace($resolved) -or
        [IO.Path]::GetPathRoot($resolved) -eq $resolved) {
        throw "Unsafe Valheim root: $Path"
    }
    if (-not (Test-Path -LiteralPath $resolved -PathType Container)) {
        throw "Valheim root does not exist: $resolved"
    }
    if ($FixtureMode) {
        if (-not (Test-Path -LiteralPath (Join-Path $resolved '.comfy-quest-creator-fixture'))) {
            throw 'FixtureMode requires .comfy-quest-creator-fixture in ValheimRoot.'
        }
    } elseif (-not (Test-Path -LiteralPath (Join-Path $resolved 'valheim.exe') -PathType Leaf)) {
        throw "Valheim executable not found under: $resolved"
    }
    return $resolved
}

function Test-ChildPath([string]$Parent, [string]$Child) {
    $parentFull = [IO.Path]::GetFullPath($Parent).TrimEnd('\', '/') + [IO.Path]::DirectorySeparatorChar
    $childFull = [IO.Path]::GetFullPath($Child)
    return $childFull.StartsWith($parentFull, [StringComparison]::OrdinalIgnoreCase)
}

function Get-Sha256([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $null }
    # Deliberately not Get-FileHash. It lives in Microsoft.PowerShell.Utility, and a hosted
    # runner has been observed failing to resolve it with the module present and its directory
    # on PSModulePath. Hashing installed bytes is a precondition for every later operation, so
    # it must not depend on module autoloading. See docs/creator-os-audit-2026-08-24.md D7.
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try {
        $stream = [System.IO.File]::OpenRead($Path)
        try {
            return [System.BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '').ToLowerInvariant()
        } finally { $stream.Dispose() }
    } finally { $sha.Dispose() }
}

function Read-SharedText([string]$Path) {
    $stream = [IO.FileStream]::new(
        $Path,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try {
        $reader = [IO.StreamReader]::new($stream, $true)
        try {
            return $reader.ReadToEnd()
        } finally { $reader.Dispose() }
    } finally { $stream.Dispose() }
}

function Copy-SharedFile([string]$Source, [string]$Destination) {
    $sourceStream = [IO.FileStream]::new(
        $Source,
        [IO.FileMode]::Open,
        [IO.FileAccess]::Read,
        ([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
    try {
        $destinationStream = [IO.FileStream]::new(
            $Destination,
            [IO.FileMode]::Create,
            [IO.FileAccess]::Write,
            [IO.FileShare]::None)
        try {
            $sourceStream.CopyTo($destinationStream)
        } finally { $destinationStream.Dispose() }
    } finally { $sourceStream.Dispose() }
}

function Get-WorldSupport([string]$ExpectedWorldUid, [string]$ExpectedWorldName) {
    if ($FixtureMode) { return $null }

    $catalogPath = Join-Path $repoRoot 'tools\creator-session\world-support.json'
    if (-not (Test-Path -LiteralPath $catalogPath -PathType Leaf)) {
        throw "Creator world-support catalog is missing: $catalogPath"
    }
    $catalog = [IO.File]::ReadAllText($catalogPath) | ConvertFrom-Json
    if ([string]$catalog.schema -ne 'comfy-quest-creator-world-support/v1') {
        throw 'Creator world-support catalog schema is unsupported.'
    }
    $matches = @($catalog.worlds | Where-Object {
            [string]$_.world_uid -eq $ExpectedWorldUid
        })
    if ($matches.Count -eq 0) { return $null }
    if ($matches.Count -ne 1) {
        throw "Creator world-support catalog has ambiguous entries for world UID $ExpectedWorldUid."
    }
    $world = $matches[0]
    if ([string]$world.world_name -ne $ExpectedWorldName) {
        throw "Creator world-support name mismatch for UID ${ExpectedWorldUid}: expected $ExpectedWorldName, catalog has $([string]$world.world_name)."
    }

    $manifestPath = [IO.Path]::GetFullPath((Join-Path $repoRoot ([string]$world.package_manifest)))
    if (-not (Test-ChildPath $repoRoot $manifestPath) -or
        -not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        throw "World-support package manifest is unavailable inside this repository: $manifestPath"
    }
    if ((Get-Sha256 $manifestPath) -ne [string]$world.package_manifest_sha256) {
        throw 'World-support package manifest hash mismatch.'
    }
    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    if ([string]$manifest.schema -ne 'comfy-quest-external-support-release/v1') {
        throw 'World-support package manifest schema is unsupported.'
    }
    $availableCapabilities = @($manifest.capabilities | ForEach-Object { [string]$_ })
    foreach ($capability in @($world.required_capabilities)) {
        if ([string]$capability -notin $availableCapabilities) {
            throw "World-support package lacks required capability: $capability"
        }
    }

    $packageRoot = Split-Path -Parent $manifestPath
    $pluginFiles = @()
    foreach ($file in @($manifest.files)) {
        $name = [string]$file.name
        if ($name -notmatch '^[A-Za-z0-9._-]+\.dll$') {
            throw "World-support plugin filename is unsafe: $name"
        }
        $source = [IO.Path]::GetFullPath((Join-Path $packageRoot $name))
        if (-not (Test-ChildPath $packageRoot $source) -or
            -not (Test-Path -LiteralPath $source -PathType Leaf)) {
            throw "World-support release file is missing: $source"
        }
        if ((Get-Item -LiteralPath $source).Length -ne [long]$file.bytes -or
            (Get-Sha256 $source) -ne [string]$file.sha256) {
            throw "World-support release identity mismatch: $name"
        }
        $pluginFiles += [pscustomobject]@{
            name = $name
            source = $source
            sha256 = [string]$file.sha256
        }
    }

    $configName = [string]$manifest.config.name
    if ($configName -notmatch '^[A-Za-z0-9._-]+\.cfg$') {
        throw "World-support config filename is unsafe: $configName"
    }
    $configSource = [IO.Path]::GetFullPath((Join-Path $packageRoot $configName))
    if (-not (Test-ChildPath $packageRoot $configSource) -or
        -not (Test-Path -LiteralPath $configSource -PathType Leaf) -or
        (Get-Sha256 $configSource) -ne [string]$manifest.config.sha256) {
        throw 'World-support configuration identity mismatch.'
    }

    return [pscustomobject]@{
        world_uid = [string]$world.world_uid
        world_name = [string]$world.world_name
        reason = [string]$world.reason
        package_id = [string]$manifest.package_id
        version = [string]$manifest.version
        release_id = [string]$manifest.release_id
        upstream_owner = [string]$manifest.upstream_owner
        manifest = $manifestPath
        manifest_sha256 = Get-Sha256 $manifestPath
        required_capabilities = @($world.required_capabilities)
        plugin_files = $pluginFiles
        config = [pscustomobject]@{
            name = $configName
            source = $configSource
            sha256 = [string]$manifest.config.sha256
            profile = [string]$manifest.config.profile
        }
        runtime_proof = $manifest.runtime_proof
    }
}

function Assert-SnapshotFile(
    $Record,
    [string]$ExpectedSource,
    [string]$SnapshotRoot,
    [string]$Label) {
    if ($null -eq $Record) { throw "$Label snapshot is missing." }
    $source = [IO.Path]::GetFullPath([string]$Record.source)
    $backup = [IO.Path]::GetFullPath([string]$Record.backup)
    $expected = [IO.Path]::GetFullPath($ExpectedSource)
    if ($source -ne $expected) { throw "$Label restore target differs from the pinned snapshot." }
    if (-not (Test-ChildPath $SnapshotRoot $backup)) {
        throw "$Label snapshot escaped the Creator Session evidence root."
    }
    if (-not (Test-Path -LiteralPath $backup -PathType Leaf) -or
        (Get-Sha256 $backup) -ne [string]$Record.sha256) {
        throw "$Label snapshot hash mismatch."
    }
}

function Restore-SnapshotFile(
    $Record,
    [string]$ExpectedSource,
    [string]$SnapshotRoot,
    [string]$Label) {
    Assert-SnapshotFile $Record $ExpectedSource $SnapshotRoot $Label
    $source = [IO.Path]::GetFullPath([string]$Record.source)
    $backup = [IO.Path]::GetFullPath([string]$Record.backup)
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $source) | Out-Null
    $temporary = $source + '.creator-restore'
    $previous = $source + '.creator-prior'
    $replaced = $false
    try {
        Copy-Item -LiteralPath $backup -Destination $temporary -Force
        if ((Get-Sha256 $temporary) -ne [string]$Record.sha256) {
            throw "$Label staged restore hash mismatch."
        }
        if (Test-Path -LiteralPath $source -PathType Leaf) {
            if (Test-Path -LiteralPath $previous -PathType Leaf) {
                throw "$Label prior-byte recovery file already exists."
            }
            [IO.File]::Replace($temporary, $source, $previous)
            $replaced = $true
        } else {
            [IO.File]::Move($temporary, $source)
        }
        if ((Get-Sha256 $source) -ne [string]$Record.sha256) {
            throw "$Label restored bytes do not match the pinned snapshot."
        }
        if ($replaced -and (Test-Path -LiteralPath $previous -PathType Leaf)) {
            Remove-Item -LiteralPath $previous -Force
        }
    } finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) {
            Remove-Item -LiteralPath $temporary -Force
        }
    }
}

function Get-InboxPins([string]$Root) {
    $pins = @()
    foreach ($relative in @(
            'BepInEx\config\comfy-quest-runtime\inbox',
            'BepInEx\config\comfy-quest-runtime\inbox-dev')) {
        $directory = Join-Path $Root $relative
        if (-not (Test-Path -LiteralPath $directory -PathType Container)) { continue }
        foreach ($file in Get-ChildItem -LiteralPath $directory -Filter '*.questpack' -File |
                Sort-Object Name) {
            $pins += [ordered]@{
                channel = Split-Path $directory -Leaf
                name = $file.Name
                sha256 = Get-Sha256 $file.FullName
            }
        }
    }
    return @($pins)
}

function Write-JsonAtomic([string]$Path, $Value) {
    $parent = Split-Path -Parent $Path
    New-Item -ItemType Directory -Force -Path $parent | Out-Null
    $temp = $Path + '.tmp'
    [IO.File]::WriteAllText(
        $temp,
        (($Value | ConvertTo-Json -Depth 12) + [Environment]::NewLine),
        (New-Object System.Text.UTF8Encoding($false)))
    if (Test-Path -LiteralPath $Path) { Remove-Item -LiteralPath $Path -Force }
    Move-Item -LiteralPath $temp -Destination $Path
}

function Test-ValheimRunning {
    if ($FixtureMode) { return $false }
    return $null -ne (Get-Process -Name 'valheim' -ErrorAction SilentlyContinue)
}

function Get-SteamAccountContext([string]$SteamExecutable = $SteamExe) {
    $steamPath = [IO.Path]::GetFullPath($SteamExecutable)
    $steamRoot = Split-Path -Parent $steamPath
    $loginUsersPath = Join-Path $steamRoot 'config\loginusers.vdf'
    if (-not (Test-Path -LiteralPath $loginUsersPath -PathType Leaf)) {
        throw "Steam login-user inventory was not found: $loginUsersPath"
    }

    $loginUsers = @()
    $loginText = [IO.File]::ReadAllText($loginUsersPath)
    $userBlocks = [regex]::Matches(
        $loginText,
        '"(?<steam_id>[0-9]{17})"\s*\{(?<body>.*?)\}',
        [Text.RegularExpressions.RegexOptions]::Singleline)
    foreach ($block in $userBlocks) {
        $body = $block.Groups['body'].Value
        $accountNameMatch = [regex]::Match($body, '"AccountName"\s*"(?<value>[^"]*)"')
        $personaNameMatch = [regex]::Match($body, '"PersonaName"\s*"(?<value>[^"]*)"')
        $steamId = [UInt64]::Parse(
            $block.Groups['steam_id'].Value,
            [Globalization.CultureInfo]::InvariantCulture)
        $accountId = $steamId - [UInt64]76561197960265728
        $loginUsers += [pscustomobject]@{
            account_id = $accountId.ToString([Globalization.CultureInfo]::InvariantCulture)
            account_name = if ($accountNameMatch.Success) { $accountNameMatch.Groups['value'].Value } else { $null }
            persona_name = if ($personaNameMatch.Success) { $personaNameMatch.Groups['value'].Value } else { $null }
        }
    }

    $steamRegistryPath = 'HKCU:\Software\Valve\Steam'
    $activeProcessPath = Join-Path $steamRegistryPath 'ActiveProcess'
    $activeAccountId = $null
    $selection = $null
    if (Test-Path $activeProcessPath) {
        $active = Get-ItemProperty -Path $activeProcessPath
        $activePid = [int]$active.pid
        $activeProcess = if ($activePid -gt 0) {
            Get-Process -Id $activePid -ErrorAction SilentlyContinue
        } else { $null }
        if ($activeProcess -and [UInt64]$active.ActiveUser -gt 0) {
            $activeProcessPathValue = $null
            try { $activeProcessPathValue = [IO.Path]::GetFullPath($activeProcess.Path) } catch { }
            if (-not $activeProcessPathValue -or
                -not [string]::Equals($activeProcessPathValue, $steamPath,
                    [StringComparison]::OrdinalIgnoreCase)) {
                throw 'The active Steam process does not match the configured Steam executable.'
            }
            $activeAccountId = ([UInt64]$active.ActiveUser).ToString(
                [Globalization.CultureInfo]::InvariantCulture)
            $selection = 'active_process'
        }
    }

    if (-not $activeAccountId) {
        if (-not (Test-Path $steamRegistryPath)) {
            throw 'Creator Session could not resolve the Steam account that would launch Valheim.'
        }
        $steam = Get-ItemProperty -Path $steamRegistryPath
        $autoLoginUser = [string]$steam.AutoLoginUser
        $autoLoginMatches = @($loginUsers | Where-Object {
                [string]::Equals([string]$_.account_name, $autoLoginUser,
                    [StringComparison]::OrdinalIgnoreCase)
            })
        if ([string]::IsNullOrWhiteSpace($autoLoginUser) -or $autoLoginMatches.Count -ne 1) {
            throw 'Creator Session requires one unambiguous selected Steam account before Prepare.'
        }
        $activeAccountId = [string]$autoLoginMatches[0].account_id
        $selection = 'auto_login'
    }

    $matches = @($loginUsers | Where-Object { [string]$_.account_id -eq $activeAccountId })
    if ($matches.Count -ne 1) {
        throw "Active Steam account $activeAccountId is absent or ambiguous in loginusers.vdf."
    }
    return [pscustomobject]@{
        account_id = $activeAccountId
        account_name = [string]$matches[0].account_name
        persona_name = [string]$matches[0].persona_name
        selection = $selection
    }
}

function Get-CharacterProfileFiles([string]$SteamAccountId) {
    $files = @()
    $localCharacters = Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim\characters'
    if (Test-Path -LiteralPath $localCharacters -PathType Container) {
        $files += Get-ChildItem -LiteralPath $localCharacters -Filter '*.fch' -File -ErrorAction SilentlyContinue
        $files += Get-ChildItem -LiteralPath $localCharacters -Filter '*.fch.new' -File -ErrorAction SilentlyContinue
    }
    $steamUserdata = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($SteamExe))) 'userdata'
    $steamCharacters = Join-Path $steamUserdata "$SteamAccountId\892970\remote\characters"
    if (Test-Path -LiteralPath $steamCharacters -PathType Container) {
        $files += Get-ChildItem -LiteralPath $steamCharacters -Filter '*.fch' -File -ErrorAction SilentlyContinue
        $files += Get-ChildItem -LiteralPath $steamCharacters -Filter '*.fch.new' -File -ErrorAction SilentlyContinue
    }
    return @($files | Where-Object { $_.Name -notmatch '(?i)backup' })
}

function Get-CharacterProfiles([string]$SteamAccountId) {
    return @(Get-CharacterProfileFiles $SteamAccountId |
        ForEach-Object { $_.Name -replace '(?i)\.fch(?:\.new)?$', '' } |
        Sort-Object -Unique)
}

function Get-CharacterProfileMetadata([string]$Path, [string]$ExpectedWorldUid) {
    $stream = [IO.File]::Open(
        $Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 8) { throw 'character_metadata_too_short' }
        $count = $reader.ReadInt32()
        if ($count -le 0 -or $count -gt 16MB -or $count -gt ($stream.Length - $stream.Position)) {
            throw 'character_metadata_payload_invalid'
        }
        $payload = $reader.ReadBytes($count)
        if ($payload.Length -ne $count) { throw 'character_metadata_payload_truncated' }
        $memory = [IO.MemoryStream]::new($payload, $false)
        $package = [IO.BinaryReader]::new($memory)
        try {
            $version = $package.ReadInt32()
            if ($version -lt 40) { throw "character_metadata_version_unsupported:$version" }
            $statCount = $package.ReadInt32()
            if ($statCount -lt 0 -or $statCount -gt 512) { throw 'character_metadata_stats_invalid' }
            for ($index = 0; $index -lt $statCount; $index++) { [void]$package.ReadSingle() }
            $firstSpawn = $package.ReadBoolean()
            $worldCount = $package.ReadInt32()
            if ($worldCount -lt 0 -or $worldCount -gt 128) { throw 'character_metadata_worlds_invalid' }
            $matchingWorlds = @()
            for ($index = 0; $index -lt $worldCount; $index++) {
                $uid = $package.ReadInt64().ToString([Globalization.CultureInfo]::InvariantCulture)
                $customSpawn = $package.ReadBoolean()
                $spawnPoint = @($package.ReadSingle(), $package.ReadSingle(), $package.ReadSingle())
                $logout = $package.ReadBoolean()
                $logoutPoint = @($package.ReadSingle(), $package.ReadSingle(), $package.ReadSingle())
                $death = $package.ReadBoolean()
                $deathPoint = @($package.ReadSingle(), $package.ReadSingle(), $package.ReadSingle())
                $homePoint = @($package.ReadSingle(), $package.ReadSingle(), $package.ReadSingle())
                $hasMap = $package.ReadBoolean()
                if ($hasMap) {
                    $mapBytes = $package.ReadInt32()
                    if ($mapBytes -lt 0 -or $mapBytes -gt ($memory.Length - $memory.Position)) {
                        throw 'character_metadata_map_invalid'
                    }
                    [void]$package.ReadBytes($mapBytes)
                }
                if ($uid -eq $ExpectedWorldUid) {
                    $matchingWorlds += [ordered]@{
                        uid = $uid
                        custom_spawn = $customSpawn
                        spawn_point = $spawnPoint
                        logout = $logout
                        logout_point = $logoutPoint
                        death = $death
                        death_point = $deathPoint
                        home_point = $homePoint
                    }
                }
            }
            $characterName = $package.ReadString()
            return [ordered]@{
                version = $version
                first_spawn = $firstSpawn
                character_name = $characterName
                matching_worlds = @($matchingWorlds)
            }
        } finally {
            $package.Dispose()
            $memory.Dispose()
        }
    } catch {
        throw "Could not read pinned Valheim character metadata from $Path`: $($_.Exception.Message)"
    } finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Get-WorldMetadata([string]$Path) {
    $stream = [IO.File]::Open(
        $Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    $reader = [IO.BinaryReader]::new($stream)
    try {
        if ($stream.Length -lt 8) { throw 'world_metadata_too_short' }
        $count = $reader.ReadInt32()
        if ($count -le 0 -or $count -gt 1MB -or $count -gt ($stream.Length - $stream.Position)) {
            throw 'world_metadata_payload_invalid'
        }
        $payload = $reader.ReadBytes($count)
        if ($payload.Length -ne $count) { throw 'world_metadata_payload_truncated' }
        $memory = [IO.MemoryStream]::new($payload, $false)
        $package = [IO.BinaryReader]::new($memory)
        try {
            $version = $package.ReadInt32()
            $displayName = $package.ReadString()
            $seedName = $package.ReadString()
            $seed = $package.ReadInt32()
            $uid = $package.ReadInt64()
            if ([string]::IsNullOrWhiteSpace($displayName) -or $uid -eq 0) {
                throw 'world_metadata_identity_invalid'
            }
            return [ordered]@{
                version = $version
                display_name = $displayName
                seed_name = $seedName
                seed = $seed
                uid = $uid.ToString([Globalization.CultureInfo]::InvariantCulture)
            }
        } finally {
            $package.Dispose()
            $memory.Dispose()
        }
    } catch {
        throw "Could not read pinned Valheim world metadata from $Path`: $($_.Exception.Message)"
    } finally {
        $reader.Dispose()
        $stream.Dispose()
    }
}

function Get-InteractiveSessionFacts {
    $current = [Diagnostics.Process]::GetCurrentProcess().SessionId
    $explorer = @(Get-Process explorer -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty SessionId -Unique)
    $steam = @(Get-Process steam -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty SessionId -Unique)
    return [ordered]@{
        current = $current
        explorer = $explorer
        steam = $steam
        current_is_interactive = $current -in $explorer -and $current -in $steam
    }
}

function Stop-ValheimProcess {
    if ($FixtureMode) {
        return [ordered]@{ state = 'fixture'; graceful = $true; forced_process_ids = @() }
    }
    $processes = @(Get-Process -Name valheim,UnityCrashHandler64 -ErrorAction SilentlyContinue)
    if ($processes.Count -eq 0) {
        return [ordered]@{ state = 'already_stopped'; graceful = $true; forced_process_ids = @() }
    }
    foreach ($process in $processes) {
        if ($process.MainWindowHandle -ne 0) { [void]$process.CloseMainWindow() }
    }
    # Large Valheim worlds continue their atomic .new save after OnApplicationQuit. ERA17 has
    # repeatedly needed about 45 seconds; killing at the former 20-second deadline interrupted
    # that write. Keep the caller's longer wait, but never allow less than two minutes.
    $gracefulWaitSeconds = [Math]::Max(120, $WaitSeconds)
    $deadline = (Get-Date).AddSeconds($gracefulWaitSeconds)
    while ((Get-Date) -lt $deadline -and
        (Get-Process -Name valheim,UnityCrashHandler64 -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 250
    }
    $forced = @(Get-Process -Name valheim,UnityCrashHandler64 -ErrorAction SilentlyContinue)
    if ($forced.Count -gt 0) {
        $forced | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    $forcedDeadline = (Get-Date).AddSeconds(10)
    while ((Get-Date) -lt $forcedDeadline -and
        (Get-Process -Name valheim,UnityCrashHandler64 -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 250
    }
    $remaining = @(Get-Process -Name valheim,UnityCrashHandler64 -ErrorAction SilentlyContinue)
    if ($remaining.Count -gt 0) {
        throw "Valheim did not stop; remaining process ids: $(@($remaining.Id) -join ',')"
    }
    return [ordered]@{
        state = if ($forced.Count -eq 0) { 'stopped_gracefully' } else { 'stopped_forcibly' }
        graceful = $forced.Count -eq 0
        graceful_wait_seconds = $gracefulWaitSeconds
        forced_process_ids = @($forced | ForEach-Object { $_.Id })
    }
}

function Set-PrivateWorldConfirmation([string]$Path, [bool]$Enabled) {
    $value = if ($Enabled) { 'true' } else { 'false' }
    $text = if (Test-Path -LiteralPath $Path) { [IO.File]::ReadAllText($Path) } else { '' }
    $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    if ($text -match '(?m)^\s*PrivateWorldConfirmed\s*=') {
        $text = [regex]::Replace(
            $text,
            '(?m)^(\s*PrivateWorldConfirmed\s*=\s*)\S+\s*$',
            ('$1' + $value))
    } else {
        if ($text.Length -gt 0 -and -not $text.EndsWith($newline)) { $text += $newline }
        $text += '[Safety]' + $newline + 'PrivateWorldConfirmed = ' + $value + $newline
    }
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Path) | Out-Null
    [IO.File]::WriteAllText($Path, $text, (New-Object System.Text.UTF8Encoding($false)))
}

function Invoke-ChildScript([string]$Script, [string[]]$Arguments) {
    & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $Script @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$(Split-Path $Script -Leaf) failed with exit code $LASTEXITCODE"
    }
}

function Invoke-GodbuildImport([string]$CapturePath, [switch]$Check) {
    $arguments = @(
        (Join-Path $repoRoot 'tools\blueprints\import_capture.py'),
        $CapturePath)
    if ($Check) { $arguments += '--check' }
    & python @arguments
    if ($LASTEXITCODE -ne 0) {
        $mode = if ($Check) { 'drift check' } else { 'import' }
        throw "Godbuild $mode failed with exit code $LASTEXITCODE"
    }
}

function Stage-ReviewedGodbuild([string]$Name, [string]$Root) {
    $sourceRoot = Join-Path $repoRoot "examples\worldbuild\$Name"
    $captureSource = Join-Path $sourceRoot "$Name.capture.json"
    $blueprintSource = Join-Path $sourceRoot "$Name.blueprint"
    $manifestPath = Join-Path $sourceRoot 'manifest.json'
    foreach ($required in @($captureSource, $blueprintSource, $manifestPath)) {
        if (-not (Test-Path -LiteralPath $required -PathType Leaf)) {
            throw "Reviewed Godbuild artifact is missing: $required"
        }
    }
    Invoke-GodbuildImport $captureSource -Check
    $manifest = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
    if ($manifest.schema -ne 'comfy-quest-godbuild/v1' -or $manifest.name -ne $Name -or
        $manifest.replay.check_before_build -ne $true -or
        $manifest.replay.post_build_proof -notmatch 'MATCH') {
        throw 'Reviewed Godbuild manifest does not authorize check-before-build replay.'
    }
    foreach ($source in @($captureSource, $blueprintSource)) {
        $leaf = Split-Path $source -Leaf
        $record = $manifest.artifacts.PSObject.Properties[$leaf].Value
        if ($null -eq $record -or (Get-Sha256 $source) -ne [string]$record.sha256) {
            throw "Reviewed Godbuild hash mismatch: $leaf"
        }
    }

    $targetRoot = Join-Path $Root 'BepInEx\config\comfy-quest-lab\blueprints'
    if (-not (Test-ChildPath $Root $targetRoot)) {
        throw 'Quest Lab blueprint directory escaped ValheimRoot.'
    }
    New-Item -ItemType Directory -Force -Path $targetRoot | Out-Null
    $staged = @()
    try {
        foreach ($source in @($captureSource, $blueprintSource)) {
            $leaf = Split-Path $source -Leaf
            $target = Join-Path $targetRoot $leaf
            $temporary = $target + '.creator-staging'
            $previous = $target + '.creator-prior'
            if (Test-Path -LiteralPath $previous -PathType Leaf) {
                throw "Godbuild recovery file already exists: $previous"
            }
            Copy-Item -LiteralPath $source -Destination $temporary -Force
            if ((Get-Sha256 $source) -ne (Get-Sha256 $temporary)) {
                throw "Godbuild staging hash mismatch: $leaf"
            }
            $staged += [pscustomobject]@{
                Source = $source
                Target = $target
                Temporary = $temporary
                Previous = $previous
            }
        }
        foreach ($item in $staged) {
            if (Test-Path -LiteralPath $item.Target -PathType Leaf) {
                [IO.File]::Replace($item.Temporary, $item.Target, $item.Previous)
            } else {
                [IO.File]::Move($item.Temporary, $item.Target)
            }
            if ((Get-Sha256 $item.Source) -ne (Get-Sha256 $item.Target)) {
                throw "Godbuild deployment hash mismatch: $(Split-Path $item.Target -Leaf)"
            }
            if (Test-Path -LiteralPath $item.Previous -PathType Leaf) {
                Remove-Item -LiteralPath $item.Previous -Force
            }
        }
    } finally {
        foreach ($item in $staged) {
            if (Test-Path -LiteralPath $item.Temporary) {
                Remove-Item -LiteralPath $item.Temporary -Force
            }
        }
    }
    return $sourceRoot
}

$ValheimRoot = Resolve-ValheimRoot $ValheimRoot
$creatorRoot = Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-creator'
$contextPath = Join-Path $creatorRoot 'session.json'
$lockPath = Join-Path $creatorRoot 'install.lock'
if (-not (Test-ChildPath $ValheimRoot $creatorRoot)) {
    throw 'Creator Session directory escaped ValheimRoot.'
}
New-Item -ItemType Directory -Force -Path $creatorRoot | Out-Null
$lease = $null
try {
    try {
        $lease = [IO.File]::Open(
            $lockPath,
            [IO.FileMode]::OpenOrCreate,
            [IO.FileAccess]::ReadWrite,
            [IO.FileShare]::None)
    } catch {
        throw 'Another process owns the install-wide Creator Session lease.'
    }

    if ($DryRun) {
        [pscustomobject]@{
            schema = 'comfy-quest-creator-session-plan/v1'
            action = $Action
            lane = $Lane
            valheim_root = $ValheimRoot
            expected_machine = $ExpectedMachine
            world_uid = $WorldUid
            world_name = $WorldName
            character_profile = $CharacterProfile
            steam_exe = $SteamExe
            session_id = $SessionId
            blueprint_name = $BlueprintName
            radius_metres = $RadiusMetres
            selection = $Selection
            build_mode = $BuildMode
            restore_game_state = [bool]$RestoreGameState
        } | ConvertTo-Json -Depth 5
        exit 0
    }

    $context = $null
    if (Test-Path -LiteralPath $contextPath) {
        $context = [IO.File]::ReadAllText($contextPath) | ConvertFrom-Json
    }

    if ($Action -eq 'Prepare') {
        if ($context -and $context.state -eq 'active') {
            throw "Creator Session $($context.session_id) already owns this install. Close it first."
        }
        if (Test-ValheimRunning) { throw 'Prepare requires Valheim to be closed.' }
        if (-not $SessionId) {
            $SessionId = 'creator-' + [DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' +
                [Guid]::NewGuid().ToString('N').Substring(0, 8)
        }
        if (-not $EvidenceRoot) {
            $EvidenceRoot = Join-Path $repoRoot "captures\creator-session\$SessionId"
        }
        $EvidenceRoot = [IO.Path]::GetFullPath($EvidenceRoot)
        New-Item -ItemType Directory -Force -Path $EvidenceRoot | Out-Null
        $backupRoot = Join-Path $EvidenceRoot 'backup'
        New-Item -ItemType Directory -Force -Path $backupRoot | Out-Null
        $worldMetadata = [ordered]@{
            version = $null
            display_name = $WorldName
            seed_name = $null
            seed = $null
            uid = $WorldUid
        }
        $characterMetadata = [ordered]@{
            version = $null
            first_spawn = $null
            character_name = $CharacterProfile
            matching_worlds = @([ordered]@{ uid = $WorldUid })
        }
        $worldEntryQuarantine = @()
        $runtimeRoot = Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-runtime'
        $deploymentRollback = @()
        $configPrepared = $false
        $activeContextWritten = $false
        $runtimeConfig = $null
        $configBackup = $null
        $configExisted = $false
        $characterBackup = $null
        $steamAccount = $null
        $worldSupport = $null
        $supportConfigRollback = @()
        $supportConfigRecords = @()
        $supportRequestQuarantine = @()
        try {

        if (-not $FixtureMode) {
            $SteamExe = [IO.Path]::GetFullPath($SteamExe)
            if (-not (Test-Path -LiteralPath $SteamExe -PathType Leaf)) {
                throw "Steam executable not found: $SteamExe"
            }
            $steamAccount = Get-SteamAccountContext
            if ($CharacterProfile -notin @(Get-CharacterProfiles $steamAccount.account_id)) {
                throw "Pinned Valheim character profile was not found for active Steam account $($steamAccount.account_id): $CharacterProfile"
            }
            $characterFiles = @(Get-CharacterProfileFiles $steamAccount.account_id | Where-Object {
                    ($_.Name -replace '(?i)\.fch(?:\.new)?$', '') -eq $CharacterProfile
                })
            if ($characterFiles.Count -ne 1) {
                throw "Pinned Valheim character profile is ambiguous across active-account save sources: $CharacterProfile"
            }
            $characterMetadata = Get-CharacterProfileMetadata $characterFiles[0].FullName $WorldUid
            if (@($characterMetadata.matching_worlds).Count -ne 1) {
                throw "Pinned character profile $CharacterProfile has no unique saved state for world UID $WorldUid."
            }
            $worldRoot = Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim\worlds_local'
            $missingWorldFiles = @('.db', '.fwl') | ForEach-Object {
                Join-Path $worldRoot ($WorldName + $_)
            } | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }
            if (@($missingWorldFiles).Count -ne 0) {
                throw "Canonical world pair was not found for $WorldName."
            }
            $worldMetadata = Get-WorldMetadata (Join-Path $worldRoot ($WorldName + '.fwl'))
            if ([string]$worldMetadata.uid -ne $WorldUid) {
                throw "Pinned world UID $WorldUid differs from $WorldName.fwl UID $($worldMetadata.uid)."
            }
            if ([string]$worldMetadata.display_name -notmatch '^[A-Za-z0-9._ -]{1,80}$') {
                throw "Pinned world display name is outside the bounded world-entry contract: $($worldMetadata.display_name)"
            }
        }
        $worldSupport = Get-WorldSupport $WorldUid $WorldName
        if ($worldSupport) {
            if (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('COMFY_AUTOJOIN'))) {
                throw 'World-support preflight refused inherited COMFY_AUTOJOIN automation.'
            }
            $supportRequestPath = Join-Path $ValheimRoot 'BepInEx\config\comfy-network-sense\native-autotest-request.json'
            if (Test-Path -LiteralPath $supportRequestPath -PathType Leaf) {
                $supportRequestBackup = Join-Path $backupRoot 'world-support-state\native-autotest-request.json'
                New-Item -ItemType Directory -Force -Path (Split-Path -Parent $supportRequestBackup) | Out-Null
                Copy-Item -LiteralPath $supportRequestPath -Destination $supportRequestBackup -Force
                Remove-Item -LiteralPath $supportRequestPath -Force
                $supportRequestQuarantine += [ordered]@{
                    source = $supportRequestPath
                    backup = $supportRequestBackup
                    sha256 = Get-Sha256 $supportRequestBackup
                }
            }
        }
        foreach ($relative in @('requests\world-entry.json', 'status\world-entry.json')) {
            $source = Join-Path $runtimeRoot $relative
            if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
            if (-not (Test-ChildPath $ValheimRoot $source)) {
                throw "World-entry state escaped ValheimRoot: $source"
            }
            $backup = Join-Path $backupRoot ('world-entry-state\' + $relative)
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backup) | Out-Null
            Copy-Item -LiteralPath $source -Destination $backup -Force
            Remove-Item -LiteralPath $source -Force
            $worldEntryQuarantine += [ordered]@{
                source = $source
                backup = $backup
                sha256 = Get-Sha256 $backup
            }
        }

        if (-not $NoBuild -and -not $FixtureMode) {
            & dotnet build (Join-Path $repoRoot 'network\mod\ComfyQuestLab\ComfyQuestLab.csproj') -c Release
            if ($LASTEXITCODE -ne 0) { throw 'Quest Lab Release build failed.' }
            & dotnet build (Join-Path $repoRoot 'network\mod\ComfyQuestRuntime\ComfyQuestRuntime.csproj') -c Release
            if ($LASTEXITCODE -ne 0) { throw 'Quest Runtime Release build failed.' }
        }

        $pluginRoot = Join-Path $ValheimRoot 'BepInEx\plugins'
        New-Item -ItemType Directory -Force -Path $pluginRoot | Out-Null
        $sources = [ordered]@{
            'ComfyQuestLab.dll' = Join-Path $repoRoot 'network\mod\ComfyQuestLab\bin\Release\ComfyQuestLab.dll'
            'ComfyQuestRuntime.dll' = Join-Path $repoRoot 'network\mod\ComfyQuestRuntime\bin\Release\net48\ComfyQuestRuntime.dll'
            'ComfyQuestContracts.dll' = Join-Path $repoRoot 'network\mod\ComfyQuestContracts\bin\Release\netstandard2.0\ComfyQuestContracts.dll'
            'Newtonsoft.Json.dll' = Join-Path $repoRoot 'network\mod\ComfyQuestRuntime\bin\Release\net48\Newtonsoft.Json.dll'
        }
        $supportPluginNames = @()
        if ($worldSupport) {
            foreach ($supportPlugin in @($worldSupport.plugin_files)) {
                $supportName = [string]$supportPlugin.name
                if ($sources.Contains($supportName)) {
                    throw "World-support plugin collides with a Quest deployment file: $supportName"
                }
                $sources[$supportName] = [string]$supportPlugin.source
                $supportPluginNames += $supportName
            }
        }
        if ($FixtureMode) {
            foreach ($name in @($sources.Keys)) {
                $sources[$name] = Join-Path $ValheimRoot "fixture-source\$name"
            }
        }
        foreach ($name in $sources.Keys) {
            $candidate = [string]$sources[$name]
            if (-not (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                throw "Built plugin is missing: $candidate"
            }
        }
        $plugins = @()
        foreach ($name in $sources.Keys) {
            $source = [string]$sources[$name]
            $target = Join-Path $pluginRoot $name
            if (-not (Test-ChildPath $ValheimRoot $target)) { throw "Plugin target escaped root: $target" }
            $backup = Join-Path $backupRoot "plugins\$name"
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backup) | Out-Null
            $existed = Test-Path -LiteralPath $target -PathType Leaf
            if ($existed) { Copy-Item -LiteralPath $target -Destination $backup -Force }
            $deploymentRollback += [ordered]@{
                name = $name
                target = $target
                backup = $backup
                existed = $existed
            }
            Copy-Item -LiteralPath $source -Destination $target -Force
            if ((Get-Sha256 $source) -ne (Get-Sha256 $target)) {
                throw "Plugin deployment hash mismatch: $name"
            }
            $plugins += [ordered]@{
                name = $name
                source = $source
                source_sha256 = Get-Sha256 $source
                target = $target
                installed_sha256 = Get-Sha256 $target
                backup = $backup
                existed = $existed
                external_support = $name -in $supportPluginNames
            }
        }

        $runtimeConfig = Join-Path $ValheimRoot 'BepInEx\config\djcdevelopment.valheim.comfyquestruntime.cfg'
        $configBackup = Join-Path $backupRoot 'config\djcdevelopment.valheim.comfyquestruntime.cfg'
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $configBackup) | Out-Null
        $configExisted = Test-Path -LiteralPath $runtimeConfig -PathType Leaf
        if ($configExisted) { Copy-Item -LiteralPath $runtimeConfig -Destination $configBackup -Force }
        $configPrepared = $true
        Set-PrivateWorldConfirmation $runtimeConfig $true

        if ($worldSupport) {
            $supportConfigSource = [string]$worldSupport.config.source
            $supportConfigTarget = Join-Path $ValheimRoot ('BepInEx\config\' + [string]$worldSupport.config.name)
            if (-not (Test-ChildPath $ValheimRoot $supportConfigTarget)) {
                throw 'World-support config target escaped ValheimRoot.'
            }
            $supportConfigBackup = Join-Path $backupRoot ('config\' + [string]$worldSupport.config.name)
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $supportConfigBackup) | Out-Null
            $supportConfigExisted = Test-Path -LiteralPath $supportConfigTarget -PathType Leaf
            if ($supportConfigExisted) {
                Copy-Item -LiteralPath $supportConfigTarget -Destination $supportConfigBackup -Force
            }
            $supportConfigRollback += [ordered]@{
                target = $supportConfigTarget
                backup = $supportConfigBackup
                existed = $supportConfigExisted
            }
            Copy-Item -LiteralPath $supportConfigSource -Destination $supportConfigTarget -Force
            if ((Get-Sha256 $supportConfigTarget) -ne [string]$worldSupport.config.sha256) {
                throw 'World-support config deployment hash mismatch.'
            }
            $supportConfigRecords += [ordered]@{
                name = [string]$worldSupport.config.name
                source = $supportConfigSource
                source_sha256 = [string]$worldSupport.config.sha256
                target = $supportConfigTarget
                installed_sha256 = Get-Sha256 $supportConfigTarget
                backup = $supportConfigBackup
                existed = $supportConfigExisted
                profile = [string]$worldSupport.config.profile
            }
        }

        $worldFiles = @()
        if (-not $FixtureMode) {
            $worldRoot = Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim\worlds_local'
            foreach ($extension in @('.db', '.fwl')) {
                $sourceWorld = Join-Path $worldRoot ($WorldName + $extension)
                if (Test-Path -LiteralPath $sourceWorld -PathType Leaf) {
                    $backupWorld = Join-Path $backupRoot ('world\' + $WorldName + $extension)
                    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backupWorld) | Out-Null
                    Copy-Item -LiteralPath $sourceWorld -Destination $backupWorld -Force
                    $worldFiles += [ordered]@{
                        source = $sourceWorld
                        backup = $backupWorld
                        sha256 = Get-Sha256 $backupWorld
                    }
                }
            }
            if ($worldFiles.Count -ne 2) {
                throw "Canonical world pair was not found for $WorldName."
            }

            $sourceCharacter = [IO.Path]::GetFullPath($characterFiles[0].FullName)
            $backupCharacter = Join-Path $backupRoot ('character\' + $characterFiles[0].Name)
            New-Item -ItemType Directory -Force -Path (Split-Path -Parent $backupCharacter) | Out-Null
            Copy-Item -LiteralPath $sourceCharacter -Destination $backupCharacter -Force
            $characterBackup = [ordered]@{
                source = $sourceCharacter
                backup = $backupCharacter
                sha256 = Get-Sha256 $backupCharacter
            }
        }

        $worldSupportRecord = if ($worldSupport) {
            [ordered]@{
                package_id = [string]$worldSupport.package_id
                version = [string]$worldSupport.version
                release_id = [string]$worldSupport.release_id
                upstream_owner = [string]$worldSupport.upstream_owner
                reason = [string]$worldSupport.reason
                manifest = [string]$worldSupport.manifest
                manifest_sha256 = [string]$worldSupport.manifest_sha256
                required_capabilities = @($worldSupport.required_capabilities)
                configs = @($supportConfigRecords)
                quarantined_requests = @($supportRequestQuarantine)
                runtime_proof = $worldSupport.runtime_proof
            }
        } else { $null }

        $context = [ordered]@{
            schema = 'comfy-quest-creator-session/v1'
            state = 'active'
            session_id = $SessionId
            lane = $Lane
            expected_machine = $ExpectedMachine
            world_uid = $WorldUid
            world_name = $WorldName
            world_display_name = [string]$worldMetadata.display_name
            world_metadata = $worldMetadata
            character_profile = $CharacterProfile
            character_metadata = $characterMetadata
            steam_exe = $SteamExe
            steam_account = $steamAccount
            prepared_utc = [DateTimeOffset]::UtcNow.ToString('o')
            repo_root = $repoRoot
            repo_commit = (& git -C $repoRoot rev-parse HEAD).Trim()
            valheim_root = $ValheimRoot
            evidence_root = $EvidenceRoot
            plugins = $plugins
            runtime_config = [ordered]@{
                path = $runtimeConfig
                backup = $configBackup
                existed = $configExisted
                armed_sha256 = Get-Sha256 $runtimeConfig
            }
            world_backup = $worldFiles
            character_backup = $characterBackup
            world_support = $worldSupportRecord
            world_entry_quarantine = $worldEntryQuarantine
            inbox_pins = Get-InboxPins $ValheimRoot
            rollback = "tools\creator-session\Invoke-CreatorSession.ps1 Close -SessionId $SessionId -Restore"
        }
        Write-JsonAtomic (Join-Path $EvidenceRoot 'session.json') $context
        Write-JsonAtomic $contextPath $context
        $activeContextWritten = $true
        $context | ConvertTo-Json -Depth 12
        exit 0
        } catch {
            $prepareError = $_.Exception.Message
            $rollbackErrors = @()
            for ($index = $deploymentRollback.Count - 1; $index -ge 0; $index--) {
                $entry = $deploymentRollback[$index]
                try {
                    if ($entry.existed) {
                        Copy-Item -LiteralPath ([string]$entry.backup) `
                            -Destination ([string]$entry.target) -Force
                    } elseif (Test-Path -LiteralPath ([string]$entry.target) -PathType Leaf) {
                        Remove-Item -LiteralPath ([string]$entry.target) -Force
                    }
                } catch { $rollbackErrors += "plugin:$([string]$entry.name):$($_.Exception.Message)" }
            }
            for ($index = $supportConfigRollback.Count - 1; $index -ge 0; $index--) {
                $entry = $supportConfigRollback[$index]
                try {
                    if ($entry.existed) {
                        Copy-Item -LiteralPath ([string]$entry.backup) `
                            -Destination ([string]$entry.target) -Force
                    } elseif (Test-Path -LiteralPath ([string]$entry.target) -PathType Leaf) {
                        Remove-Item -LiteralPath ([string]$entry.target) -Force
                    }
                } catch { $rollbackErrors += "support_config:$($_.Exception.Message)" }
            }
            if ($configPrepared) {
                try {
                    if ($configExisted) {
                        Copy-Item -LiteralPath $configBackup -Destination $runtimeConfig -Force
                    } elseif (Test-Path -LiteralPath $runtimeConfig -PathType Leaf) {
                        Remove-Item -LiteralPath $runtimeConfig -Force
                    }
                } catch { $rollbackErrors += "config:$($_.Exception.Message)" }
            }
            foreach ($entry in $supportRequestQuarantine) {
                try {
                    New-Item -ItemType Directory -Force -Path `
                        (Split-Path -Parent ([string]$entry.source)) | Out-Null
                    Copy-Item -LiteralPath ([string]$entry.backup) `
                        -Destination ([string]$entry.source) -Force
                } catch { $rollbackErrors += "support_request:$($_.Exception.Message)" }
            }
            foreach ($entry in $worldEntryQuarantine) {
                try {
                    New-Item -ItemType Directory -Force -Path `
                        (Split-Path -Parent ([string]$entry.source)) | Out-Null
                    Copy-Item -LiteralPath ([string]$entry.backup) `
                        -Destination ([string]$entry.source) -Force
                } catch { $rollbackErrors += "world-entry-state:$($_.Exception.Message)" }
            }
            if ($activeContextWritten) {
                try { Remove-Item -LiteralPath $contextPath -Force }
                catch { $rollbackErrors += "session-manifest:$($_.Exception.Message)" }
            }
            try {
                Write-JsonAtomic (Join-Path $EvidenceRoot 'preparation-failure.json') ([ordered]@{
                        schema = 'comfy-quest-creator-session-preparation-failure/v1'
                        session_id = $SessionId
                        failed_utc = [DateTimeOffset]::UtcNow.ToString('o')
                        error = $prepareError
                        rollback_errors = $rollbackErrors
                        state = if ($rollbackErrors.Count -eq 0) { 'rolled_back' } else { 'rollback_incomplete' }
                    })
            } catch { $rollbackErrors += "failure-receipt:$($_.Exception.Message)" }
            if ($rollbackErrors.Count -ne 0) {
                throw "Creator Session Prepare failed: $prepareError; rollback incomplete: $($rollbackErrors -join '; ')"
            }
            throw "Creator Session Prepare failed and rolled back: $prepareError"
        }
    }

    if (-not $context -or $context.state -ne 'active') {
        throw 'No active Creator Session. Run Prepare while Valheim is closed.'
    }
    if ($SessionId -and $SessionId -ne $context.session_id) {
        throw "Session mismatch: active is $($context.session_id), requested $SessionId."
    }
    $SessionId = [string]$context.session_id
    if (($PSBoundParameters.ContainsKey('ExpectedMachine') -and
            $ExpectedMachine -ne [string]$context.expected_machine) -or
        ($PSBoundParameters.ContainsKey('WorldUid') -and
            $WorldUid -ne [string]$context.world_uid) -or
        [IO.Path]::GetFullPath([string]$context.valheim_root) -ne $ValheimRoot) {
        throw 'Creator Session identity differs from the active install manifest.'
    }
    $pluginHashMismatches = @($context.plugins | Where-Object {
            (Get-Sha256 ([string]$_.target)) -ne [string]$_.installed_sha256
        } | ForEach-Object { [string]$_.name })
    if ($pluginHashMismatches.Count -ne 0 -and $Action -ne 'Stop') {
        throw "Installed bytes changed during Creator Session: $($pluginHashMismatches -join ', ')"
    }
    $supportConfigs = @()
    $supportRequests = @()
    if ($null -ne $context.world_support) {
        $supportConfigs = @($context.world_support.configs)
        $supportRequests = @($context.world_support.quarantined_requests)
    }
    $supportConfigHashMismatches = @($supportConfigs | Where-Object {
            (Get-Sha256 ([string]$_.target)) -ne [string]$_.installed_sha256
        } | ForEach-Object { [string]$_.name })
    if ($supportConfigHashMismatches.Count -ne 0 -and $Action -ne 'Stop') {
        throw "World-support configuration changed during Creator Session: $($supportConfigHashMismatches -join ', ')"
    }
    if ($RestoreGameState -and $Action -ne 'Close') {
        throw 'RestoreGameState is available only with Close.'
    }
    if ($RestoreGameState -and -not $Restore) {
        throw 'RestoreGameState requires Close -Restore so game and install bytes roll back together.'
    }
    if ($Action -eq 'Close' -and ($Restore -or $RestoreGameState) -and (Test-ValheimRunning)) {
        throw 'Restore requires Valheim to be closed.'
    }
    $EvidenceRoot = [string]$context.evidence_root
    $operationRoot = Join-Path $EvidenceRoot ([DateTimeOffset]::UtcNow.ToString('yyyyMMddTHHmmssZ') + '-' + $Action.ToLowerInvariant())
    New-Item -ItemType Directory -Force -Path $operationRoot | Out-Null

    if ($Action -eq 'Status') {
        [pscustomobject]@{
            schema = 'comfy-quest-creator-session-status/v1'
            session_id = $SessionId
            state = $context.state
            machine = $context.expected_machine
            world_uid = $context.world_uid
            world_name = $context.world_name
            world_display_name = $context.world_display_name
            character_profile = $context.character_profile
            plugin_hashes_match = $true
            support_config_hashes_match = $true
            world_support = $context.world_support
            valheim_running = Test-ValheimRunning
            prepared_inbox = $context.inbox_pins
            current_inbox = Get-InboxPins $ValheimRoot
            runtime_status = if (Test-Path -LiteralPath (Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-runtime\status\dev-channel.json')) {
                [IO.File]::ReadAllText((Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-runtime\status\dev-channel.json')) | ConvertFrom-Json
            } else { $null }
            world_entry_status = if (Test-Path -LiteralPath (Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-runtime\status\world-entry.json')) {
                [IO.File]::ReadAllText((Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-runtime\status\world-entry.json')) | ConvertFrom-Json
            } else { $null }
        } | ConvertTo-Json -Depth 12
        exit 0
    }

    $batchScript = Join-Path $repoRoot 'tools\questlab-batch\Invoke-I5QuestLabBatch.ps1'
    $runtimeScript = Join-Path $repoRoot 'tools\creator-session\Invoke-RuntimeCreatorRequest.ps1'
    $identityArgs = @(
        '-Lane', $Lane,
        '-ExpectedMachine', [string]$context.expected_machine,
        '-ExpectedWorldUid', [string]$context.world_uid,
        '-CreatorSessionId', $SessionId,
        '-OmenValheimRoot', [string]$context.valheim_root,
        '-WaitSeconds', [string][Math]::Min($WaitSeconds, 60),
        '-OutputDirectory', $operationRoot)
    $godbuildDirectory = $null
    $worldEntryReceipt = $null
    $processLifecycle = $null
    $supportRuntimeProof = $null

    if ($Action -eq 'Launch') {
        if ($FixtureMode) { throw 'Launch is unavailable in FixtureMode.' }
        if (Test-ValheimRunning) { throw 'Launch requires Valheim to be closed.' }
        # The ordinary request timeout is deliberately short, but world entry is not an
        # ordinary mailbox operation. ERA17 has repeatedly needed about 190 seconds to load
        # its 8.9 million ZDOs. Keep an explicit caller override, and otherwise retain the
        # installed driver's proven thirteen-minute bound so a cold resume does not report
        # failure while the pinned world is still making healthy progress.
        $launchWaitSeconds = if ($PSBoundParameters.ContainsKey('WaitSeconds')) {
            $WaitSeconds
        } else {
            780
        }
        $steamPath = [IO.Path]::GetFullPath([string]$context.steam_exe)
        if (-not (Test-Path -LiteralPath $steamPath -PathType Leaf)) {
            throw "Steam executable not found: $steamPath"
        }
        $currentSteamAccount = Get-SteamAccountContext -SteamExecutable $steamPath
        if (-not $context.steam_account -or
            [string]$currentSteamAccount.account_id -ne [string]$context.steam_account.account_id) {
            throw "Steam account changed since Prepare: expected $([string]$context.steam_account.account_id), active $([string]$currentSteamAccount.account_id)."
        }
        if ($context.world_support) {
            if (-not [string]::IsNullOrWhiteSpace([Environment]::GetEnvironmentVariable('COMFY_AUTOJOIN'))) {
                throw 'Launch refused inherited COMFY_AUTOJOIN automation.'
            }
            $nativeAutotestRequest = Join-Path $ValheimRoot 'BepInEx\config\comfy-network-sense\native-autotest-request.json'
            if (Test-Path -LiteralPath $nativeAutotestRequest -PathType Leaf) {
                throw "Launch refused an unpinned NetworkSense native-autotest request: $nativeAutotestRequest"
            }
        }
        $sessions = Get-InteractiveSessionFacts
        if (-not $sessions.current_is_interactive) {
            throw 'Launch requires this process, Explorer, and the running Steam client in the same interactive session.'
        }
        $runtimeRoot = Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-runtime'
        $requestPath = Join-Path $runtimeRoot 'requests\world-entry.json'
        if (Test-Path -LiteralPath $requestPath) {
            throw "A world-entry request is already armed: $requestPath"
        }
        $requestId = 'world-entry-' + [Guid]::NewGuid().ToString('N')
        $now = [DateTimeOffset]::UtcNow
        $request = [ordered]@{
            schema = 'comfy-quest-world-entry-request/v1'
            request_id = $requestId
            created_utc = $now.ToString('o')
            expires_utc = $now.AddMinutes(15).ToString('o')
            expected_machine = [string]$context.expected_machine
            expected_world_uid = [string]$context.world_uid
            world_name = [string]$context.world_name
            world_display_name = [string]$context.world_display_name
            character_profile = [string]$context.character_profile
            creator_session_id = $SessionId
        }
        Write-JsonAtomic $requestPath $request
        Write-JsonAtomic (Join-Path $operationRoot 'world-entry-request.json') $request
        $supportLogPath = Join-Path $ValheimRoot 'BepInEx\LogOutput.log'
        if ($context.world_support -and (Test-Path -LiteralPath $supportLogPath -PathType Leaf)) {
            Move-Item -LiteralPath $supportLogPath `
                -Destination (Join-Path $operationRoot 'pre-launch-LogOutput.log') -Force
        }
        Start-Process -FilePath $steamPath `
            -ArgumentList @('-applaunch', '892970', '-console') -WindowStyle Hidden

        $processDeadline = (Get-Date).AddSeconds(90)
        $game = $null
        while (-not $game -and (Get-Date) -lt $processDeadline) {
            Start-Sleep -Milliseconds 500
            $game = Get-Process valheim -ErrorAction SilentlyContinue | Select-Object -First 1
        }
        if (-not $game) { throw 'Valheim did not start within 90 seconds.' }
        if ($game.SessionId -ne [Diagnostics.Process]::GetCurrentProcess().SessionId) {
            throw "Valheim started in session $($game.SessionId), expected current interactive session $([Diagnostics.Process]::GetCurrentProcess().SessionId)."
        }
        $receiptPath = Join-Path $runtimeRoot "receipts\world-entry\$requestId.json"
        $statusPath = Join-Path $runtimeRoot 'status\world-entry.json'
        $deadline = (Get-Date).AddSeconds($launchWaitSeconds)
        while ((Get-Date) -lt $deadline) {
            if (-not (Get-Process -Id $game.Id -ErrorAction SilentlyContinue)) {
                throw 'Valheim exited before the pinned world-entry receipt arrived.'
            }
            if (Test-Path -LiteralPath $receiptPath -PathType Leaf) {
                try {
                    $candidate = [IO.File]::ReadAllText($receiptPath) | ConvertFrom-Json
                    if ([string]$candidate.request_id -eq $requestId -and
                        [string]$candidate.creator_session_id -eq $SessionId) {
                        if ([string]$candidate.state -eq 'rejected') {
                            throw "World entry rejected: $([string]$candidate.detail)"
                        }
                        if ([string]$candidate.state -eq 'entered') {
                            $worldEntryReceipt = $candidate
                            break
                        }
                    }
                } catch {
                    if ($_.Exception.Message -like 'World entry rejected:*') { throw }
                }
            }
            if (Test-Path -LiteralPath $statusPath -PathType Leaf) {
                try {
                    $status = [IO.File]::ReadAllText($statusPath) | ConvertFrom-Json
                    if ([string]$status.request_id -eq $requestId -and
                        [string]$status.creator_session_id -eq $SessionId -and
                        [string]$status.state -eq 'rejected') {
                        throw "World entry rejected: $([string]$status.detail)"
                    }
                } catch {
                    if ($_.Exception.Message -like 'World entry rejected:*') { throw }
                }
            }
            Start-Sleep -Milliseconds 250
        }
        if (-not $worldEntryReceipt) {
            throw "Pinned world entry did not complete within $launchWaitSeconds seconds."
        }
        if ([string]$worldEntryReceipt.machine -ne [string]$context.expected_machine -or
            [string]$worldEntryReceipt.world_uid -ne [string]$context.world_uid -or
            [string]$worldEntryReceipt.world_name -ne [string]$context.world_name -or
            [string]$worldEntryReceipt.world_display_name -ne [string]$context.world_display_name -or
            [string]$worldEntryReceipt.character_profile -ne [string]$context.character_profile) {
            throw 'World-entry receipt identity differs from the active Creator Session.'
        }
        if ($context.world_support) {
            $requiredSignals = @($context.world_support.runtime_proof.required_log_contains | ForEach-Object { [string]$_ })
            $forbiddenSignals = @($context.world_support.runtime_proof.forbidden_log_contains | ForEach-Object { [string]$_ })
            $supportDeadline = (Get-Date).AddSeconds(15)
            $supportLogText = ''
            $missingSignals = @($requiredSignals)
            while ((Get-Date) -lt $supportDeadline) {
                if (Test-Path -LiteralPath $supportLogPath -PathType Leaf) {
                    try { $supportLogText = Read-SharedText $supportLogPath } catch { $supportLogText = '' }
                }
                $missingSignals = @($requiredSignals | Where-Object { -not $supportLogText.Contains($_) })
                if ($missingSignals.Count -eq 0) { break }
                Start-Sleep -Milliseconds 250
            }
            if ($missingSignals.Count -ne 0) {
                throw "Pinned world-support plugin did not emit required current-launch proof: $($missingSignals -join '; ')"
            }
            $forbiddenFound = @($forbiddenSignals | Where-Object { $supportLogText.Contains($_) })
            if ($forbiddenFound.Count -ne 0) {
                throw "Pinned world-support plugin emitted a forbidden vanilla/error signal: $($forbiddenFound -join '; ')"
            }
            $rewrittenSupportConfigs = @($supportConfigs | Where-Object {
                    (Get-Sha256 ([string]$_.target)) -ne [string]$_.installed_sha256
                } | ForEach-Object { [string]$_.name })
            if ($rewrittenSupportConfigs.Count -ne 0) {
                throw "World-support configuration was rewritten during launch: $($rewrittenSupportConfigs -join ', ')"
            }
            $supportLogEvidence = Join-Path $operationRoot 'world-support-LogOutput.log'
            Copy-SharedFile $supportLogPath $supportLogEvidence
            $supportEvidenceText = [IO.File]::ReadAllText($supportLogEvidence)
            $evidenceMissingSignals = @($requiredSignals | Where-Object { -not $supportEvidenceText.Contains($_) })
            if ($evidenceMissingSignals.Count -ne 0) {
                throw "Captured world-support evidence omitted required proof: $($evidenceMissingSignals -join '; ')"
            }
            $evidenceForbiddenFound = @($forbiddenSignals | Where-Object { $supportEvidenceText.Contains($_) })
            if ($evidenceForbiddenFound.Count -ne 0) {
                throw "Captured world-support evidence contains a forbidden vanilla/error signal: $($evidenceForbiddenFound -join '; ')"
            }
            $supportRuntimeProof = [ordered]@{
                package_id = [string]$context.world_support.package_id
                release_id = [string]$context.world_support.release_id
                required_signals = $requiredSignals
                forbidden_signals_absent = $forbiddenSignals
                log = $supportLogEvidence
                log_sha256 = Get-Sha256 $supportLogEvidence
            }
        }
        Write-JsonAtomic (Join-Path $operationRoot 'world-entry-receipt.json') $worldEntryReceipt
        $processLifecycle = [ordered]@{
            state = 'world_entered'
            process_id = $game.Id
            process_session_id = $game.SessionId
            steam_exe = $steamPath
            launch_arguments = @('-applaunch', '892970', '-console')
            world_entry_wait_seconds = $launchWaitSeconds
            world_support_proof = $supportRuntimeProof
        }
    } elseif ($Action -eq 'Stop') {
        $processLifecycle = Stop-ValheimProcess
        $processLifecycle.quarantined_partial_saves = @()
        if (-not $processLifecycle.graceful) {
            $interruptedRoot = Join-Path $operationRoot 'interrupted-save'
            $worldRoot = if ($FixtureMode) {
                Join-Path $ValheimRoot 'worlds_local'
            } else {
                Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim\worlds_local'
            }
            $partialSources = @(
                (Join-Path $worldRoot ([string]$context.world_name + '.db.new')),
                (Join-Path $worldRoot ([string]$context.world_name + '.fwl.new')),
                ([string]$context.character_backup.source + '.new'))
            foreach ($source in $partialSources) {
                if (-not (Test-Path -LiteralPath $source -PathType Leaf)) { continue }
                $target = Join-Path $interruptedRoot (Split-Path $source -Leaf)
                if (-not (Test-ChildPath $operationRoot $target)) {
                    throw 'Interrupted-save quarantine escaped the Stop evidence directory.'
                }
                New-Item -ItemType Directory -Force -Path $interruptedRoot | Out-Null
                Move-Item -LiteralPath $source -Destination $target
                $processLifecycle.quarantined_partial_saves += [ordered]@{
                    source = [IO.Path]::GetFullPath($source)
                    quarantine = [IO.Path]::GetFullPath($target)
                    sha256 = Get-Sha256 $target
                    bytes = (Get-Item -LiteralPath $target).Length
                }
            }
        }
        $pending = Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-runtime\requests\world-entry.json'
        if (Test-Path -LiteralPath $pending -PathType Leaf) {
            Copy-Item -LiteralPath $pending -Destination (Join-Path $operationRoot 'unconsumed-world-entry-request.json') -Force
            Remove-Item -LiteralPath $pending -Force
        }
        foreach ($source in @(
                (Join-Path $ValheimRoot 'BepInEx\LogOutput.log'),
                (Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim\Player.log'),
                (Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-runtime\status\world-entry.json'))) {
            if (Test-Path -LiteralPath $source -PathType Leaf) {
                Copy-Item -LiteralPath $source -Destination (Join-Path $operationRoot (Split-Path $source -Leaf)) -Force
            }
        }
    } elseif ($Action -eq 'GalleryRebuild') {
        Invoke-ChildScript $batchScript (@('gallery_identify') + $identityArgs)
        Invoke-ChildScript $batchScript (@('gallery_rebuild', '-Profile', $Profile) + $identityArgs)
        Invoke-ChildScript $batchScript (@('gallery_evidence', '-Selector', $Profile) + $identityArgs)
    } elseif ($Action -eq 'Capture') {
        if (-not $BlueprintName) { throw 'Capture requires -BlueprintName.' }
        $captureArgs = @(
            'blueprint_capture', '-BlueprintName', $BlueprintName,
            '-RadiusMetres', $RadiusMetres.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture),
            '-Selection', $Selection)
        if ($Replace) { $captureArgs += '-Replace' }
        Invoke-ChildScript $batchScript ($captureArgs + $identityArgs)
        Invoke-ChildScript $batchScript (@(
                'blueprint_inspect', '-BlueprintName', $BlueprintName) + $identityArgs)
        $captureArtifact = Get-ChildItem -LiteralPath $operationRoot -Filter '*-capture.json' -File |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if (-not $captureArtifact) { throw 'Blueprint capture receipt did not yield its fixed artifact.' }
        Invoke-GodbuildImport $captureArtifact.FullName
        Invoke-GodbuildImport $captureArtifact.FullName -Check
        $godbuildDirectory = Join-Path $repoRoot "examples\worldbuild\$BlueprintName"
    } elseif ($Action -eq 'Replay') {
        if (-not $BlueprintName) { throw 'Replay requires -BlueprintName.' }
        $godbuildDirectory = Stage-ReviewedGodbuild $BlueprintName $ValheimRoot
        Invoke-ChildScript $batchScript (@(
                'blueprint_check', '-BlueprintName', $BlueprintName) + $identityArgs)
        Invoke-ChildScript $batchScript (@(
                'blueprint_build', '-BlueprintName', $BlueprintName,
                '-BuildMode', $BuildMode) + $identityArgs)
        Invoke-ChildScript $batchScript (@(
                'blueprint_diff', '-BlueprintName', $BlueprintName,
                '-RadiusMetres', $RadiusMetres.ToString('0.###', [Globalization.CultureInfo]::InvariantCulture),
                '-Selection', 'lab') + $identityArgs)
        $diffReceipt = Get-ChildItem -LiteralPath $operationRoot -Filter 'blueprint-diff-*-receipt.json' -File |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
        if (-not $diffReceipt -or
            -not (([IO.File]::ReadAllText($diffReceipt.FullName) | ConvertFrom-Json).detail -match ': MATCH')) {
            throw 'Blueprint replay did not produce a translation-independent zero diff.'
        }
    } elseif ($Action -in @('Arm', 'Disarm', 'BuildOn', 'BuildOff')) {
        $runtimeOperation = switch ($Action) {
            'BuildOn' { 'build_on' }
            'BuildOff' { 'build_off' }
            default { $Action.ToLowerInvariant() }
        }
        Invoke-ChildScript $runtimeScript (@($runtimeOperation) + $identityArgs)
    } elseif ($Action -eq 'Close') {
        $snapshotRoot = Join-Path $EvidenceRoot 'backup'
        $worldEntryRestorePlan = @()
        if ($Restore) {
            $runtimeRoot = Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-runtime'
            $worldEntryTargets = @(
                [pscustomobject]@{
                    Source = Join-Path $runtimeRoot 'requests\world-entry.json'
                    Label = 'world-entry request'
                },
                [pscustomobject]@{
                    Source = Join-Path $runtimeRoot 'status\world-entry.json'
                    Label = 'world-entry status'
                })
            $worldEntryRecords = @($context.world_entry_quarantine)
            $expectedWorldEntrySources = @($worldEntryTargets | ForEach-Object {
                    [IO.Path]::GetFullPath([string]$_.Source)
                })
            foreach ($record in $worldEntryRecords) {
                if ($null -eq $record -or [string]::IsNullOrWhiteSpace([string]$record.source) -or
                    $expectedWorldEntrySources -notcontains [IO.Path]::GetFullPath([string]$record.source)) {
                    throw 'World-entry snapshot restore target is outside the bounded request/status pair.'
                }
            }
            foreach ($target in $worldEntryTargets) {
                $expectedSource = [IO.Path]::GetFullPath([string]$target.Source)
                $records = @($worldEntryRecords | Where-Object {
                        [IO.Path]::GetFullPath([string]$_.source) -eq $expectedSource
                    })
                if ($records.Count -gt 1) {
                    throw "$([string]$target.Label) snapshot is ambiguous."
                }
                $record = if ($records.Count -eq 1) { $records[0] } else { $null }
                if ($null -ne $record) {
                    Assert-SnapshotFile $record $expectedSource $snapshotRoot ([string]$target.Label)
                }
                $worldEntryRestorePlan += [pscustomobject]@{
                    Record = $record
                    ExpectedSource = $expectedSource
                    Label = [string]$target.Label
                }
            }
        }
        $gameStateRestorePlan = @()
        if ($RestoreGameState) {
            $worldRecords = @($context.world_backup)
            if ($worldRecords.Count -ne 2) {
                throw 'RestoreGameState requires the exact pinned world pair.'
            }
            if ($FixtureMode) {
                foreach ($extension in @('.db', '.fwl')) {
                    $record = @($worldRecords | Where-Object {
                            [IO.Path]::GetExtension([string]$_.source) -eq $extension
                        })
                    if ($record.Count -ne 1 -or
                        -not (Test-ChildPath $ValheimRoot ([string]$record[0].source))) {
                        throw "Fixture $extension world restore target is not uniquely bounded."
                    }
                    $gameStateRestorePlan += [pscustomobject]@{
                        Record = $record[0]
                        ExpectedSource = [string]$record[0].source
                        Label = "world $extension"
                    }
                }
            } else {
                $worldRoot = Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim\worlds_local'
                foreach ($extension in @('.db', '.fwl')) {
                    $expectedWorld = Join-Path $worldRoot ([string]$context.world_name + $extension)
                    $record = @($worldRecords | Where-Object {
                            [IO.Path]::GetFullPath([string]$_.source) -eq [IO.Path]::GetFullPath($expectedWorld)
                        })
                    if ($record.Count -ne 1) {
                        throw "Pinned world $extension snapshot is missing or ambiguous."
                    }
                    $gameStateRestorePlan += [pscustomobject]@{
                        Record = $record[0]
                        ExpectedSource = $expectedWorld
                        Label = "world $extension"
                    }
                }
            }

            $characterRecord = $context.character_backup
            if ($null -eq $characterRecord) { throw 'RestoreGameState requires the pinned character snapshot.' }
            $characterSource = [IO.Path]::GetFullPath([string]$characterRecord.source)
            if ($FixtureMode) {
                if (-not (Test-ChildPath $ValheimRoot $characterSource)) {
                    throw 'Fixture character restore target escaped ValheimRoot.'
                }
            } else {
                $profileLeaf = [regex]::Escape([string]$context.character_profile)
                $localCharacters = Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim\characters'
                $steamUserdata = Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath([string]$context.steam_exe))) 'userdata'
                $validLeaf = (Split-Path $characterSource -Leaf) -match "(?i)^$profileLeaf\.fch(?:\.new)?$"
                $validLocal = Test-ChildPath $localCharacters $characterSource
                $validSteam = (Test-ChildPath $steamUserdata $characterSource) -and
                    $characterSource -match '(?i)\\892970\\remote\\characters\\'
                if (-not $validLeaf -or (-not $validLocal -and -not $validSteam)) {
                    throw 'Pinned character restore target is outside the supported Valheim save roots.'
                }
            }
            $gameStateRestorePlan += [pscustomobject]@{
                Record = $characterRecord
                ExpectedSource = $characterSource
                Label = 'character profile'
            }
            foreach ($item in $gameStateRestorePlan) {
                Assert-SnapshotFile $item.Record $item.ExpectedSource $snapshotRoot $item.Label
            }
        }
        if (Test-ValheimRunning) {
            Invoke-ChildScript $runtimeScript (@('build_off') + $identityArgs)
            Invoke-ChildScript $runtimeScript (@('disarm') + $identityArgs)
        }
        Set-PrivateWorldConfirmation ([string]$context.runtime_config.path) $false
        $context.state = 'closed'
        $context | Add-Member -NotePropertyName closed_utc `
            -NotePropertyValue ([DateTimeOffset]::UtcNow.ToString('o')) -Force
        if ($Restore) {
            foreach ($plugin in $context.plugins) {
                $target = [string]$plugin.target
                if ($plugin.existed) {
                    Copy-Item -LiteralPath ([string]$plugin.backup) -Destination $target -Force
                } elseif (Test-Path -LiteralPath $target) {
                    Remove-Item -LiteralPath $target -Force
                }
            }
            $config = $context.runtime_config
            if ($config.existed) {
                Copy-Item -LiteralPath ([string]$config.backup) -Destination ([string]$config.path) -Force
            } elseif (Test-Path -LiteralPath ([string]$config.path)) {
                Remove-Item -LiteralPath ([string]$config.path) -Force
            }
            foreach ($supportConfig in $supportConfigs) {
                $target = [string]$supportConfig.target
                if ($supportConfig.existed) {
                    Copy-Item -LiteralPath ([string]$supportConfig.backup) -Destination $target -Force
                } elseif (Test-Path -LiteralPath $target) {
                    Remove-Item -LiteralPath $target -Force
                }
            }
            foreach ($supportRequest in $supportRequests) {
                Restore-SnapshotFile $supportRequest ([string]$supportRequest.source) `
                    $snapshotRoot 'world-support request'
            }
            foreach ($item in $worldEntryRestorePlan) {
                if ($null -ne $item.Record) {
                    Restore-SnapshotFile $item.Record $item.ExpectedSource $snapshotRoot $item.Label
                } elseif (Test-Path -LiteralPath $item.ExpectedSource -PathType Leaf) {
                    Remove-Item -LiteralPath $item.ExpectedSource -Force
                }
            }
            $context | Add-Member -NotePropertyName restored -NotePropertyValue $true -Force
            $context | Add-Member -NotePropertyName restored_world_entry_state -NotePropertyValue $true -Force
        }
        if ($RestoreGameState) {
            foreach ($item in $gameStateRestorePlan) {
                Restore-SnapshotFile $item.Record $item.ExpectedSource $snapshotRoot $item.Label
            }
            $context | Add-Member -NotePropertyName restored_game_state -NotePropertyValue $true -Force
        }
        Write-JsonAtomic $contextPath $context
        Write-JsonAtomic (Join-Path $EvidenceRoot 'session.closed.json') $context
    }

    $operation = [ordered]@{
        schema = 'comfy-quest-creator-session-operation/v1'
        session_id = $SessionId
        action = $Action
        completed_utc = [DateTimeOffset]::UtcNow.ToString('o')
        machine = $context.expected_machine
        world_uid = $context.world_uid
        installed_plugins = @($context.plugins | ForEach-Object {
            [ordered]@{ name = $_.name; sha256 = Get-Sha256 ([string]$_.target) }
        })
        world_support = $context.world_support
        inbox_pins = Get-InboxPins $ValheimRoot
        godbuild_directory = $godbuildDirectory
        evidence_directory = $operationRoot
        world_entry_receipt = $worldEntryReceipt
        process_lifecycle = $processLifecycle
        restored_world_entry_state = [bool]$context.restored_world_entry_state
        restored_game_state = [bool]$context.restored_game_state
        plugin_hash_mismatches = $pluginHashMismatches
        support_config_hash_mismatches = $supportConfigHashMismatches
    }
    Write-JsonAtomic (Join-Path $operationRoot 'operation.json') $operation
    $operation | ConvertTo-Json -Depth 10
} finally {
    if ($lease) { $lease.Dispose() }
}
