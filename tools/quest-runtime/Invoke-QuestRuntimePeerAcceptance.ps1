#Requires -Version 5.1
<#
.SYNOPSIS
Prepare, guide, receipt, and clean up the OMEN-listen-host/i5-peer Quest Runtime acceptance.

.DESCRIPTION
This harness deliberately uses ordinary native Valheim multiplayer. OMEN hosts a private listen
world and i5 joins through Steam Friends. It never reads or changes AM4, Docker, Gateway, or
ComfyNetworkSense settings. Gameplay is human-driven; acceptance is receipt-driven.
#>
[CmdletBinding()]
param(
    [ValidateSet('Prepare','Run','Status','Collect','Stop')]
    [string] $Action = 'Run',
    [string] $RunId = '',
    [string] $Version = '1.5.0',
    [string] $OmenCharacter = 'Tugcorp',
    [string] $I5Character = 'durracktu',
    [ValidateRange(60,1800)][int] $HumanTimeoutSeconds = 900,
    [string] $EvidenceRoot = '',
    [switch] $PlanOnly
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) { $EvidenceRoot = Join-Path $repoRoot 'fieldlab\runs\quest-runtime-peer' }
if ([string]::IsNullOrWhiteSpace($RunId)) { $RunId = 'quest-peer-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') }
if ($RunId -notmatch '^[A-Za-z0-9._-]{1,80}$') { throw 'RunId must be a safe token.' }
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'Version must be semantic major.minor.patch.' }

$runRoot = [IO.Path]::GetFullPath((Join-Path $EvidenceRoot $RunId))
$contextPath = Join-Path $runRoot 'context.json'
$workbookPath = Join-Path $runRoot 'operator-workbook.md'
$resultPath = Join-Path $runRoot 'result.json'
$i5Deploy = Join-Path $repoRoot 'tools\i5\Deploy-ToI5.ps1'
$i5Link = Join-Path $repoRoot 'tools\i5\Test-I5Link.ps1'
$valheimRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim'
$omenPluginRoot = Join-Path $valheimRoot 'BepInEx\plugins'
$omenRuntimeRoot = Join-Path $valheimRoot 'BepInEx\config\comfy-quest-runtime'
$omenConfig = Join-Path $valheimRoot 'BepInEx\config\djcdevelopment.valheim.comfyquestruntime.cfg'
$runtimeDll = Join-Path $repoRoot 'network\mod\ComfyQuestRuntime\bin\Release\net48\ComfyQuestRuntime.dll'
$contractsDll = Join-Path $repoRoot 'network\mod\ComfyQuestContracts\bin\Release\netstandard2.0\ComfyQuestContracts.dll'
$jsonDll = Join-Path $repoRoot 'network\mod\ComfyQuestRuntime\bin\Release\net48\Newtonsoft.Json.dll'
$pack = Join-Path $repoRoot "network\mod\ComfyQuestRuntime\LiveTest\omen-inscription-proof-$Version.questpack"
$manifest = Join-Path $repoRoot 'network\mod\ComfyQuestRuntime\LiveTest\manifest.json'
$remoteEvidence = 'C:\deploy\baseline\fieldlab\runs\quest-runtime-peer'
$remoteRuntimeRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\config\comfy-quest-runtime'
$remoteConfig = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\config\djcdevelopment.valheim.comfyquestruntime.cfg'
$contentHash = if (Test-Path $manifest) { [string]((Get-Content $manifest -Raw | ConvertFrom-Json).content_hash) } else { '' }

function Write-Json([string] $Path, [object] $Value) {
    $directory = Split-Path -Parent $Path
    if ($directory) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }
    $temporary = "$Path.tmp"
    [IO.File]::WriteAllText($temporary,(($Value | ConvertTo-Json -Depth 14)+[Environment]::NewLine),[Text.UTF8Encoding]::new($false))
    Move-Item -LiteralPath $temporary -Destination $Path -Force
}

function Invoke-I5([string] $Script) {
    $encoded = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($Script))
    $output = @(& ssh -o BatchMode=yes -o ConnectTimeout=15 i5 "powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand $encoded")
    if ($LASTEXITCODE -ne 0) { throw "i5 command failed with exit $LASTEXITCODE." }
    return @($output | Where-Object { $_ -notmatch '^#< CLIXML' })
}

