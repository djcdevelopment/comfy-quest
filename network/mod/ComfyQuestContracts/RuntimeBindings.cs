namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

/// <summary>A bounded, locally-owned world object that may receive a Charm binding.</summary>
public sealed class RuntimeBindingCandidate {
  [JsonProperty("binding_zdo")] public string BindingZdo {get;set;}
  [JsonProperty("target_kind")] public string TargetKind {get;set;}
  [JsonProperty("label")] public string Label {get;set;}
  [JsonProperty("distance_metres")] public double DistanceMetres {get;set;}
  [JsonProperty("prefab",NullValueHandling=NullValueHandling.Ignore)] public string Prefab {get;set;}
  [JsonProperty("position",NullValueHandling=NullValueHandling.Ignore)] public RuntimeBindingPosition Position {get;set;}
}

public sealed class RuntimeBindingPosition {
  [JsonProperty("x")] public double X {get;set;}
  [JsonProperty("y")] public double Y {get;set;}
  [JsonProperty("z")] public double Z {get;set;}
}

/// <summary>Applies the wire bound without allowing one dense target kind to hide every
/// candidate of another kind. Inputs come from the closed, locally-owned world adapter;
/// the binding coordinator remains responsible for validating the selected wire values.</summary>
public static class RuntimeBindingCandidateSelector {
  sealed class IndexedCandidate {
    public int Index {get;set;}
    public RuntimeBindingCandidate Candidate {get;set;}
  }

  public static IReadOnlyList<RuntimeBindingCandidate> Select(
      IEnumerable<RuntimeBindingCandidate> candidates,int maxCandidates) {
    if(maxCandidates<1)throw new ArgumentOutOfRangeException(nameof(maxCandidates));
    var source=(candidates??Enumerable.Empty<RuntimeBindingCandidate>()).ToArray();
    if(source.Any(value=>value==null))throw new InvalidDataException("binding_candidate_invalid");
    var ordered=source.Select((candidate,index)=>new IndexedCandidate{Index=index,Candidate=candidate})
      .OrderBy(value=>value.Candidate.DistanceMetres)
      .ThenBy(value=>value.Candidate.TargetKind,StringComparer.Ordinal)
      .ThenBy(value=>value.Candidate.BindingZdo,StringComparer.Ordinal)
      .ThenBy(value=>value.Index).ToArray();
    if(ordered.Length<=maxCandidates)return ordered.Select(value=>value.Candidate).ToArray();

    var selected=new HashSet<int>();
    foreach(var representative in ordered.GroupBy(value=>value.Candidate.TargetKind,StringComparer.Ordinal)
        .Select(group=>group.First())) {
      if(selected.Count>=maxCandidates)break;
      selected.Add(representative.Index);
    }
    // A nearby building can contribute dozens of floors and walls. Preserve its
    // explicit interaction anchors before filling the remaining piece choices.
    foreach(var value in ordered.Where(value=>value.Candidate.TargetKind is "sign" or "item_stand" or "dedicated_charm")) {
      if(selected.Count>=maxCandidates)break;
      selected.Add(value.Index);
    }
    foreach(var value in ordered) {
      if(selected.Count>=maxCandidates)break;
      selected.Add(value.Index);
    }
    return ordered.Where(value=>selected.Contains(value.Index)).Select(value=>value.Candidate).ToArray();
  }
}

/// <summary>The ZDO values that constitute one binding. An all-null value means unbound.</summary>
public sealed class RuntimeBindingReference {
  [JsonProperty("pack_id")] public string PackId {get;set;}
  [JsonProperty("experience_id")] public string ExperienceId {get;set;}
  [JsonProperty("binding_id")] public string BindingId {get;set;}
  [JsonProperty("version")] public string Version {get;set;}
  [JsonProperty("content_hash")] public string ContentHash {get;set;}
  [JsonProperty("binding_instance_id",NullValueHandling=NullValueHandling.Ignore)] public string BindingInstanceId {get;set;}
}

