namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

public sealed class RuntimeRunScope {
  [JsonProperty("world_id")] public string WorldId {get;set;}
  [JsonProperty("experience_id")] public string ExperienceId {get;set;}
  [JsonProperty("binding_zdo")] public string BindingZdo {get;set;}
  [JsonProperty("participant_ids")] public List<string> ParticipantIds {get;set;}=new();
  [JsonProperty("content_hash")] public string ContentHash {get;set;}
  [JsonIgnore] public string CharacterId=>ParticipantIds?.OrderBy(x=>x,StringComparer.Ordinal).FirstOrDefault()??"0";
  [JsonIgnore] public string LegacyKey=>string.Join("|",WorldId,CharacterId,BindingZdo,ContentHash);
  [JsonIgnore] public string ScopeId=>"scope-"+RuntimeRunHash.Hex(Canonical()).Substring(0,32);
  public string Canonical(){var participants=(ParticipantIds??new()).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal);return string.Join("\n",Field(WorldId),Field(ExperienceId),Field(BindingZdo),Field(ContentHash),string.Join(",",participants.Select(Field)));}
  static string Field(string value){value=value??"";return value.Length.ToString(CultureInfo.InvariantCulture)+":"+value;}
}

public sealed class RuntimeRunRecord {
  [JsonProperty("run_id")] public string RunId {get;set;}
  [JsonProperty("scope_id")] public string ScopeId {get;set;}
  [JsonProperty("scope")] public RuntimeRunScope Scope {get;set;}
  [JsonProperty("state_key")] public string StateKey {get;set;}
  [JsonProperty("status")] public string Status {get;set;}="active";
  [JsonProperty("started_utc")] public DateTimeOffset StartedUtc {get;set;}
  [JsonProperty("ended_utc",NullValueHandling=NullValueHandling.Ignore)] public DateTimeOffset? EndedUtc {get;set;}
  [JsonProperty("outcome",NullValueHandling=NullValueHandling.Ignore)] public string Outcome {get;set;}
  [JsonProperty("predecessor_run_id",NullValueHandling=NullValueHandling.Ignore)] public string PredecessorRunId {get;set;}
  [JsonProperty("reset_id",NullValueHandling=NullValueHandling.Ignore)] public string ResetId {get;set;}
  [JsonProperty("legacy_storage")] public bool LegacyStorage {get;set;}
  [JsonProperty("reward_policy")] public string RewardPolicy {get;set;}="per_run";

  public WorkflowIdentity Identity()=>new(){WorldId=Scope.WorldId,CharacterId=Scope.CharacterId,BindingZdo=Scope.BindingZdo,ContentHash=Scope.ContentHash,ExperienceId=Scope.ExperienceId,RunId=RunId,StateKey=StateKey};
}

/// <summary>Authority for one active run per exact world/experience/binding/participant/content scope.</summary>
public sealed class RuntimeRunRegistry {
  public const int MaxRuns=512,MaxRegistryBytes=4*1024*1024;
  readonly object gate=new(); readonly string path;
  public RuntimeRunRegistry(string runtimeRoot){path=Path.Combine(Path.GetFullPath(runtimeRoot),"state","runs.json");}