function Install-LocalFile([string] $Source,[string] $Destination) {
    $resolved = (Resolve-Path -LiteralPath $Source).Path
    New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Destination) | Out-Null
    $temporary = "$Destination.quest-runtime.tmp"
    Copy-Item -LiteralPath $resolved -Destination $temporary -Force
    Move-Item -LiteralPath $temporary -Destination $Destination -Force
    $sourceHash = (Get-FileHash $resolved -Algorithm SHA256).Hash.ToLowerInvariant()
    $targetHash = (Get-FileHash $Destination -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($sourceHash -ne $targetHash) { throw "Deployment hash mismatch: $Destination" }
    [ordered]@{path=$Destination;sha256=$targetHash}
}

function Set-PrivateConfirmation([string] $Path) {
    $text = if(Test-Path $Path){[IO.File]::ReadAllText($Path)}else{"[Safety]`r`nPrivateWorldConfirmed = true`r`n"}
    if($text -match '(?m)^PrivateWorldConfirmed\s*='){$text=[regex]::Replace($text,'(?m)^(PrivateWorldConfirmed\s*=\s*)\S+\s*$','${1}true')}
    else{$text += "`r`n[Safety]`r`nPrivateWorldConfirmed = true`r`n"}
    [IO.File]::WriteAllText($Path,$text,[Text.UTF8Encoding]::new($false))
}

function Get-ReceiptNames([string] $Directory) {
    if(Test-Path $Directory){ @((Get-ChildItem $Directory -Filter *.json -File).Name) }else{ @() }
}

function Get-NewLocalReceipts([object] $Context) {
    $known=@($Context.omen_receipt_baseline)
    $rows=@()
    $directory=Join-Path $omenRuntimeRoot 'receipts'
    if(Test-Path $directory){foreach($file in Get-ChildItem $directory -Filter *.json -File){if($file.Name -notin $known){try{$o=Get-Content $file.FullName -Raw|ConvertFrom-Json;$o|Add-Member receipt_file $file.Name -Force;$rows+=$o}catch{}}}}
    @($rows)
}

function Get-NewI5Receipts([object] $Context) {
    $knownJson=@($Context.i5_receipt_baseline)|ConvertTo-Json -Compress
    $script="`$known=@($knownJson);`$p='$remoteRuntimeRoot\receipts';`$rows=@();if(Test-Path `$p){foreach(`$f in Get-ChildItem `$p -Filter *.json -File){if(`$f.Name -notin `$known){try{`$o=Get-Content `$f.FullName -Raw|ConvertFrom-Json;`$o|Add-Member receipt_file `$f.Name -Force;`$rows+=`$o}catch{}}}};ConvertTo-Json -InputObject @(`$rows) -Depth 12 -Compress"
    $text=(Invoke-I5 $script)-join "`n"
    if([string]::IsNullOrWhiteSpace($text)){return @()}
    @($text|ConvertFrom-Json)
}

function Get-Plan {
    [ordered]@{
        schema='comfy-quest-peer-acceptance-plan/v2';run_id=$RunId;topology='omen_listen_host_i5_peer'
        roles=[ordered]@{omen="$OmenCharacter native private listen host";i5="$I5Character native Steam Friends peer"}
        infrastructure='AM4, Docker, Gateway, and NetworkSense configuration are out of scope and untouched'
        dependencies=@('ComfyQuestRuntime.dll','ComfyQuestContracts.dll','Newtonsoft.Json.dll')
        phases=@('prepare and hash both clients','OMEN starts private listen world','i5 joins through Steam Friends','OMEN proves authority','i5 proves peer denial','receipt collection and byte-exact restore')
        completion='positive host action receipts plus peer authority denial plus zero peer actions'
    }
}