/// <summary>Install-local recovery record written before the world ZDO is changed.</summary>
public sealed class RuntimeBindingChange {
  public const string CurrentSchema="comfy-quest-runtime-binding-change/v1";
  [JsonProperty("schema")] public string Schema {get;set;}=CurrentSchema;
  [JsonProperty("change_id")] public string ChangeId {get;set;}
  [JsonProperty("binding_zdo")] public string BindingZdo {get;set;}
  [JsonProperty("resolved_binding_zdo",NullValueHandling=NullValueHandling.Ignore)] public string ResolvedBindingZdo {get;set;}
  [JsonProperty("world_id")] public string WorldId {get;set;}
  [JsonProperty("state")] public string State {get;set;}="pending";
  [JsonProperty("created_utc")] public DateTimeOffset CreatedUtc {get;set;}
  [JsonProperty("completed_utc",NullValueHandling=NullValueHandling.Ignore)] public DateTimeOffset? CompletedUtc {get;set;}
  [JsonProperty("restoration_disposition",NullValueHandling=NullValueHandling.Ignore)] public string RestorationDisposition {get;set;}
  [JsonProperty("previous")] public RuntimeBindingReference Previous {get;set;}
  [JsonProperty("applied")] public RuntimeBindingReference Applied {get;set;}
}

/// <summary>Unity is behind this seam; policy, prerequisite checks and recovery stay testable.</summary>
public interface IRuntimeBindingAdapter {
  IReadOnlyList<RuntimeBindingCandidate> ListCandidates();
  RuntimeBindingReference Read(string bindingZdo);
  bool TryWrite(string bindingZdo,RuntimeBindingReference reference,out string error);
}

/// <summary>Optional authoritative saved-world lookup, independent of nearby selection.</summary>
public interface IRuntimeBindingRecoveryAdapter {
  IReadOnlyList<string> FindBindingInstances(IReadOnlyCollection<string> instanceIds);
}

public sealed class RuntimeBindingCoordinator {
  public const int MaxCandidates=32;
  public const int MaxChanges=512;
  public const int MaxChangeBytes=64*1024;
  public const double MaxCandidateDistanceMetres=20d;
  static readonly HashSet<string> TargetKinds=new(StringComparer.Ordinal){"sign","player_built_piece","item_stand","dedicated_charm"};
  readonly object gate=new();
  readonly string root,changesRoot;
  readonly RuntimeRunRegistry runs;
  readonly IRuntimeBindingAdapter adapter;

  public RuntimeBindingCoordinator(string runtimeRoot,IRuntimeBindingAdapter bindingAdapter,RuntimeRunRegistry registry=null){
    root=Path.GetFullPath(runtimeRoot??throw new ArgumentNullException(nameof(runtimeRoot)));
    changesRoot=Path.Combine(root,"state","binding-changes");
    adapter=bindingAdapter??throw new ArgumentNullException(nameof(bindingAdapter));
    runs=registry??new RuntimeRunRegistry(root);
  }

  public IReadOnlyList<RuntimeBindingCandidate> Candidates(){
    var values=adapter.ListCandidates()??Array.Empty<RuntimeBindingCandidate>();
    if(values.Count>MaxCandidates)throw new InvalidOperationException("binding_candidate_limit");
    var seen=new HashSet<string>(StringComparer.Ordinal);
    foreach(var value in values){
      if(value==null||!SafeZdo(value.BindingZdo)||!TargetKinds.Contains(value.TargetKind)
          ||string.IsNullOrWhiteSpace(value.Label)||value.Label.Length>120||value.Label.Any(char.IsControl)
          ||double.IsNaN(value.DistanceMetres)||double.IsInfinity(value.DistanceMetres)
          ||value.DistanceMetres<0||value.DistanceMetres>MaxCandidateDistanceMetres
          ||!OptionalPrefab(value.Prefab)||!OptionalPosition(value.Position)
          ||!seen.Add(value.BindingZdo))throw new InvalidDataException("binding_candidate_invalid");
    }
    return values.OrderBy(value=>value.DistanceMetres)
      .ThenBy(value=>value.TargetKind,StringComparer.Ordinal)
      .ThenBy(value=>value.BindingZdo,StringComparer.Ordinal).ToArray();
  }
  static bool OptionalPrefab(string value)=>string.IsNullOrWhiteSpace(value)
    ||value.Length<=256&&!value.Any(char.IsControl);
  static bool OptionalPosition(RuntimeBindingPosition value)=>value==null
    ||Finite(value.X)&&Finite(value.Y)&&Finite(value.Z)
      &&Math.Abs(value.X)<=10500&&Math.Abs(value.Y)<=10500&&Math.Abs(value.Z)<=10500;
  static bool Finite(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value);