  public RuntimeRunRecord Resolve(RuntimeRunScope scope,bool legacyStateExists,DateTimeOffset now){Validate(scope);lock(gate){var file=Read();if(file.Unreadable)throw new InvalidDataException("run_registry_unreadable");var found=file.Runs.LastOrDefault(x=>x.Status=="active"&&x.ScopeId==scope.ScopeId);if(found!=null)return found;if(file.Runs.Count>=MaxRuns)throw new InvalidOperationException("run_registry_limit");var legacyId="legacy-"+RuntimeRunHash.Hex(scope.LegacyKey).Substring(0,16);var value=new RuntimeRunRecord{RunId=legacyStateExists?legacyId:NewRunId(now),ScopeId=scope.ScopeId,Scope=CloneScope(scope),StateKey=legacyStateExists?scope.LegacyKey:null,StartedUtc=now,LegacyStorage=legacyStateExists};value.StateKey=value.StateKey??value.RunId;file.Runs.Add(value);Write(file);return value;}}
  public RuntimeRunRecord Active(RuntimeRunScope scope){if(scope==null)return null;lock(gate){var file=Read();return file.Unreadable?null:file.Runs.LastOrDefault(x=>x.Status=="active"&&x.ScopeId==scope.ScopeId);}}
  public RuntimeRunRecord Find(string runId){if(string.IsNullOrWhiteSpace(runId))return null;lock(gate){var file=Read();return file.Unreadable?null:file.Runs.FirstOrDefault(x=>x.RunId==runId);}}
  public IReadOnlyList<RuntimeRunRecord> List(){lock(gate){var file=Read();return file.Unreadable?Array.Empty<RuntimeRunRecord>():file.Runs.OrderByDescending(x=>x.StartedUtc).ToArray();}}
  public bool IsActive(WorkflowIdentity identity){if(identity==null)return false;lock(gate){var file=Read();if(file.Unreadable)return false;var matches=file.Runs.Where(x=>x.StateKey==identity.Key||(!string.IsNullOrWhiteSpace(identity.RunId)&&x.RunId==identity.RunId)).ToArray();return matches.Length==0&&string.IsNullOrWhiteSpace(identity.RunId)||matches.Any(x=>x.Status=="active");}}
  public RuntimeRunRecord StartSuccessor(string priorRunId,string resetId,DateTimeOffset now){lock(gate){var file=Read();if(file.Unreadable)throw new InvalidDataException("run_registry_unreadable");var prior=file.Runs.FirstOrDefault(x=>x.RunId==priorRunId);if(prior==null)throw new InvalidOperationException("run_missing");var existing=file.Runs.FirstOrDefault(x=>x.PredecessorRunId==priorRunId&&x.ResetId==resetId);if(existing!=null)return existing;if(prior.Status!="active")throw new InvalidOperationException("run_not_active");if(file.Runs.Count>=MaxRuns)throw new InvalidOperationException("run_registry_limit");prior.Status="reset";prior.EndedUtc=now;prior.ResetId=resetId;var next=new RuntimeRunRecord{RunId=NewRunId(now),ScopeId=prior.ScopeId,Scope=CloneScope(prior.Scope),StateKey=null,Status="active",StartedUtc=now,PredecessorRunId=prior.RunId,ResetId=resetId,RewardPolicy="per_run"};next.StateKey=next.RunId;file.Runs.Add(next);Write(file);return next;}}
  public void MarkOutcome(string runId,string outcome,DateTimeOffset at){if(string.IsNullOrWhiteSpace(runId)||string.IsNullOrWhiteSpace(outcome))return;lock(gate){var file=Read();if(file.Unreadable)return;var record=file.Runs.FirstOrDefault(x=>x.RunId==runId);if(record==null||record.Outcome!=null)return;record.Outcome=outcome;Write(file);}}