function Prepare {
    if(Get-Process valheim -ErrorAction SilentlyContinue){throw 'OMEN Valheim is running; close it before Prepare.'}
    foreach($required in @($runtimeDll,$contractsDll,$jsonDll,$pack,$manifest,$i5Deploy,$i5Link)){if(-not(Test-Path $required -PathType Leaf)){throw "Required file missing: $required"}}
    $manifestDoc=Get-Content $manifest -Raw|ConvertFrom-Json
    if([string]$manifestDoc.version -ne $Version){throw "LiveTest manifest version is not $Version."}
    & $i5Link|Out-Host
    $remoteRunning=(Invoke-I5 "if(Get-Process valheim -ErrorAction SilentlyContinue){'true'}else{'false'}")-join ''
    if($remoteRunning.Trim() -eq 'true'){throw 'i5 Valheim is running; close it before Prepare.'}
    New-Item -ItemType Directory -Force -Path $runRoot|Out-Null
    $omenExisted=Test-Path $omenConfig
    if($omenExisted){[IO.File]::WriteAllBytes((Join-Path $runRoot 'omen-runtime-config.before'),[IO.File]::ReadAllBytes($omenConfig))}
    Set-PrivateConfirmation $omenConfig
    $deployed=@(
        (Install-LocalFile $runtimeDll (Join-Path $omenPluginRoot 'ComfyQuestRuntime.dll'))
        (Install-LocalFile $contractsDll (Join-Path $omenPluginRoot 'ComfyQuestContracts.dll'))
        (Install-LocalFile $jsonDll (Join-Path $omenPluginRoot 'Newtonsoft.Json.dll'))
        (Install-LocalFile $pack (Join-Path $omenRuntimeRoot "inbox\$(Split-Path -Leaf $pack)")))
    & $i5Deploy -Path $runtimeDll -ValheimPlugins|Out-Host
    & $i5Deploy -Path $contractsDll -ValheimPlugins|Out-Host
    & $i5Deploy -Path $jsonDll -ValheimPlugins|Out-Host
    & $i5Deploy -Path $pack -Dest 'C:/Program Files (x86)/Steam/steamapps/common/Valheim/BepInEx/config/comfy-quest-runtime/inbox'|Out-Host
    $remoteRun="$remoteEvidence\$RunId"
    $remoteSetup=@"
`$run='$remoteRun';`$cfg='$remoteConfig';New-Item -ItemType Directory -Force -Path `$run|Out-Null;`$existed=Test-Path `$cfg;if(`$existed){[IO.File]::WriteAllBytes((Join-Path `$run 'i5-runtime-config.before'),[IO.File]::ReadAllBytes(`$cfg));`$text=[IO.File]::ReadAllText(`$cfg)}else{`$text="[Safety]`r`nPrivateWorldConfirmed = true`r`n"};if(`$text -match '(?m)^PrivateWorldConfirmed\s*='){`$text=[regex]::Replace(`$text,'(?m)^(PrivateWorldConfirmed\s*=\s*)\S+\s*`$','`${1}true')}else{`$text+="`r`n[Safety]`r`nPrivateWorldConfirmed = true`r`n"};[IO.File]::WriteAllText(`$cfg,`$text,[Text.UTF8Encoding]::new(`$false));@{existed=`$existed;active=(Get-FileHash `$cfg -Algorithm SHA256).Hash.ToLowerInvariant();names=if(Test-Path '$remoteRuntimeRoot\receipts'){@(Get-ChildItem '$remoteRuntimeRoot\receipts' -Filter *.json -File|% Name)}else{@()}}|ConvertTo-Json -Compress
"@
    $i5State=((Invoke-I5 $remoteSetup)-join "`n")|ConvertFrom-Json
    $context=[ordered]@{schema='comfy-quest-peer-acceptance-context/v2';run_id=$RunId;topology='omen_listen_host_i5_peer';state='prepared';prepared_utc=[DateTimeOffset]::UtcNow.ToString('o');version=$Version;content_hash=$contentHash;omen_character=$OmenCharacter;i5_character=$I5Character;omen_config_existed=$omenExisted;omen_config_active_sha256=(Get-FileHash $omenConfig -Algorithm SHA256).Hash.ToLowerInvariant();i5_config_existed=$i5State.existed;i5_config_active_sha256=$i5State.active;local_deployment=$deployed;omen_receipt_baseline=@(Get-ReceiptNames (Join-Path $omenRuntimeRoot 'receipts'));i5_receipt_baseline=@($i5State.names|Where-Object{$_ -is [string]});evidence_window_utc=[DateTimeOffset]::UtcNow.ToString('o')}
    Write-Json $contextPath $context
    Write-Workbook
    $context|ConvertTo-Json -Depth 12
}