  public RuntimeBindingChange Bind(string bindingZdo,string worldId,ActiveSet active,
      ExperienceDocument document,DateTimeOffset now){
    if(!SafeZdo(bindingZdo)||!SafeWorld(worldId)||active==null||document==null
        ||!string.Equals(active.ExperienceId,document.Id,StringComparison.Ordinal))
      throw new ArgumentException("binding_scope_invalid");
    var applied=new RuntimeBindingReference{PackId=active.PackId,ExperienceId=document.Id,
      BindingId="default",Version=active.Version,ContentHash=active.ContentHash};
    ValidateReference(applied,false);
    var candidate=Candidates().SingleOrDefault(value=>value.BindingZdo==bindingZdo)
      ??throw new InvalidOperationException("binding_candidate_not_found");
    var binding=(document.Bindings??new List<ExperienceBinding>()).FirstOrDefault(value=>value?.Id=="default")
      ??throw new InvalidOperationException("binding_default_missing");
    if((binding.TargetKinds??new List<string>()).Count>0&&!binding.TargetKinds.Contains(candidate.TargetKind,StringComparer.Ordinal))
      throw new InvalidOperationException("binding_target_incompatible");
    var previous=adapter.Read(bindingZdo)??new RuntimeBindingReference();
    ValidateReference(previous,true);
    foreach(var prerequisite in document.Prerequisites??new List<string>()){
      var complete=runs.List().Any(value=>value?.Outcome=="complete"&&value.Scope!=null
        &&value.Scope.WorldId==worldId&&value.Scope.ExperienceId==prerequisite
        &&(string.IsNullOrWhiteSpace(previous.BindingInstanceId)
          ?string.IsNullOrWhiteSpace(value.Scope.BindingInstanceId)&&value.Scope.BindingZdo==bindingZdo
          :value.Scope.BindingInstanceId==previous.BindingInstanceId)
        &&string.Equals(value.Scope.ContentHash,active.ContentHash,StringComparison.OrdinalIgnoreCase));
      if(!complete)throw new InvalidOperationException("experience_prerequisite_incomplete:"+prerequisite);
    }
    lock(gate){
      var current=adapter.Read(bindingZdo)??new RuntimeBindingReference();
      ValidateReference(current,true);
      if(!Same(current,previous))throw new InvalidOperationException("binding_state_changed");
      Directory.CreateDirectory(changesRoot);
      if(Directory.GetFiles(changesRoot,"binding-*.json").Length>=MaxChanges)
        throw new InvalidOperationException("binding_change_limit");
      var changeId=NewChangeId(now);
      applied.BindingInstanceId=string.IsNullOrWhiteSpace(previous.BindingInstanceId)?changeId:previous.BindingInstanceId;
      ValidateReference(applied,false);
      var change=new RuntimeBindingChange{ChangeId=changeId,BindingZdo=bindingZdo,
        WorldId=worldId,CreatedUtc=now,Previous=previous,Applied=applied};
      var path=ChangePath(change.ChangeId);
      Write(path,change,create:true);
      string failure=null;
      try{
        if(!adapter.TryWrite(bindingZdo,applied,out var error))failure=error??"binding_write_failed";
        else if(!Same(adapter.Read(bindingZdo)??new RuntimeBindingReference(),applied))failure="binding_write_verification_failed";
      }catch(Exception error){failure="binding_write_failed:"+error.GetType().Name;}
      if(failure!=null){
        try{Restore(bindingZdo,change.ChangeId,worldId,DateTimeOffset.UtcNow);}
        catch(Exception recovery){throw new InvalidOperationException(failure+";binding_recovery_failed:"+recovery.Message,recovery);}
        throw new InvalidOperationException(failure);
      }
      change.State="applied";change.CompletedUtc=DateTimeOffset.UtcNow;
      Write(path,change,create:false);
      return change;
    }
  }