  static string NewRunId(DateTimeOffset at)=>"run-"+at.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
  static RuntimeRunScope CloneScope(RuntimeRunScope value)=>new(){WorldId=value.WorldId,ExperienceId=value.ExperienceId,BindingZdo=value.BindingZdo,ContentHash=value.ContentHash,ParticipantIds=(value.ParticipantIds??new()).Where(x=>!string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToList()};
  static void Validate(RuntimeRunScope scope){if(scope==null||!Safe(scope.WorldId,80)||!Safe(scope.ExperienceId,80)||!Safe(scope.BindingZdo,80)||!Safe(scope.ContentHash,128)||scope.ParticipantIds==null||scope.ParticipantIds.Count<1||scope.ParticipantIds.Count>16||scope.ParticipantIds.Any(x=>!Safe(x,80)))throw new ArgumentException("run_scope_invalid");}
  static bool Safe(string value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=max&&value.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_'||c==':'||c=='$');
  State Read(){if(!File.Exists(path))return new();try{var info=new FileInfo(path);if(info.Length<=0||info.Length>MaxRegistryBytes)return new State{Unreadable=true};var value=JsonConvert.DeserializeObject<State>(File.ReadAllText(path))??new();value.Runs??=new();var ids=new HashSet<string>(StringComparer.Ordinal);var activeScopes=new HashSet<string>(StringComparer.Ordinal);if(value.Schema!="comfy-quest-runtime-run-registry/v1"||value.Runs.Count>MaxRuns)value.Unreadable=true;foreach(var run in value.Runs){if(run==null||!Safe(run.RunId,96)||!ids.Add(run.RunId)||run.Scope==null||run.ScopeId!=run.Scope.ScopeId||string.IsNullOrWhiteSpace(run.StateKey)||run.StateKey.Length>1024||run.Status is not ("active" or "reset")||run.RewardPolicy!="per_run"||run.Status=="active"&&!activeScopes.Add(run.ScopeId)){value.Unreadable=true;break;}try{Validate(run.Scope);}catch{value.Unreadable=true;break;}}return value;}catch{return new State{Unreadable=true};}}
  void Write(State value){Directory.CreateDirectory(Path.GetDirectoryName(path));var json=JsonConvert.SerializeObject(value,Formatting.Indented);if(Encoding.UTF8.GetByteCount(json)>MaxRegistryBytes)throw new InvalidDataException("run_registry_too_large");var temp=path+".tmp";File.WriteAllText(temp,json);if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);}
  sealed class State{[JsonProperty("schema")]public string Schema{get;set;}="comfy-quest-runtime-run-registry/v1";[JsonProperty("runs")]public List<RuntimeRunRecord> Runs{get;set;}=new();[JsonIgnore]public bool Unreadable{get;set;}}
}

public sealed class RuntimeSpawnResetObservation {
  [JsonProperty("state")] public string State {get;set;}
  [JsonProperty("detail",NullValueHandling=NullValueHandling.Ignore)] public string Detail {get;set;}
  public bool Safe=>State=="owned"||State=="already_absent";
}

public interface IRuntimeSpawnResetAdapter {
  RuntimeSpawnResetObservation Inspect(SpawnedObject value);
  RuntimeSpawnResetObservation Cleanup(SpawnedObject value);
}

public sealed class RuntimeResetSnapshot {
  [JsonProperty("workflow_present")] public bool WorkflowPresent {get;set;}
  [JsonProperty("stage_id",NullValueHandling=NullValueHandling.Ignore)] public string StageId {get;set;}
  [JsonProperty("outcome",NullValueHandling=NullValueHandling.Ignore)] public string Outcome {get;set;}
  [JsonProperty("pending_transition_id",NullValueHandling=NullValueHandling.Ignore)] public string PendingTransitionId {get;set;}
  [JsonProperty("timer_ids")] public IReadOnlyList<string> TimerIds {get;set;}=Array.Empty<string>();
  [JsonProperty("action_claim_count")] public int ActionClaimCount {get;set;}
  [JsonProperty("spawned_objects")] public IReadOnlyList<RuntimeResetSpawn> SpawnedObjects {get;set;}=Array.Empty<RuntimeResetSpawn>();
  [JsonProperty("notice_count")] public int NoticeCount {get;set;}
  [JsonProperty("previous_rewards")] public string PreviousRewards {get;set;}="retained";
  [JsonProperty("successor_reward_policy")] public string SuccessorRewardPolicy {get;set;}="per_run";
}
public sealed class RuntimeResetSpawn {
  [JsonProperty("zdo_user_id")] public long UserId {get;set;}
  [JsonProperty("zdo_object_id")] public uint ObjectId {get;set;}
  [JsonProperty("action_id")] public string ActionId {get;set;}
  [JsonProperty("inspection")] public RuntimeSpawnResetObservation Inspection {get;set;}
}
public sealed class RuntimeResetPreview {
  [JsonProperty("schema")] public string Schema {get;set;}="comfy-quest-runtime-reset-preview/v1";
  [JsonProperty("preview_token")] public string PreviewToken {get;set;}
  [JsonProperty("run_id")] public string RunId {get;set;}
  [JsonProperty("scope_id")] public string ScopeId {get;set;}
  [JsonProperty("created_utc")] public DateTimeOffset CreatedUtc {get;set;}
  [JsonProperty("expires_utc")] public DateTimeOffset ExpiresUtc {get;set;}
  [JsonProperty("snapshot_hash")] public string SnapshotHash {get;set;}
  [JsonProperty("snapshot")] public RuntimeResetSnapshot Snapshot {get;set;}
}
public sealed class RuntimeResetResult {
  [JsonProperty("schema")] public string Schema {get;set;}="comfy-quest-runtime-reset-result/v1";
  [JsonProperty("reset_id")] public string ResetId {get;set;}
  [JsonProperty("preview_token")] public string PreviewToken {get;set;}
  [JsonProperty("state")] public string State {get;set;}
  [JsonProperty("detail",NullValueHandling=NullValueHandling.Ignore)] public string Detail {get;set;}
  [JsonProperty("prior_run_id")] public string PriorRunId {get;set;}
  [JsonProperty("new_run_id",NullValueHandling=NullValueHandling.Ignore)] public string NewRunId {get;set;}
  [JsonProperty("completed_utc",NullValueHandling=NullValueHandling.Ignore)] public DateTimeOffset? CompletedUtc {get;set;}
  [JsonProperty("workflow_states_scoped")] public int WorkflowStatesScoped {get;set;}
  [JsonProperty("timers_scoped")] public int TimersScoped {get;set;}
  [JsonProperty("action_claims_scoped")] public int ActionClaimsScoped {get;set;}
  [JsonProperty("spawn_rows_cleaned")] public int SpawnRowsCleaned {get;set;}
  [JsonProperty("notices_cleared")] public int NoticesCleared {get;set;}
  [JsonProperty("previous_rewards")] public string PreviousRewards {get;set;}="retained";
  [JsonProperty("successor_reward_policy")] public string SuccessorRewardPolicy {get;set;}="per_run";
}

public sealed class RuntimeRunControlRequest {
  public const string CurrentSchema="comfy-quest-runtime-run-control-request/v1";
  [JsonProperty("schema")] public string Schema {get;set;}=CurrentSchema;
  [JsonProperty("request_id")] public string RequestId {get;set;}
  [JsonProperty("operation")] public string Operation {get;set;}
  [JsonProperty("created_utc")] public string CreatedUtc {get;set;}
  [JsonProperty("expires_utc")] public string ExpiresUtc {get;set;}
  [JsonProperty("expected_machine")] public string ExpectedMachine {get;set;}
  [JsonProperty("expected_world_uid")] public string ExpectedWorldUid {get;set;}
  [JsonProperty("run_id")] public string RunId {get;set;}
  [JsonProperty("preview_token",NullValueHandling=NullValueHandling.Ignore)] public string PreviewToken {get;set;}
  /// <summary>Which experience of the activated pack to bind. Additive and nullable: the two reset
  /// operations never carry it, and select_experience is refused without it.</summary>
  [JsonProperty("experience_id",NullValueHandling=NullValueHandling.Ignore)] public string ExperienceId {get;set;}
  [JsonProperty("confirm_reset")] public bool ConfirmReset {get;set;}
}
public static class RuntimeRunControlRequestPolicy {
  /// <summary>The allowlist. select_experience addresses the activated pack rather than a run, so
  /// it is the one operation that carries no run id — and the only one that carries an experience.
  /// The operation is checked before identity now, because what counts as identity depends on it.</summary>
  public static readonly IReadOnlyList<string> Operations=new[]{"preview_reset","apply_reset","select_experience"};
  public static bool Validate(RuntimeRunControlRequest request,DateTimeOffset now,out string error){if(request==null||request.Schema!=RuntimeRunControlRequest.CurrentSchema){error="request_schema_invalid";return false;}if(!Operations.Contains(request.Operation)){error="operation_not_allowlisted";return false;}var selecting=request.Operation=="select_experience";if(!Safe(request.RequestId,80)||!Safe(request.ExpectedMachine,80)||(!selecting&&!Safe(request.RunId,96))||!long.TryParse(request.ExpectedWorldUid,NumberStyles.Integer,CultureInfo.InvariantCulture,out var world)||world==0){error="request_identity_invalid";return false;}if(selecting&&!Safe(request.ExperienceId,80)){error="experience_selection_required";return false;}if(!selecting&&!string.IsNullOrWhiteSpace(request.ExperienceId)){error="experience_selection_not_allowed";return false;}if(request.Operation=="apply_reset"&&(!request.ConfirmReset||!Safe(request.PreviewToken,80))){error="reset_confirmation_required";return false;}if(!DateTimeOffset.TryParse(request.CreatedUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var created)||!DateTimeOffset.TryParse(request.ExpiresUtc,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind,out var expires)||created<now.AddMinutes(-30)||created>now.AddMinutes(1)||expires<=now||expires<=created||expires>now.AddMinutes(10)){error="request_time_invalid";return false;}error=null;return true;}
  public static bool CanAddressReceipt(RuntimeRunControlRequest request)=>request!=null&&Safe(request.RequestId,80);
  static bool Safe(string value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=max&&value.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_'||c=='.');
}
public sealed class RuntimeRunControlReceipt {
  [JsonProperty("schema")] public string Schema {get;set;}="comfy-quest-runtime-run-control-receipt/v1";
  [JsonProperty("request_id")] public string RequestId {get;set;}
  [JsonProperty("operation")] public string Operation {get;set;}
  [JsonProperty("state")] public string State {get;set;}
  [JsonProperty("detail",NullValueHandling=NullValueHandling.Ignore)] public string Detail {get;set;}
  [JsonProperty("machine")] public string Machine {get;set;}
  [JsonProperty("world_uid")] public string WorldUid {get;set;}
  [JsonProperty("completed_utc")] public DateTimeOffset CompletedUtc {get;set;}
  [JsonProperty("preview",NullValueHandling=NullValueHandling.Ignore)] public RuntimeResetPreview Preview {get;set;}
  [JsonProperty("result",NullValueHandling=NullValueHandling.Ignore)] public RuntimeResetResult Result {get;set;}
}

public sealed class RuntimeRunStatusEntry {
  [JsonProperty("run_id")] public string RunId {get;set;}
  [JsonProperty("scope_id")] public string ScopeId {get;set;}
  [JsonProperty("experience_id")] public string ExperienceId {get;set;}
  [JsonProperty("binding_zdo")] public string BindingZdo {get;set;}
  [JsonProperty("participant_ids")] public IReadOnlyList<string> ParticipantIds {get;set;}=Array.Empty<string>();
  [JsonProperty("content_hash")] public string ContentHash {get;set;}
  [JsonProperty("stage_id",NullValueHandling=NullValueHandling.Ignore)] public string StageId {get;set;}
  [JsonProperty("outcome",NullValueHandling=NullValueHandling.Ignore)] public string Outcome {get;set;}
  [JsonProperty("reward_policy")] public string RewardPolicy {get;set;}="per_run";
}
public sealed class RuntimeRunStatusDocument {
  [JsonProperty("schema")] public string Schema {get;set;}="comfy-quest-runtime-run-status/v1";
  [JsonProperty("observed_utc")] public DateTimeOffset ObservedUtc {get;set;}
  [JsonProperty("machine")] public string Machine {get;set;}
  [JsonProperty("world_uid")] public string WorldUid {get;set;}
  [JsonProperty("runs")] public IReadOnlyList<RuntimeRunStatusEntry> Runs {get;set;}=Array.Empty<RuntimeRunStatusEntry>();
}
public sealed class RuntimeRunStatusStore {
  public const int MaxStatusBytes=256*1024; readonly object gate=new();readonly string path;
  public RuntimeRunStatusStore(string runtimeRoot){path=Path.Combine(Path.GetFullPath(runtimeRoot),"status","runs.json");}
  public void Write(RuntimeRunStatusDocument value){if(!Valid(value))throw new InvalidDataException("run_status_invalid");lock(gate){Directory.CreateDirectory(Path.GetDirectoryName(path));var json=JsonConvert.SerializeObject(value,Formatting.Indented);if(Encoding.UTF8.GetByteCount(json)>MaxStatusBytes)throw new InvalidDataException("run_status_too_large");var temp=path+".tmp";File.WriteAllText(temp,json);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}}
  public RuntimeRunStatusDocument Read(){try{var info=new FileInfo(path);if(!info.Exists||info.Length<=0||info.Length>MaxStatusBytes)return null;var value=JsonConvert.DeserializeObject<RuntimeRunStatusDocument>(File.ReadAllText(path));return Valid(value)?value:null;}catch{return null;}}
  static bool Valid(RuntimeRunStatusDocument value){if(value==null||value.Schema!="comfy-quest-runtime-run-status/v1"||string.IsNullOrWhiteSpace(value.Machine)||value.Machine.Length>80||value.Runs==null||value.Runs.Count>64)return false;if(!string.IsNullOrWhiteSpace(value.WorldUid)&&(!long.TryParse(value.WorldUid,NumberStyles.Integer,CultureInfo.InvariantCulture,out var world)||world==0))return false;var ids=new HashSet<string>(StringComparer.Ordinal);return value.Runs.All(run=>run!=null&&Safe(run.RunId,96)&&ids.Add(run.RunId)&&Safe(run.ScopeId,96)&&Safe(run.ExperienceId,80)&&Safe(run.BindingZdo,80)&&Safe(run.ContentHash,128)&&run.RewardPolicy=="per_run"&&run.ParticipantIds!=null&&run.ParticipantIds.Count>0&&run.ParticipantIds.Count<=16&&run.ParticipantIds.All(id=>Safe(id,80)));}
  static bool Safe(string value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=max&&value.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_'||c==':'||c=='$'||c=='.');
}

/// <summary>Previewed, resumable reset across the four durable workflow stores.</summary>
public sealed class RuntimeRunCoordinator {
  readonly string root; readonly RuntimeRunRegistry registry; readonly WorkflowStateStore workflows; readonly DurableTimerStore timers; readonly ActionExecutionLedger actions; readonly SpawnExecutionStore spawned;
  public RuntimeRunCoordinator(string runtimeRoot){root=Path.GetFullPath(runtimeRoot);registry=new(root);workflows=new(root);timers=new(root);actions=new(root);spawned=new(root);}
  public RuntimeRunRegistry Registry=>registry;
  public RuntimeRunRecord Resolve(RuntimeRunScope scope,DateTimeOffset now){var active=registry.Active(scope);if(active!=null)return active;if(!workflows.TryGetByKey(scope.LegacyKey,out var workflow))throw new InvalidDataException("workflow_state_unreadable");if(!timers.TryForOwner(scope.LegacyKey,out var timerRows))throw new InvalidDataException("timer_state_unreadable");if(!actions.TryForOwner(scope.LegacyKey,out var claims))throw new InvalidDataException("action_state_unreadable");if(!spawned.TryForOwner(scope.LegacyKey,out var spawnRows))throw new InvalidDataException("spawn_state_unreadable");var legacy=workflow!=null||timerRows.Count>0||claims.Count>0||spawnRows.Count>0;return registry.Resolve(scope,legacy,now);}

