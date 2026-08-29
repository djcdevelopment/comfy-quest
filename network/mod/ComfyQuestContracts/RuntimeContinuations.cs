namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

/// <summary>Selection policy for an authored in-pack continuation. Eligibility is deliberately
/// narrower than ordinary history lookup: world, durable binding, participants, and content must
/// all describe the exact campaign scope that just completed.</summary>
public static class ExperienceContinuationSelector {
  public static string FirstEligible(ExperienceDocument source,
      IReadOnlyDictionary<string,ExperienceDocument> documents,
      IEnumerable<RuntimeRunRecord> runs,RuntimeRunScope completedScope) {
    if(source==null||documents==null||completedScope==null)return null;
    var history=(runs??Enumerable.Empty<RuntimeRunRecord>()).Where(value=>value?.Scope!=null).ToArray();
    foreach(var successorId in source.SuccessorExperienceIds??new List<string>()) {
      if(!documents.TryGetValue(successorId,out var successor)||successor==null)
        throw new InvalidDataException("continuation_successor_missing:"+successorId);
      if(history.Any(value=>value.Outcome=="complete"&&SameScope(value.Scope,completedScope,successorId)))
        continue;
      var eligible=(successor.Prerequisites??new List<string>()).All(prerequisite=>
        history.Any(value=>value.Outcome=="complete"&&value.Scope.ExperienceId==prerequisite
          &&SameScope(value.Scope,completedScope,prerequisite)));
      if(eligible)return successorId;
    }
    return null;
  }

  public static bool SameScope(RuntimeRunScope candidate,RuntimeRunScope campaign,string experienceId) {
    if(candidate==null||campaign==null||candidate.ExperienceId!=experienceId
        ||candidate.WorldId!=campaign.WorldId
        ||!string.Equals(candidate.ContentHash,campaign.ContentHash,StringComparison.OrdinalIgnoreCase))return false;
    var sameBinding=!string.IsNullOrWhiteSpace(campaign.BindingInstanceId)
      ?candidate.BindingInstanceId==campaign.BindingInstanceId
      :string.IsNullOrWhiteSpace(candidate.BindingInstanceId)&&candidate.BindingZdo==campaign.BindingZdo;
    if(!sameBinding)return false;
    return Participants(candidate).SequenceEqual(Participants(campaign),StringComparer.Ordinal);
  }

  static IEnumerable<string> Participants(RuntimeRunScope scope)=>(scope.ParticipantIds??new List<string>())
    .Where(value=>!string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).OrderBy(value=>value,StringComparer.Ordinal);
}

/// <summary>One durable, idempotent decision made at a successful terminal outcome. Once written,
/// retries may repair the same handoff but may never select a later authored successor.</summary>
public sealed class RuntimeContinuationRecord {
  public const string CurrentSchema="comfy-quest-runtime-continuation/v1";
  [JsonProperty("schema")] public string Schema {get;set;}=CurrentSchema;
  [JsonProperty("handoff_id")] public string HandoffId {get;set;}
  [JsonProperty("state")] public string State {get;set;}="pending";
  [JsonProperty("created_utc")] public DateTimeOffset CreatedUtc {get;set;}
  [JsonProperty("completed_utc",NullValueHandling=NullValueHandling.Ignore)] public DateTimeOffset? CompletedUtc {get;set;}
  [JsonProperty("pack_id")] public string PackId {get;set;}
  [JsonProperty("version")] public string Version {get;set;}
  [JsonProperty("content_hash")] public string ContentHash {get;set;}
  [JsonProperty("activation_id",NullValueHandling=NullValueHandling.Ignore)] public string ActivationId {get;set;}
  [JsonProperty("source_experience_id")] public string SourceExperienceId {get;set;}
  [JsonProperty("source_run_id")] public string SourceRunId {get;set;}
  [JsonProperty("successor_experience_id")] public string SuccessorExperienceId {get;set;}
  [JsonProperty("successor_run_id",NullValueHandling=NullValueHandling.Ignore)] public string SuccessorRunId {get;set;}
  [JsonProperty("world_id")] public string WorldId {get;set;}
  [JsonProperty("binding_zdo")] public string BindingZdo {get;set;}
  [JsonProperty("binding_instance_id",NullValueHandling=NullValueHandling.Ignore)] public string BindingInstanceId {get;set;}
  [JsonProperty("participant_ids")] public List<string> ParticipantIds {get;set;}=new();
  [JsonProperty("binding_change_id",NullValueHandling=NullValueHandling.Ignore)] public string BindingChangeId {get;set;}
  [JsonProperty("last_error",NullValueHandling=NullValueHandling.Ignore)] public string LastError {get;set;}
}