  /// <summary>Move an existing Charm to an authored in-pack successor without minting a new
  /// binding identity. Eligibility is decided before this call; this transaction proves that the
  /// exact source reference was still present and that the same target kind is accepted.</summary>
  public RuntimeBindingChange Continue(string bindingZdo,string worldId,ActiveSet active,
      string sourceExperienceId,ExperienceDocument successor,string expectedBindingInstanceId,
      string targetKind,DateTimeOffset now){
    if(!SafeZdo(bindingZdo)||!SafeWorld(worldId)||active==null||successor==null
        ||!string.Equals(active.ExperienceId,successor.Id,StringComparison.Ordinal)
        ||string.IsNullOrWhiteSpace(sourceExperienceId)||!TargetKinds.Contains(targetKind))
      throw new ArgumentException("continuation_binding_scope_invalid");
    var binding=(successor.Bindings??new List<ExperienceBinding>()).FirstOrDefault(value=>value?.Id=="default")
      ??throw new InvalidOperationException("binding_default_missing");
    if((binding.TargetKinds??new List<string>()).Count>0&&!binding.TargetKinds.Contains(targetKind,StringComparer.Ordinal))
      throw new InvalidOperationException("binding_target_incompatible");
    var expected=new RuntimeBindingReference{PackId=active.PackId,ExperienceId=sourceExperienceId,
      BindingId="default",Version=active.Version,ContentHash=active.ContentHash,
      BindingInstanceId=expectedBindingInstanceId};
    var applied=new RuntimeBindingReference{PackId=active.PackId,ExperienceId=successor.Id,
      BindingId="default",Version=active.Version,ContentHash=active.ContentHash,
      BindingInstanceId=expectedBindingInstanceId};
    ValidateReference(expected,false);ValidateReference(applied,false);
    lock(gate){
      var current=adapter.Read(bindingZdo)??new RuntimeBindingReference();
      ValidateReference(current,false);
      if(Same(current,applied)){
        Directory.CreateDirectory(changesRoot);
        var recovered=Directory.GetFiles(changesRoot,"binding-*.json")
          .Select(Read).Where(value=>value!=null&&value.WorldId==worldId
            &&value.Applied!=null&&Same(value.Applied,applied)
            &&value.Previous!=null&&Same(value.Previous,expected)
            &&value.State is "pending" or "applied")
          .OrderByDescending(value=>value.CreatedUtc).ThenByDescending(value=>value.ChangeId,StringComparer.Ordinal)
          .FirstOrDefault();
        if(recovered!=null){
          if(recovered.State=="pending"){recovered.State="applied";recovered.CompletedUtc=now;Write(ChangePath(recovered.ChangeId),recovered,create:false);}
          return recovered;
        }
        if(Directory.GetFiles(changesRoot,"binding-*.json").Length>=MaxChanges)
          throw new InvalidOperationException("binding_change_limit");
        var recoveredId=NewChangeId(now);
        recovered=new RuntimeBindingChange{ChangeId=recoveredId,BindingZdo=bindingZdo,
          WorldId=worldId,CreatedUtc=now,CompletedUtc=now,State="applied",Previous=expected,Applied=applied};
        var recoveredPath=ChangePath(recovered.ChangeId);Write(recoveredPath,recovered,create:true);
        return recovered;
      }
      if(!Same(current,expected))throw new InvalidOperationException("continuation_binding_changed");
      Directory.CreateDirectory(changesRoot);
      if(Directory.GetFiles(changesRoot,"binding-*.json").Length>=MaxChanges)
        throw new InvalidOperationException("binding_change_limit");
      var changeId=NewChangeId(now);
      var change=new RuntimeBindingChange{ChangeId=changeId,BindingZdo=bindingZdo,
        WorldId=worldId,CreatedUtc=now,Previous=expected,Applied=applied};
      var path=ChangePath(change.ChangeId);Write(path,change,create:true);
      string failure=null;
      try{
        if(!adapter.TryWrite(bindingZdo,applied,out var error))failure=error??"binding_write_failed";
        else if(!Same(adapter.Read(bindingZdo)??new RuntimeBindingReference(),applied))failure="binding_write_verification_failed";
      }catch(Exception error){failure="binding_write_failed:"+error.GetType().Name;}
      if(failure!=null){
        try{Restore(bindingZdo,change.ChangeId,worldId,DateTimeOffset.UtcNow);}
        catch(Exception recovery){throw new InvalidOperationException(failure+";binding_recovery_failed:"+recovery.Message,recovery);}
        throw new InvalidOperationException(failure);
      }
      change.State="applied";change.CompletedUtc=DateTimeOffset.UtcNow;Write(path,change,create:false);return change;
    }
  }