  public RuntimeResetPreview Preview(string runId,DateTimeOffset now,IRuntimeSpawnResetAdapter adapter,int noticeCount=0){var run=registry.Find(runId);if(run==null)throw new InvalidOperationException("run_missing");if(run.Status!="active")throw new InvalidOperationException("run_not_active");var snapshot=Snapshot(run,adapter,noticeCount);if(snapshot.SpawnedObjects.Any(x=>x.Inspection==null||!x.Inspection.Safe))throw new InvalidOperationException("spawn_cleanup_ambiguous");var hash=RuntimeRunHash.Hex(JsonConvert.SerializeObject(snapshot,Formatting.None));var token="rstp-"+now.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,12);var preview=new RuntimeResetPreview{PreviewToken=token,RunId=run.RunId,ScopeId=run.ScopeId,CreatedUtc=now,ExpiresUtc=now.AddMinutes(5),SnapshotHash=hash,Snapshot=snapshot};var directory=Path.Combine(root,"state","reset-previews");PruneExpiredPreviews(directory,now,63);WriteBounded(Path.Combine(directory,token+".json"),preview);return preview;}

  public RuntimeResetResult Apply(string runId,string previewToken,DateTimeOffset now,IRuntimeSpawnResetAdapter adapter,Action clearNotices=null){if(!SafeToken(previewToken))throw new InvalidOperationException("reset_preview_token_invalid");var transactionPath=Path.Combine(root,"state","reset-transactions",previewToken+".json");var transaction=Read<ResetTransaction>(transactionPath);if(File.Exists(transactionPath)&&transaction==null)throw new InvalidDataException("reset_transaction_unreadable");var previewPath=Path.Combine(root,"state","reset-previews",previewToken+".json");var preview=Read<RuntimeResetPreview>(previewPath);if(!ValidPreview(preview,previewToken,runId))throw new InvalidOperationException("reset_preview_missing");var run=registry.Find(runId);if(run==null||run.ScopeId!=preview.ScopeId)throw new InvalidOperationException("reset_scope_changed");if(transaction!=null&&!ValidTransaction(transaction,preview,run))throw new InvalidDataException("reset_transaction_scope_invalid");if(transaction?.Result!=null)return transaction.Result;if(transaction==null){if(preview.ExpiresUtc<=now)throw new InvalidOperationException("reset_preview_expired");if(run.Status!="active")throw new InvalidOperationException("reset_scope_changed");var current=Snapshot(run,adapter,preview.Snapshot.NoticeCount);var currentHash=RuntimeRunHash.Hex(JsonConvert.SerializeObject(current,Formatting.None));if(currentHash!=preview.SnapshotHash)throw new InvalidOperationException("reset_preview_stale");transaction=new ResetTransaction{ResetId="reset-"+Guid.NewGuid().ToString("N"),Preview=preview,Pending=spawned.ForOwner(run.StateKey).ToList(),Cleaned=new()};WriteBounded(transactionPath,transaction);}
    foreach(var row in transaction.Pending.ToArray()){var result=adapter.Cleanup(row);if(result==null||!result.Safe){transaction.LastError=result?.Detail??result?.State??"spawn_cleanup_failed";WriteBounded(transactionPath,transaction);return Failed(transaction,preview,transaction.LastError);}spawned.Remove(new[]{row});transaction.Pending.RemoveAll(x=>x.UserId==row.UserId&&x.ObjectId==row.ObjectId);transaction.Cleaned.Add(row);WriteBounded(transactionPath,transaction);}
    RuntimeRunRecord successor;try{successor=registry.StartSuccessor(runId,transaction.ResetId,now);}catch(Exception e){transaction.LastError=e.Message;WriteBounded(transactionPath,transaction);return Failed(transaction,preview,e.Message);}var noticesCleared=0;try{clearNotices?.Invoke();noticesCleared=clearNotices==null?0:preview.Snapshot.NoticeCount;}catch{}transaction.Result=new RuntimeResetResult{ResetId=transaction.ResetId,PreviewToken=previewToken,State="completed",PriorRunId=runId,NewRunId=successor.RunId,CompletedUtc=now,WorkflowStatesScoped=preview.Snapshot.WorkflowPresent?1:0,TimersScoped=preview.Snapshot.TimerIds.Count,ActionClaimsScoped=preview.Snapshot.ActionClaimCount,SpawnRowsCleaned=transaction.Cleaned.Count,NoticesCleared=noticesCleared};WriteBounded(transactionPath,transaction);PruneCompletedTransactions(Path.GetDirectoryName(transactionPath),Path.GetDirectoryName(previewPath),64);return transaction.Result;}