/// <summary>Bounded install-local continuation journal. The source run is the idempotency key.</summary>
public sealed class RuntimeContinuationStore {
  public const int MaxRecords=512;
  public const int MaxFileBytes=4*1024*1024;
  readonly object gate=new(); readonly string path;

  public RuntimeContinuationStore(string runtimeRoot){path=Path.Combine(Path.GetFullPath(runtimeRoot??throw new ArgumentNullException(nameof(runtimeRoot))),"state","continuations.json");}

  public RuntimeContinuationRecord Begin(RuntimeRunRecord source,string successorExperienceId,
      string packId,string version,string contentHash,string activationId,DateTimeOffset now) {
    if(source?.Scope==null||source.Outcome!="complete"||!Safe(successorExperienceId,80)
        ||!Safe(packId,80)||!Safe(version,40)||!Safe(contentHash,128)||!OptionalSafe(activationId,80))
      throw new ArgumentException("continuation_scope_invalid");
    lock(gate) {
      var file=Read();if(file.Unreadable)throw new InvalidDataException("continuation_store_unreadable");
      var existing=file.Records.FirstOrDefault(value=>value.SourceRunId==source.RunId);
      if(existing!=null) {
        if(existing.SuccessorExperienceId!=successorExperienceId||existing.PackId!=packId
            ||existing.Version!=version||!string.Equals(existing.ContentHash,contentHash,StringComparison.OrdinalIgnoreCase)
            ||existing.ActivationId!=activationId||existing.SourceExperienceId!=source.Scope.ExperienceId
            ||existing.WorldId!=source.Scope.WorldId||existing.BindingInstanceId!=source.Scope.BindingInstanceId
            ||string.IsNullOrWhiteSpace(source.Scope.BindingInstanceId)&&existing.BindingZdo!=source.Scope.BindingZdo
            ||!Participants(existing.ParticipantIds).SequenceEqual(Participants(source.Scope.ParticipantIds),StringComparer.Ordinal))
          throw new InvalidOperationException("continuation_decision_conflict");
        return existing;
      }
      if(file.Records.Count>=MaxRecords)throw new InvalidOperationException("continuation_store_limit");
      var record=new RuntimeContinuationRecord {
        HandoffId="handoff-"+now.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'")+"-"+Guid.NewGuid().ToString("N").Substring(0,8),
        CreatedUtc=now,PackId=packId,Version=version,ContentHash=contentHash,ActivationId=activationId,
        SourceExperienceId=source.Scope.ExperienceId,SourceRunId=source.RunId,
        SuccessorExperienceId=successorExperienceId,WorldId=source.Scope.WorldId,
        BindingZdo=source.Scope.BindingZdo,BindingInstanceId=source.Scope.BindingInstanceId,
        ParticipantIds=Participants(source.Scope.ParticipantIds).ToList(),
      };
      Validate(record);file.Records.Add(record);Write(file);return record;
    }
  }

  public IReadOnlyList<RuntimeContinuationRecord> Pending(){lock(gate){var file=Read();if(file.Unreadable)throw new InvalidDataException("continuation_store_unreadable");return file.Records.Where(value=>value.State=="pending").OrderBy(value=>value.CreatedUtc).ThenBy(value=>value.HandoffId,StringComparer.Ordinal).ToArray();}}
  public RuntimeContinuationRecord Find(string handoffId){lock(gate){var file=Read();return file.Unreadable?null:file.Records.FirstOrDefault(value=>value.HandoffId==handoffId);}}
  public RuntimeContinuationRecord FindBySourceRun(string sourceRunId){lock(gate){var file=Read();return file.Unreadable?null:file.Records.FirstOrDefault(value=>value.SourceRunId==sourceRunId);}}
  public void SetBindingChange(string handoffId,string changeId)=>Update(handoffId,value=>{if(!string.IsNullOrWhiteSpace(value.BindingChangeId)&&value.BindingChangeId!=changeId)throw new InvalidOperationException("continuation_binding_change_conflict");if(value.BindingChangeId==changeId)return false;value.BindingChangeId=changeId;return true;});
  public void SetSuccessorRun(string handoffId,string runId)=>Update(handoffId,value=>{if(!string.IsNullOrWhiteSpace(value.SuccessorRunId)&&value.SuccessorRunId!=runId)throw new InvalidOperationException("continuation_successor_run_conflict");if(value.SuccessorRunId==runId)return false;value.SuccessorRunId=runId;return true;});
  public bool RecordError(string handoffId,string error)=>Update(handoffId,value=>{var bounded=Bound(error,240);if(string.Equals(value.LastError,bounded,StringComparison.Ordinal))return false;value.LastError=bounded;return true;});
  public void Complete(string handoffId,DateTimeOffset now)=>Update(handoffId,value=>{if(value.State=="completed")return false;if(string.IsNullOrWhiteSpace(value.SuccessorRunId))throw new InvalidOperationException("continuation_successor_run_missing");value.State="completed";value.CompletedUtc=now;value.LastError=null;return true;});

