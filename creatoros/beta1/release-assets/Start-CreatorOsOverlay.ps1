#Requires -Version 5.1
<#
.SYNOPSIS
Render local CreatorOS Runtime receipts with the optional Discoverlay HUD.

.DESCRIPTION
This adapter reads only the local player's bounded Runtime receipts and emits the
discoverlay-spec-v1 view model beside hud.exe. It deliberately omits actor, Steam, character,
and server credentials. The overlay is an ergonomic display; Runtime and NetworkSense receipts
remain the evidence authorities.
#>
[CmdletBinding()]
param(
    [string] $ValheimDir = (Join-Path ${env:ProgramFiles(x86)} 'Steam\steamapps\common\Valheim'),
    [ValidateRange(1, 30)][int] $RefreshSeconds = 1,
    [switch] $Once,
    [switch] $NoLaunch
)

$ErrorActionPreference = 'Stop'
$overlayRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$clientRoot = Split-Path -Parent $overlayRoot
$hud = Join-Path $overlayRoot 'hud.exe'
$output = Join-Path $overlayRoot 'overlay.json'
$installPath = Join-Path $clientRoot 'install-manifest.json'
if (-not (Test-Path -LiteralPath $installPath -PathType Leaf)) {
    throw 'install-manifest.json is missing beside the optional overlay.'
}
if (-not $NoLaunch -and -not (Test-Path -LiteralPath $hud -PathType Leaf)) {
    throw 'hud.exe is missing from the optional overlay.'
}
$install = Get-Content -LiteralPath $installPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ([string]$install.schema -ne 'creatoros-beta-install-manifest/v1' -or
    [string]$install.release_id -ne 'creatoros-beta1') {
    throw 'The install manifest identity is unsupported.'
}
$receipts = Join-Path ([IO.Path]::GetFullPath($ValheimDir)) 'BepInEx\config\comfy-quest-runtime\receipts'
$utf8 = New-Object Text.UTF8Encoding($false)
$sequence = 0

function Crop([string] $Value, [int] $Maximum = 54) {
    if ([string]::IsNullOrWhiteSpace($Value)) { return '-' }
    $clean = ($Value -replace '[\r\n\t]+', ' ').Trim()
    if ($clean.Length -le $Maximum) { return $clean }
    return $clean.Substring(0, $Maximum - 1) + [char]0x2026
}

function Receipt-State($Receipt) {
    $status = [string]$Receipt.status
    if ($status -match 'rejected|failed|error') { return 'crit' }
    if ($status -match 'pending|ignored|diagnostic') { return 'warn' }
    if ($status -match 'complete|completed|activated|started|matched|accepted|inscribed') { return 'ok' }
    return 'info'
}

function Receipt-Text($Receipt) {
    $experience = switch ([string]$Receipt.experience_id) {
        'slayers-air-drop' { 'Air Drop' }
        'slayers-cold-shot' { 'Cold Shot' }
        default { Crop ([string]$Receipt.experience_id) 24 }
    }
    $operation = Crop ([string]$Receipt.operation) 20
    $status = Crop ([string]$Receipt.status) 20
    $stage = Crop ([string]$Receipt.next_stage_id) 22
    if ($stage -eq '-') { $stage = Crop ([string]$Receipt.current_stage_id) 22 }
    if ($stage -eq '-') { $stage = Crop ([string]$Receipt.stage_id) 22 }
    $count = ''
    if ($null -ne $Receipt.current_count -and $null -ne $Receipt.required_count) {
        $count = " $($Receipt.current_count)/$($Receipt.required_count)"
    }
    if ($experience -ne '-') { return "$experience - $operation/$status - $stage$count" }
    return "$operation/$status - $stage$count"
}

function Write-Overlay {
    $script:sequence++
    $rows = @(
        [ordered]@{ label = 'World'; value = [string]$install.world_name; state = 'ok' },
        [ordered]@{ label = 'Player'; value = Crop ([string]$install.player_label) 24; state = 'info' },
        [ordered]@{ label = 'Campaign'; value = ([string]$install.pack_content_hash).Substring(0, 12); state = 'info'; note = 'immutable content hash prefix' }
    )
    $latest = @()
    if (Test-Path -LiteralPath $receipts -PathType Container) {
        $latest = @(Get-ChildItem -LiteralPath $receipts -File -Filter '*.json' |
            Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 6)
    }
    if ($latest.Count -eq 0) {
        $rows += [ordered]@{ label = ''; value = 'Waiting for the first local hunt receipt'; state = 'stale' }
    } else {
        foreach ($file in @($latest | Sort-Object LastWriteTimeUtc)) {
            try {
                $receipt = Get-Content -LiteralPath $file.FullName -Raw -Encoding UTF8 | ConvertFrom-Json
                if ([string]$receipt.schema -ne 'comfy-quest-runtime-receipt/v1') { continue }
                $rows += [ordered]@{
                    label = ''
                    value = Receipt-Text $receipt
                    state = Receipt-State $receipt
                }
            } catch {
                $rows += [ordered]@{ label = ''; value = 'A local receipt is unreadable'; state = 'warn' }
            }
        }
    }
    $spec = [ordered]@{
        format = 'discoverlay-spec-v1'
        generated_at = (Get-Date).ToUniversalTime().ToString('o')
        seq = $script:sequence
        window = [ordered]@{ anchor = 'top-right'; x = 18; y = 150; opacity = 215; gap = 8 }
        panels = @([ordered]@{
            id = 'creatoros-signature-hunt'
            title = 'CreatorOS - SIGNATURE HUNT'
            layout = 'rows'
            width = 390
            rows = $rows
        })
        footer = 'Local view only - Runtime + NetworkSense remain authoritative'
        errors = @()
    }
    $temporary = $output + '.tmp-' + [Guid]::NewGuid().ToString('N')
    try {
        [IO.File]::WriteAllText($temporary, ($spec | ConvertTo-Json -Depth 10) + [Environment]::NewLine, $utf8)
        Move-Item -LiteralPath $temporary -Destination $output -Force
    } finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}

$hudProcess = $null
try {
    Write-Overlay
    if (-not $NoLaunch) {
        $hudProcess = Start-Process -FilePath $hud -WorkingDirectory $overlayRoot -PassThru
    }
    if ($Once) { Write-Output $output; return }
    while ($NoLaunch -or ($hudProcess -and -not $hudProcess.HasExited)) {
        Start-Sleep -Seconds $RefreshSeconds
        Write-Overlay
    }
} finally {
    if ($hudProcess -and -not $hudProcess.HasExited) {
        Stop-Process -Id $hudProcess.Id -ErrorAction SilentlyContinue
    }
}