function Write-Workbook {
    $body=@"
# Quest Runtime native peer acceptance — $RunId

Topology: **OMEN private listen host → i5 Steam Friends peer**  
Objective: prove positive listen-host authority and fail-closed peer mutation using the identical $Version content hash.

## 1 — OMEN: HOST

1. Launch Valheim normally.
2. Select **$OmenCharacter** and the existing Quest test world.
3. Enable **Start Server**, keep it private, and use a temporary password.
4. Wait until fully rendered in-world. Do not open F9 yet.

Expected: an ordinary native listen world. AM4 is not involved.

## 2 — i5: JOIN

1. Launch Valheim normally as **$I5Character**.
2. In Steam Friends, join **wary.fool** and enter the temporary password.
3. Confirm both players are visible in the same world.

## 3 — OMEN: PROVE HOST AUTHORITY

1. Press **F9** and run **Content Update** until **$Version active** appears.
2. Place or select a host-owned sign. Aim at it and press backquote once for CHECK, then once for CAST.
3. Close F9 and edit the sign exactly once.
4. Confirm: cast message; Wood increases by exactly one; one floor appears nearby; after about five seconds that floor clears; cleanup message appears; experience reports complete.
5. Stop interacting.

## 4 — i5: PROVE PEER DENIAL

1. Place one sign owned by **$I5Character**.
2. Press **F9**, run **Content Update**, and confirm the identical **$Version active**.
3. Aim at the i5-owned sign and press backquote once.
4. Expect **NOT READY · mutation_authority_unavailable**.
5. Do not cast or edit repeatedly. Confirm no quest message, Wood reward, floor spawn, or executed action.
6. Stop interacting. The harness will collect evidence and restore both configs.

Stop condition: if either player is not in the same world, the version/hash differs, or the denial differs, stop and report exactly what is visible.
"@
    [IO.File]::WriteAllText($workbookPath,$body,[Text.UTF8Encoding]::new($false));Write-Host $body
}

function Restore-Configs([Collections.Generic.List[string]]$Errors) {
    try{$backup=Join-Path $runRoot 'omen-runtime-config.before';$ctx=if(Test-Path $contextPath){Get-Content $contextPath -Raw|ConvertFrom-Json}else{$null};if(Test-Path $backup){[IO.File]::WriteAllBytes($omenConfig,[IO.File]::ReadAllBytes($backup))}elseif($ctx -and -not $ctx.omen_config_existed -and (Test-Path $omenConfig)){Remove-Item $omenConfig -Force}}catch{$Errors.Add("omen_config_restore:$($_.Exception.Message)")}
    try{$remoteRun="$remoteEvidence\$RunId";Invoke-I5 "`$b='$remoteRun\i5-runtime-config.before';`$p='$remoteConfig';if(Test-Path `$b){[IO.File]::WriteAllBytes(`$p,[IO.File]::ReadAllBytes(`$b))}elseif(Test-Path `$p){Remove-Item `$p -Force}"|Out-Null}catch{$Errors.Add("i5_config_restore:$($_.Exception.Message)")}
}

function Stop-All([bool]$CloseGames=$true) {
    $errors=[Collections.Generic.List[string]]::new()
    if($CloseGames){try{$p=@(Get-Process valheim -ErrorAction SilentlyContinue);if($p.Count){$p|Stop-Process}}catch{$errors.Add("omen_stop:$($_.Exception.Message)")};try{Invoke-I5 "`$p=@(Get-Process valheim -ErrorAction SilentlyContinue);if(`$p.Count){`$p|Stop-Process}"|Out-Null}catch{$errors.Add("i5_stop:$($_.Exception.Message)")}}
    Restore-Configs $errors
    @($errors)
}