  bool Update(string handoffId,Func<RuntimeContinuationRecord,bool> change){if(!Safe(handoffId,96))throw new ArgumentException("continuation_id_invalid");lock(gate){var file=Read();if(file.Unreadable)throw new InvalidDataException("continuation_store_unreadable");var record=file.Records.FirstOrDefault(value=>value.HandoffId==handoffId)??throw new InvalidOperationException("continuation_missing");if(!change(record))return false;Validate(record);Write(file);return true;}}
  State Read(){if(!File.Exists(path))return new();try{var info=new FileInfo(path);if(info.Length<=0||info.Length>MaxFileBytes)return new State{Unreadable=true};var value=JsonConvert.DeserializeObject<State>(File.ReadAllText(path))??new();value.Records??=new();var ids=new HashSet<string>(StringComparer.Ordinal);var sources=new HashSet<string>(StringComparer.Ordinal);if(value.Schema!="comfy-quest-runtime-continuations/v1"||value.Records.Count>MaxRecords)return new State{Unreadable=true};foreach(var record in value.Records){Validate(record);if(!ids.Add(record.HandoffId)||!sources.Add(record.SourceRunId))return new State{Unreadable=true};}return value;}catch{return new State{Unreadable=true};}}
  void Write(State value){Directory.CreateDirectory(Path.GetDirectoryName(path));var json=JsonConvert.SerializeObject(value,Formatting.Indented);if(Encoding.UTF8.GetByteCount(json)>MaxFileBytes)throw new InvalidDataException("continuation_store_too_large");var temp=path+".tmp";File.WriteAllText(temp,json);if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);}
  static void Validate(RuntimeContinuationRecord value){if(value==null||value.Schema!=RuntimeContinuationRecord.CurrentSchema||!Safe(value.HandoffId,96)||value.State is not ("pending" or "completed")||value.CreatedUtc==default||value.State=="completed"&&(!value.CompletedUtc.HasValue||value.CompletedUtc.Value<value.CreatedUtc||string.IsNullOrWhiteSpace(value.SuccessorRunId))||!Safe(value.PackId,80)||!Safe(value.Version,40)||!Safe(value.ContentHash,128)||!OptionalSafe(value.ActivationId,80)||!Safe(value.SourceExperienceId,80)||!Safe(value.SourceRunId,96)||!Safe(value.SuccessorExperienceId,80)||!OptionalSafe(value.SuccessorRunId,96)||!Safe(value.WorldId,80)||!Safe(value.BindingZdo,80)||!OptionalSafe(value.BindingInstanceId,80)||!OptionalSafe(value.BindingChangeId,96)||!ValidText(value.LastError,240)||value.ParticipantIds==null||value.ParticipantIds.Count<1||value.ParticipantIds.Count>16||value.ParticipantIds.Any(participant=>!Safe(participant,80)))throw new InvalidDataException("continuation_record_invalid");}
  static IEnumerable<string> Participants(IEnumerable<string> values)=>(values??Enumerable.Empty<string>()).Where(value=>!string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.Ordinal).OrderBy(value=>value,StringComparer.Ordinal);
  static string Bound(string value,int max){if(string.IsNullOrWhiteSpace(value))return null;value=new string(value.Where(ch=>!char.IsControl(ch)).ToArray());return value.Length<=max?value:value.Substring(0,max);}
  static bool OptionalSafe(string value,int max)=>string.IsNullOrWhiteSpace(value)||Safe(value,max);
  static bool ValidText(string value,int max)=>string.IsNullOrWhiteSpace(value)||value.Length<=max&&!value.Any(char.IsControl);
  static bool Safe(string value,int max)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=max&&value.All(ch=>char.IsLetterOrDigit(ch)||ch is '-' or '_' or '.' or ':' or '$' or ';' or ',' or '+');
  sealed class State{[JsonProperty("schema")]public string Schema{get;set;}="comfy-quest-runtime-continuations/v1";[JsonProperty("records")]public List<RuntimeContinuationRecord> Records{get;set;}=new();[JsonIgnore]public bool Unreadable{get;set;}}
}
