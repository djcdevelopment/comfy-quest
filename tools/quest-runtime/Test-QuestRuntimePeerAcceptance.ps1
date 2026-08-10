#Requires -Version 5.1
[CmdletBinding()]param()
$ErrorActionPreference='Stop'
$scriptPath=Join-Path $PSScriptRoot 'Invoke-QuestRuntimePeerAcceptance.ps1'
$source=Get-Content $scriptPath -Raw
$errors=$null;$tokens=$null
[Management.Automation.Language.Parser]::ParseFile($scriptPath,[ref]$tokens,[ref]$errors)|Out-Null
if($errors.Count){throw "Parser errors: $($errors.Message -join '; ')"}
$plan=& $scriptPath -PlanOnly|ConvertFrom-Json
if($plan.schema -ne 'comfy-quest-peer-acceptance-plan/v2'){throw 'Plan schema mismatch.'}
if($plan.topology -ne 'omen_listen_host_i5_peer'){throw 'Listen-host topology drifted.'}
if($plan.roles.omen -notmatch 'listen host' -or $plan.roles.i5 -notmatch 'Steam Friends peer'){throw 'Native player roles drifted.'}
if('Newtonsoft.Json.dll' -notin @($plan.dependencies)){throw 'Runtime JSON dependency is not packaged.'}
foreach($forbidden in @('Invoke-ValheimServerRuntimeControl','Start-Am4','Get-Am4','docker compose','am4Container','zdoJournalCutoverEnabled','lumberjacksMotionEnabled','zdoAuthoritativeConsumerEnabled')){if($source -match [regex]::Escape($forbidden)){throw "Forbidden AM4/NetworkSense coupling: $forbidden"}}
if($source -notmatch 'PrivateWorldConfirmed' -or $source -notmatch 'omen-runtime-config.before' -or $source -notmatch 'i5-runtime-config.before'){throw 'Dual Quest config restoration contract missing.'}
if($source -notmatch 'omen_receipt_baseline' -or $source -notmatch 'i5_receipt_baseline'){throw 'Dual receipt baselines missing.'}
foreach($gate in @('host_version_activated','host_required_actions','host_terminal','peer_version_activated','peer_authority_denied','peer_executed_actions')){if($source -notmatch $gate){throw "Result gate missing: $gate"}}
foreach($action in @('message-cast','grant-wood','raise-floor','cleanup-timer','clear-floor','message-cleared')){if($source -notmatch $action){throw "Required host action missing: $action"}}
if($source -notmatch 'mutation_authority_unavailable'){throw 'Peer authority diagnostic gate missing.'}
if($source -notmatch "'complete','completed'"){throw 'Terminal transition status compatibility gate missing.'}
if($source -notmatch 'Steam Friends' -or $source -notmatch 'Start Server'){throw 'Native operator workbook missing.'}
if($source -match 'active-set\.json.*Write|Write.*active-set\.json'){throw 'Harness must not activate content offline.'}
[ordered]@{schema='comfy-quest-peer-harness-test/v2';result='passed';checks=15}|ConvertTo-Json
