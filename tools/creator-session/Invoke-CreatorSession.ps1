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
    [ValidateSet('Prepare', 'Status', 'GalleryRebuild', 'Capture', 'Replay', 'Arm', 'Disarm', 'Close')]
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

    [ValidateRange(1, 60)]
    [int]$WaitSeconds = 60,

    [string]$EvidenceRoot,

    [switch]$Replace,
    [switch]$Restore,
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
    return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
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
            Copy-Item -LiteralPath $source -Destination $temporary -Force
            if ((Get-Sha256 $source) -ne (Get-Sha256 $temporary)) {
                throw "Godbuild staging hash mismatch: $leaf"
            }
            $staged += [pscustomobject]@{ Source = $source; Target = $target; Temporary = $temporary }
        }
        foreach ($item in $staged) {
            if (Test-Path -LiteralPath $item.Target -PathType Leaf) {
                [IO.File]::Replace($item.Temporary, $item.Target, $null)
            } else {
                [IO.File]::Move($item.Temporary, $item.Target)
            }
            if ((Get-Sha256 $item.Source) -ne (Get-Sha256 $item.Target)) {
                throw "Godbuild deployment hash mismatch: $(Split-Path $item.Target -Leaf)"
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
            session_id = $SessionId
            blueprint_name = $BlueprintName
            radius_metres = $RadiusMetres
            selection = $Selection
            build_mode = $BuildMode
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

        if (-not $FixtureMode) {
            $worldRoot = Join-Path $env:USERPROFILE 'AppData\LocalLow\IronGate\Valheim\worlds_local'
            $missingWorldFiles = @('.db', '.fwl') | ForEach-Object {
                Join-Path $worldRoot ($WorldName + $_)
            } | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) }
            if (@($missingWorldFiles).Count -ne 0) {
                throw "Canonical world pair was not found for $WorldName."
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
            Copy-Item -LiteralPath $source -Destination $target -Force
            if ((Get-Sha256 $source) -ne (Get-Sha256 $target)) {
                throw "Plugin deployment hash mismatch: $name"
            }
            $plugins += [ordered]@{
                name = $name
                target = $target
                installed_sha256 = Get-Sha256 $target
                backup = $backup
                existed = $existed
            }
        }

        $runtimeConfig = Join-Path $ValheimRoot 'BepInEx\config\djcdevelopment.valheim.comfyquestruntime.cfg'
        $configBackup = Join-Path $backupRoot 'config\djcdevelopment.valheim.comfyquestruntime.cfg'
        New-Item -ItemType Directory -Force -Path (Split-Path -Parent $configBackup) | Out-Null
        $configExisted = Test-Path -LiteralPath $runtimeConfig -PathType Leaf
        if ($configExisted) { Copy-Item -LiteralPath $runtimeConfig -Destination $configBackup -Force }
        Set-PrivateWorldConfirmation $runtimeConfig $true

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
        }

        $context = [ordered]@{
            schema = 'comfy-quest-creator-session/v1'
            state = 'active'
            session_id = $SessionId
            lane = $Lane
            expected_machine = $ExpectedMachine
            world_uid = $WorldUid
            world_name = $WorldName
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
            inbox_pins = Get-InboxPins $ValheimRoot
            rollback = "tools\creator-session\Invoke-CreatorSession.ps1 Close -SessionId $SessionId -Restore"
        }
        Write-JsonAtomic $contextPath $context
        Write-JsonAtomic (Join-Path $EvidenceRoot 'session.json') $context
        $context | ConvertTo-Json -Depth 12
        exit 0
    }

    if (-not $context -or $context.state -ne 'active') {
        throw 'No active Creator Session. Run Prepare while Valheim is closed.'
    }
    if ($SessionId -and $SessionId -ne $context.session_id) {
        throw "Session mismatch: active is $($context.session_id), requested $SessionId."
    }
    $SessionId = [string]$context.session_id
    if ($ExpectedMachine -ne [string]$context.expected_machine -or
        $WorldUid -ne [string]$context.world_uid -or
        [IO.Path]::GetFullPath([string]$context.valheim_root) -ne $ValheimRoot) {
        throw 'Creator Session identity differs from the active install manifest.'
    }
    foreach ($plugin in $context.plugins) {
        if ((Get-Sha256 ([string]$plugin.target)) -ne [string]$plugin.installed_sha256) {
            throw "Installed bytes changed during Creator Session: $($plugin.name)"
        }
    }
    if ($Action -eq 'Close' -and $Restore -and (Test-ValheimRunning)) {
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
            plugin_hashes_match = $true
            prepared_inbox = $context.inbox_pins
            current_inbox = Get-InboxPins $ValheimRoot
            runtime_status = if (Test-Path -LiteralPath (Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-runtime\status\dev-channel.json')) {
                [IO.File]::ReadAllText((Join-Path $ValheimRoot 'BepInEx\config\comfy-quest-runtime\status\dev-channel.json')) | ConvertFrom-Json
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
        '-WaitSeconds', [string]$WaitSeconds,
        '-OutputDirectory', $operationRoot)
    $godbuildDirectory = $null

    if ($Action -eq 'GalleryRebuild') {
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
    } elseif ($Action -eq 'Arm' -or $Action -eq 'Disarm') {
        $runtimeOperation = $Action.ToLowerInvariant()
        Invoke-ChildScript $runtimeScript (@($runtimeOperation) + $identityArgs)
    } elseif ($Action -eq 'Close') {
        if (Test-ValheimRunning) {
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
            $context | Add-Member -NotePropertyName restored -NotePropertyValue $true -Force
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
        inbox_pins = Get-InboxPins $ValheimRoot
        godbuild_directory = $godbuildDirectory
        evidence_directory = $operationRoot
    }
    Write-JsonAtomic (Join-Path $operationRoot 'operation.json') $operation
    $operation | ConvertTo-Json -Depth 10
} finally {
    if ($lease) { $lease.Dispose() }
}