  public RuntimeBindingChange Restore(string bindingZdo,string changeId,string worldId,DateTimeOffset now){
    if(!SafeZdo(bindingZdo)||!SafeChangeId(changeId)||!SafeWorld(worldId))throw new ArgumentException("binding_restore_identity_invalid");
    lock(gate){
      var path=ChangePath(changeId);
      var change=Read(path)??throw new InvalidOperationException("binding_change_missing");
      if(change.Schema!=RuntimeBindingChange.CurrentSchema||change.ChangeId!=changeId
          ||change.BindingZdo!=bindingZdo||change.WorldId!=worldId
          ||!string.IsNullOrWhiteSpace(change.ResolvedBindingZdo)&&!SafeZdo(change.ResolvedBindingZdo)
          ||change.State is not ("pending" or "applied" or "restored"))
        throw new InvalidDataException("binding_change_invalid");
      ValidateReference(change.Previous,true);ValidateReference(change.Applied,false);
      var resolvedBindingZdo=ResolveRestoreBinding(change,bindingZdo);
      if(resolvedBindingZdo==null){
        if(runs.List().Any(run=>run.Status=="active" && run.Scope.WorldId==worldId
            && (run.Scope.BindingInstanceId==change.Applied.BindingInstanceId
              || !string.IsNullOrWhiteSpace(change.Previous?.BindingInstanceId)
                && run.Scope.BindingInstanceId==change.Previous.BindingInstanceId)))
          throw new InvalidOperationException("binding_restore_active_runs");
        change.State="restored";change.RestorationDisposition="anchor_absent";
        change.ResolvedBindingZdo=null;change.CompletedUtc=now;Write(path,change,false);return change;
      }
      change.ResolvedBindingZdo=resolvedBindingZdo;
      var current=adapter.Read(resolvedBindingZdo)??new RuntimeBindingReference();
      if(change.State=="restored"&&Same(current,change.Previous))return change;
      // A crash can leave the transaction pending after the ZDO write. Current state, rather
      // than the last persisted word, tells recovery whether restoration is still safe. A
      // pending binding write may be a hybrid of its exact before/after values; no other
      // partial state is accepted.
      if(Same(current,change.Previous)){change.State="restored";change.CompletedUtc=now;Write(path,change,false);return change;}
      if(change.State=="pending"?!CompatiblePartial(current,change.Previous,change.Applied):!Same(current,change.Applied))
        throw new InvalidOperationException("binding_restore_state_changed");
      change.State="pending";change.CompletedUtc=null;Write(path,change,false);
      if(!adapter.TryWrite(resolvedBindingZdo,change.Previous,out var error))throw new InvalidOperationException(error??"binding_restore_failed");
      if(!Same(adapter.Read(resolvedBindingZdo)??new RuntimeBindingReference(),change.Previous))
        throw new InvalidOperationException("binding_restore_verification_failed");
      change.State="restored";change.CompletedUtc=now;Write(path,change,false);return change;
    }
  }

  string ResolveRestoreBinding(RuntimeBindingChange change,string legacyBindingZdo){
    var markers=new HashSet<string>(StringComparer.Ordinal);
    if((change.State!="restored"||change.RestorationDisposition=="anchor_absent")&&!string.IsNullOrWhiteSpace(change.Applied?.BindingInstanceId))
      markers.Add(change.Applied.BindingInstanceId);
    if(change.State!="applied"&&!string.IsNullOrWhiteSpace(change.Previous?.BindingInstanceId))
      markers.Add(change.Previous.BindingInstanceId);
    if(markers.Count==0)return legacyBindingZdo;
    if(adapter is IRuntimeBindingRecoveryAdapter recovery){
      var found=recovery.FindBindingInstances(markers);
      if(found==null)throw new InvalidOperationException("binding_restore_world_unavailable");
      if(found.Count>1)throw new InvalidOperationException("binding_restore_instance_ambiguous");
      return found.SingleOrDefault();
    }
    var matches=new List<string>();
    foreach(var candidate in Candidates()){
      var reference=adapter.Read(candidate.BindingZdo)??new RuntimeBindingReference();
      if(markers.Contains(reference.BindingInstanceId))matches.Add(candidate.BindingZdo);
    }
    if(matches.Count==0)throw new InvalidOperationException("binding_restore_instance_missing");
    if(matches.Count!=1)throw new InvalidOperationException("binding_restore_instance_ambiguous");
    return matches[0];
  }