function Collect([bool]$WaitForEvidence=$false) {
    if(-not(Test-Path $contextPath)){throw "Run context missing: $contextPath"}
    $context=Get-Content $contextPath -Raw|ConvertFrom-Json
    $deadline=(Get-Date).AddSeconds($(if($WaitForEvidence){$HumanTimeoutSeconds}else{0}))
    do{
        $omen=@(Get-NewLocalReceipts $context);$i5=@(Get-NewI5Receipts $context)
        $omenLoad=@($omen|?{$_.operation -eq 'load' -and $_.status -eq 'activated' -and $_.version -eq $Version})
        $omenActions=@($omen|?{$_.operation -eq 'action' -and $_.status -eq 'executed'})
        $requiredActions=@('message-cast','grant-wood','raise-floor','cleanup-timer','clear-floor','message-cleared')
        $seenActions=@($omenActions|% action_id)
        $hostComplete=@($omen|?{$_.operation -eq 'transition' -and $_.status -in @('complete','completed')})
        $i5Load=@($i5|?{$_.operation -eq 'load' -and $_.status -eq 'activated' -and $_.version -eq $Version})
        $i5Denied=@($i5|?{$_.operation -eq 'charm_check' -and $_.status -eq 'rejected' -and $_.error -eq 'mutation_authority_unavailable'})
        $i5Actions=@($i5|?{$_.operation -eq 'action' -and $_.status -eq 'executed'})
        $actionsComplete=@($requiredActions|?{$_ -in $seenActions}).Count -eq $requiredActions.Count
        if($omenLoad.Count -and $actionsComplete -and $hostComplete.Count -and $i5Load.Count -and $i5Denied.Count){break}
        if(-not $WaitForEvidence -or (Get-Date)-ge $deadline){break};Start-Sleep 2
    }while($true)
    Write-Json (Join-Path $runRoot 'omen-runtime-receipts.json') @($omen);Write-Json (Join-Path $runRoot 'i5-runtime-receipts.json') @($i5)
    $cleanup=@(Stop-All -CloseGames:$false)
    $passed=$omenLoad.Count -gt 0 -and $actionsComplete -and $hostComplete.Count -gt 0 -and $i5Load.Count -gt 0 -and $i5Denied.Count -gt 0 -and $i5Actions.Count -eq 0 -and $cleanup.Count -eq 0
    $result=[ordered]@{schema='comfy-quest-peer-acceptance-result/v2';run_id=$RunId;topology='omen_listen_host_i5_peer';completed_utc=[DateTimeOffset]::UtcNow.ToString('o');verdict=if($passed){'passed'}elseif($cleanup.Count){'cleanup_incomplete'}else{'failed_and_cleaned_up'};gates=[ordered]@{host_version_activated=$omenLoad.Count -gt 0;host_required_actions=$actionsComplete;host_terminal=$hostComplete.Count -gt 0;peer_version_activated=$i5Load.Count -gt 0;peer_authority_denied=$i5Denied.Count -gt 0;peer_executed_actions=$i5Actions.Count;cleanup_errors=$cleanup};evidence=[ordered]@{workbook='operator-workbook.md';omen_receipts='omen-runtime-receipts.json';i5_receipts='i5-runtime-receipts.json';version=$Version;content_hash=$contentHash}}
    Write-Json $resultPath $result;$result|ConvertTo-Json -Depth 12
    if(-not $passed){exit 3}
}

function Run {
    if(-not(Test-Path $contextPath)){Prepare|Out-Host}
    $context=Get-Content $contextPath -Raw|ConvertFrom-Json
    $context.state='waiting_human_native_session';$context|Add-Member waiting_human_utc ([DateTimeOffset]::UtcNow.ToString('o')) -Force;Write-Json $contextPath $context
    Write-Workbook
    Collect -WaitForEvidence $true
}

if($PlanOnly){Get-Plan|ConvertTo-Json -Depth 10;exit 0}
switch($Action){
    'Prepare'{Prepare}
    'Run'{Run}
    'Collect'{Collect}
    'Stop'{@(Stop-All)|ConvertTo-Json}
    'Status'{[ordered]@{run_id=$RunId;context=if(Test-Path $contextPath){Get-Content $contextPath -Raw|ConvertFrom-Json}else{$null};result=if(Test-Path $resultPath){Get-Content $resultPath -Raw|ConvertFrom-Json}else{$null};omen_valheim=@(Get-Process valheim -ErrorAction SilentlyContinue|Select-Object Id,Responding);i5_valheim=(Invoke-I5 "Get-Process valheim -ErrorAction SilentlyContinue|Select Id,Responding|ConvertTo-Json -Compress")-join "`n"}|ConvertTo-Json -Depth 12}
}