  RuntimeResetSnapshot Snapshot(RuntimeRunRecord run,IRuntimeSpawnResetAdapter adapter,int noticeCount){if(!workflows.TryGetByKey(run.StateKey,out var workflow))throw new InvalidDataException("workflow_state_unreadable");if(!timers.TryForOwner(run.StateKey,out var timerRows))throw new InvalidDataException("timer_state_unreadable");if(!actions.TryForOwner(run.StateKey,out var claims))throw new InvalidDataException("action_state_unreadable");if(!spawned.TryForOwner(run.StateKey,out var spawnRows))throw new InvalidDataException("spawn_state_unreadable");return new RuntimeResetSnapshot{WorkflowPresent=workflow!=null,StageId=workflow?.StageId,Outcome=workflow?.Outcome,PendingTransitionId=workflow?.PendingTransitionId,TimerIds=timerRows.Select(x=>x.TimerId).OrderBy(x=>x,StringComparer.Ordinal).ToArray(),ActionClaimCount=claims.Count,SpawnedObjects=spawnRows.OrderBy(x=>x.UserId).ThenBy(x=>x.ObjectId).Select(x=>new RuntimeResetSpawn{UserId=x.UserId,ObjectId=x.ObjectId,ActionId=x.ActionId,Inspection=adapter?.Inspect(x)??new RuntimeSpawnResetObservation{State="unavailable",Detail="spawn_adapter_unavailable"}}).ToArray(),NoticeCount=Math.Max(0,noticeCount)};}
  bool ValidTransaction(ResetTransaction value,RuntimeResetPreview preview,RuntimeRunRecord run){if(value==null||value.Schema!="comfy-quest-runtime-reset-transaction/v1"||!SafeToken(value.ResetId)||value.Preview==null||value.Preview.PreviewToken!=preview.PreviewToken||value.Preview.RunId!=preview.RunId||value.Preview.ScopeId!=preview.ScopeId||value.Preview.SnapshotHash!=preview.SnapshotHash||value.Pending==null||value.Cleaned==null||value.Pending.Count+value.Cleaned.Count>4096)return false;var ids=new HashSet<string>(StringComparer.Ordinal);if(value.Pending.Concat(value.Cleaned).Any(row=>!Owned(row,run)||!ids.Add(row.UserId+":"+row.ObjectId)))return false;if(value.Result==null)return true;var successor=registry.Find(value.Result.NewRunId);return value.Result.Schema=="comfy-quest-runtime-reset-result/v1"&&value.Result.State=="completed"&&value.Result.ResetId==value.ResetId&&value.Result.PreviewToken==preview.PreviewToken&&value.Result.PriorRunId==run.RunId&&successor!=null&&successor.PredecessorRunId==run.RunId&&successor.ResetId==value.ResetId;}
  static bool Owned(SpawnedObject row,RuntimeRunRecord run)=>row!=null&&!string.IsNullOrWhiteSpace(row.ActionKey)&&row.ActionKey.StartsWith(run.StateKey+"|",StringComparison.Ordinal)&&row.ContentHash==run.Scope.ContentHash&&(string.IsNullOrWhiteSpace(row.RunId)||row.RunId==run.RunId)&&(string.IsNullOrWhiteSpace(row.WorldId)||row.WorldId==run.Scope.WorldId)&&(string.IsNullOrWhiteSpace(row.ExperienceId)||row.ExperienceId==run.Scope.ExperienceId);
  static bool ValidPreview(RuntimeResetPreview value,string token,string runId)=>value!=null&&value.Schema=="comfy-quest-runtime-reset-preview/v1"&&value.PreviewToken==token&&value.RunId==runId&&!string.IsNullOrWhiteSpace(value.ScopeId)&&ValidSnapshot(value.Snapshot);
  static bool ValidSnapshot(RuntimeResetSnapshot value)=>value!=null&&value.TimerIds!=null&&value.TimerIds.Count<=4096&&value.TimerIds.All(x=>!string.IsNullOrWhiteSpace(x))&&value.ActionClaimCount>=0&&value.ActionClaimCount<=32768&&value.SpawnedObjects!=null&&value.SpawnedObjects.Count<=4096&&value.SpawnedObjects.All(x=>x!=null&&x.Inspection!=null&&x.Inspection.Safe)&&value.NoticeCount>=0&&value.PreviousRewards=="retained"&&value.SuccessorRewardPolicy=="per_run";
  static RuntimeResetResult Failed(ResetTransaction transaction,RuntimeResetPreview preview,string detail)=>new(){ResetId=transaction.ResetId,PreviewToken=preview.PreviewToken,State="cleanup_incomplete",Detail=detail,PriorRunId=preview.RunId,WorkflowStatesScoped=preview.Snapshot.WorkflowPresent?1:0,TimersScoped=preview.Snapshot.TimerIds.Count,ActionClaimsScoped=preview.Snapshot.ActionClaimCount,SpawnRowsCleaned=transaction.Cleaned.Count,NoticesCleared=0};
  static bool SafeToken(string value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=80&&value.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_'||c=='.');
  static void PruneExpiredPreviews(string directory,DateTimeOffset now,int keep){if(!Directory.Exists(directory))return;var retained=new List<Tuple<string,DateTimeOffset>>();foreach(var path in Directory.GetFiles(directory,"*.json")){var preview=ReadStatic<RuntimeResetPreview>(path);if(preview==null)continue;if(preview.ExpiresUtc<=now){TryDelete(path);continue;}retained.Add(Tuple.Create(path,preview.CreatedUtc));}foreach(var value in retained.OrderByDescending(x=>x.Item2).Skip(Math.Max(0,keep)))TryDelete(value.Item1);}
  static void PruneCompletedTransactions(string transactionDirectory,string previewDirectory,int keep){if(!Directory.Exists(transactionDirectory))return;var completed=new List<Tuple<string,string,DateTimeOffset>>();foreach(var path in Directory.GetFiles(transactionDirectory,"*.json")){var value=ReadStatic<ResetTransaction>(path);if(value?.Result?.CompletedUtc!=null)completed.Add(Tuple.Create(path,value.Preview?.PreviewToken,value.Result.CompletedUtc.Value));}foreach(var value in completed.OrderByDescending(x=>x.Item3).Skip(Math.Max(0,keep))){TryDelete(value.Item1);if(SafeToken(value.Item2))TryDelete(Path.Combine(previewDirectory,value.Item2+".json"));}}
  static T ReadStatic<T>(string path)where T:class{try{var info=new FileInfo(path);return info.Exists&&info.Length>0&&info.Length<=1024*1024?JsonConvert.DeserializeObject<T>(File.ReadAllText(path)):null;}catch{return null;}}
  static void TryDelete(string path){try{if(!string.IsNullOrWhiteSpace(path)&&File.Exists(path))File.Delete(path);}catch{}}
  T Read<T>(string path)where T:class{try{var info=new FileInfo(path);return info.Exists&&info.Length>0&&info.Length<=1024*1024?JsonConvert.DeserializeObject<T>(File.ReadAllText(path)):null;}catch{return null;}}
  static void WriteBounded<T>(string path,T value){var json=JsonConvert.SerializeObject(value,Formatting.Indented);if(Encoding.UTF8.GetByteCount(json)>1024*1024)throw new InvalidDataException("run_control_document_too_large");Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+".tmp";File.WriteAllText(temp,json);if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);}
  sealed class ResetTransaction{[JsonProperty("schema")]public string Schema{get;set;}="comfy-quest-runtime-reset-transaction/v1";[JsonProperty("reset_id")]public string ResetId{get;set;}[JsonProperty("preview")]public RuntimeResetPreview Preview{get;set;}[JsonProperty("pending")]public List<SpawnedObject> Pending{get;set;}=new();[JsonProperty("cleaned")]public List<SpawnedObject> Cleaned{get;set;}=new();[JsonProperty("last_error",NullValueHandling=NullValueHandling.Ignore)]public string LastError{get;set;}[JsonProperty("result",NullValueHandling=NullValueHandling.Ignore)]public RuntimeResetResult Result{get;set;}}
}

internal static class RuntimeRunHash {
  public static string Hex(string value){using(var hash=SHA256.Create()){var bytes=hash.ComputeHash(Encoding.UTF8.GetBytes(value??""));return BitConverter.ToString(bytes).Replace("-","").ToLowerInvariant();}}
}