  static void ValidateReference(RuntimeBindingReference value,bool allowEmpty){
    if(value==null)throw new InvalidDataException("binding_reference_invalid");
    var empty=string.IsNullOrWhiteSpace(value.PackId)&&string.IsNullOrWhiteSpace(value.ExperienceId)
      &&string.IsNullOrWhiteSpace(value.BindingId)&&string.IsNullOrWhiteSpace(value.Version)
      &&string.IsNullOrWhiteSpace(value.ContentHash)&&string.IsNullOrWhiteSpace(value.BindingInstanceId);
    if(empty&&allowEmpty)return;
    var decision=CharmPolicy.ValidateReference(new CharmReference{PackId=value.PackId,
      ExperienceId=value.ExperienceId,BindingId=value.BindingId,Version=value.Version,
      ContentHash=value.ContentHash});
    if(!decision.Allowed)throw new InvalidDataException(decision.Diagnostic??"binding_reference_invalid");
    if(!string.IsNullOrWhiteSpace(value.BindingInstanceId)&&!SafeChangeId(value.BindingInstanceId))
      throw new InvalidDataException("binding_instance_id_invalid");
  }
  static bool Same(RuntimeBindingReference left,RuntimeBindingReference right)=>
    string.Equals(left?.PackId,right?.PackId,StringComparison.Ordinal)
    &&string.Equals(left?.ExperienceId,right?.ExperienceId,StringComparison.Ordinal)
    &&string.Equals(left?.BindingId,right?.BindingId,StringComparison.Ordinal)
    &&string.Equals(left?.Version,right?.Version,StringComparison.Ordinal)
    &&string.Equals(left?.ContentHash,right?.ContentHash,StringComparison.OrdinalIgnoreCase)
    &&string.Equals(left?.BindingInstanceId,right?.BindingInstanceId,StringComparison.Ordinal);
  static bool CompatiblePartial(RuntimeBindingReference current,RuntimeBindingReference previous,RuntimeBindingReference applied)=>
    OneOf(current?.PackId,previous?.PackId,applied?.PackId,StringComparison.Ordinal)
    &&OneOf(current?.ExperienceId,previous?.ExperienceId,applied?.ExperienceId,StringComparison.Ordinal)
    &&OneOf(current?.BindingId,previous?.BindingId,applied?.BindingId,StringComparison.Ordinal)
    &&OneOf(current?.Version,previous?.Version,applied?.Version,StringComparison.Ordinal)
    &&OneOf(current?.ContentHash,previous?.ContentHash,applied?.ContentHash,StringComparison.OrdinalIgnoreCase)
    &&OneOf(current?.BindingInstanceId,previous?.BindingInstanceId,applied?.BindingInstanceId,StringComparison.Ordinal);
  static bool OneOf(string current,string previous,string applied,StringComparison comparison)=>
    string.Equals(current,previous,comparison)||string.Equals(current,applied,comparison);
  RuntimeBindingChange Read(string path){try{var info=new FileInfo(path);return info.Exists&&info.Length>0&&info.Length<=MaxChangeBytes?JsonConvert.DeserializeObject<RuntimeBindingChange>(File.ReadAllText(path)):null;}catch{return null;}}
  void Write(string path,RuntimeBindingChange value,bool create){var json=JsonConvert.SerializeObject(value,Formatting.Indented);if(Encoding.UTF8.GetByteCount(json)>MaxChangeBytes)throw new InvalidDataException("binding_change_too_large");Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+".tmp";File.WriteAllText(temp,json);if(create){if(File.Exists(path)){File.Delete(temp);throw new IOException("binding_change_collision");}File.Move(temp,path);}else File.Replace(temp,path,null);}
  string ChangePath(string id)=>Path.Combine(changesRoot,id+".json");
  static string NewChangeId(DateTimeOffset now)=>"binding-"+now.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
  static bool SafeChangeId(string value)=>!string.IsNullOrWhiteSpace(value)&&value.Length==36&&value.StartsWith("binding-",StringComparison.Ordinal)&&value.All(ch=>char.IsLetterOrDigit(ch)||ch=='-'||ch=='T'||ch=='Z');
  static bool SafeWorld(string value)=>long.TryParse(value,NumberStyles.Integer,CultureInfo.InvariantCulture,out var world)&&world!=0;
  static bool SafeZdo(string value){if(string.IsNullOrWhiteSpace(value)||value.Length>80)return false;var parts=value.Split(':');return parts.Length==2&&long.TryParse(parts[0],NumberStyles.Integer,CultureInfo.InvariantCulture,out _)&&uint.TryParse(parts[1],NumberStyles.None,CultureInfo.InvariantCulture,out _);}
}
