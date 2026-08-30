namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Newtonsoft.Json;

public sealed class RuntimeEvent {
  public string Name { get; set; }
  public string Target { get; set; }
  public string SourceId { get; set; }
  public DateTimeOffset At { get; set; }
  public IReadOnlyDictionary<string,string> Fields { get; set; }
  // Witness position stamped at emission by the Runtime spatial observation seam. Additive:
  // events without a position deserialize null and fail spatial predicates closed.
  [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public double? PosX { get; set; }
  [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public double? PosY { get; set; }
  [JsonProperty(NullValueHandling=NullValueHandling.Ignore)] public double? PosZ { get; set; }
  [JsonIgnore] public string DedupeKey { get; set; }
}

public sealed class TriggerProgress {
  public int Current { get; set; }
  public int Required { get; set; }
  public bool Complete { get { return Required > 0 && Current >= Required; } }
}

public sealed class TriggerEvaluationContext {
  public DateTimeOffset? At { get; set; }
  public DateTimeOffset? StageEnteredUtc { get; set; }
  public DateTimeOffset? LastProgressUtc { get; set; }
  public SpatialPoint? BindingPosition { get; set; }
  public IReadOnlyList<SpatialPoint> SpawnedPositions { get; set; }
  public IReadOnlyDictionary<string,SpatialArea> SpatialAreas { get; set; }
  public int? DeathsInStage { get; set; }
  public IReadOnlyDictionary<string,SpawnTally> SpawnsByAction { get; set; }
  public bool TryMeasure(string name,out double value)=>TryMeasure(name,null,out value);
  public bool TryMeasure(string name,string actionId,out double value){value=0;if(string.Equals(name,AdaptiveMeasureCatalog.DeathsMeasure,StringComparison.Ordinal)){if(!DeathsInStage.HasValue)return false;value=DeathsInStage.Value;return true;}var remaining=string.Equals(name,AdaptiveMeasureCatalog.RemainingMeasure,StringComparison.Ordinal);if(remaining||string.Equals(name,AdaptiveMeasureCatalog.ClearedMeasure,StringComparison.Ordinal)){if(string.IsNullOrWhiteSpace(actionId)||SpawnsByAction==null||!SpawnsByAction.TryGetValue(actionId,out var tally)||tally==null)return false;value=remaining?tally.Live:tally.Cleared;return true;}if(!At.HasValue)return false;DateTimeOffset? origin=null;if(string.Equals(name,"time_since_stage_entered",StringComparison.Ordinal))origin=StageEnteredUtc;else if(string.Equals(name,"time_since_progress",StringComparison.Ordinal))origin=LastProgressUtc;else return false;if(!origin.HasValue)return false;value=(At.Value-origin.Value).TotalSeconds;return true;}
  public SpatialArea FindArea(string areaId){if(string.IsNullOrWhiteSpace(areaId)||SpatialAreas==null||!SpatialAreas.TryGetValue(areaId,out var area))return null;return area;}
  public bool TryResolveArea(string areaId,RuntimeEvent trigger,out SpatialPoint point){point=default;var area=FindArea(areaId);if(area==null)return false;if(string.Equals(area.Frame,"world",StringComparison.Ordinal)){if(area.Center==null)return false;point=new SpatialPoint(area.Center.X,area.Center.Y,area.Center.Z);return true;}if(string.Equals(area.Frame,"binding",StringComparison.Ordinal)){if(!BindingPosition.HasValue)return false;point=BindingPosition.Value;return true;}if(string.Equals(area.Frame,"player",StringComparison.Ordinal))return SpatialEvaluator.TryPosition(trigger,out point);return false;}
}

public sealed class TriggerWhereTrace {
  [JsonProperty("field")] public string Field { get; set; }
  [JsonProperty("expected")] public string Expected { get; set; }
  [JsonProperty("actual", NullValueHandling=NullValueHandling.Ignore)] public string Actual { get; set; }
  [JsonProperty("satisfied")] public bool Satisfied { get; set; }
}

public sealed class TriggerClauseTrace {
  [JsonProperty("op")] public string Op { get; set; }
  [JsonProperty("event", NullValueHandling=NullValueHandling.Ignore)] public string Event { get; set; }
  [JsonProperty("target", NullValueHandling=NullValueHandling.Ignore)] public string Target { get; set; }
  [JsonProperty("satisfied")] public bool Satisfied { get; set; }
  [JsonProperty("current")] public int Current { get; set; }
  [JsonProperty("required")] public int Required { get; set; }
  [JsonProperty("sequence_index", NullValueHandling=NullValueHandling.Ignore)] public int? SequenceIndex { get; set; }
  [JsonProperty("within_seconds", NullValueHandling=NullValueHandling.Ignore)] public int? WithinSeconds { get; set; }
  [JsonProperty("where")] public List<TriggerWhereTrace> Where { get; set; } = new();
  [JsonProperty("children")] public List<TriggerClauseTrace> Children { get; set; } = new();
  [JsonProperty("truncated", NullValueHandling=NullValueHandling.Ignore)] public bool? Truncated { get; set; }
  [JsonProperty("area_id", NullValueHandling=NullValueHandling.Ignore)] public string AreaId { get; set; }
  [JsonProperty("spatial", NullValueHandling=NullValueHandling.Ignore)] public string Spatial { get; set; }
  [JsonProperty("anchor_sha256", NullValueHandling=NullValueHandling.Ignore)] public string AnchorSha256 { get; set; }
  [JsonProperty("resolved_center", NullValueHandling=NullValueHandling.Ignore)] public SpatialContractPoint ResolvedCenter { get; set; }
  [JsonProperty("radius_meters", NullValueHandling=NullValueHandling.Ignore)] public double? RadiusMeters { get; set; }
  [JsonProperty("observed_position", NullValueHandling=NullValueHandling.Ignore)] public SpatialContractPoint ObservedPosition { get; set; }
  [JsonProperty("distance_meters", NullValueHandling=NullValueHandling.Ignore)] public double? DistanceMeters { get; set; }
}

public sealed class RejectedTransitionEvidence {
  [JsonProperty("transition_id")] public string TransitionId { get; set; }
  [JsonProperty("evidence")] public TriggerClauseTrace Evidence { get; set; }
}

public static class TriggerEvaluator {
  public const int MaxTraceSerializedBytes = 8 * 1024;
  public const int MaxTraceWhereEntries = 8;
  public static bool Matches(TriggerExpression expression, IReadOnlyList<RuntimeEvent> history) {
    if (expression == null) return false;
    history ??= Array.Empty<RuntimeEvent>();
    var bounded = expression.WithinSeconds.HasValue && history.Count > 0
      ? history.Where(x => x.At >= history[history.Count-1].At.AddSeconds(-expression.WithinSeconds.Value)).ToArray()
      : history.ToArray();
    return Eval(expression, bounded);
  }
  public static bool Matches(TriggerExpression expression,IReadOnlyList<RuntimeEvent> history,TriggerEvaluationContext context) {
    if(expression==null)return false;history??=Array.Empty<RuntimeEvent>();var bounded=Bound(expression,history);return Eval(expression,bounded,context);
  }
  public static TriggerClauseTrace Explain(TriggerExpression expression, IReadOnlyList<RuntimeEvent> history) {
    history ??= Array.Empty<RuntimeEvent>();
    var bounded = expression?.WithinSeconds.HasValue == true && history.Count > 0
      ? history.Where(x => x.At >= history[history.Count - 1].At.AddSeconds(-expression.WithinSeconds.Value)).ToArray()
      : history.ToArray();
    return BuildBoundedTrace(expression, bounded);
  }
  public static TriggerClauseTrace Explain(TriggerExpression expression,IReadOnlyList<RuntimeEvent> history,TriggerEvaluationContext context) {
    history??=Array.Empty<RuntimeEvent>();return BuildBoundedTrace(expression,Bound(expression,history),context);
  }
  public static TriggerProgress Measure(TriggerExpression expression, IReadOnlyList<RuntimeEvent> history) {
    history ??= Array.Empty<RuntimeEvent>();
    if (expression == null) return new TriggerProgress { Current = 0, Required = 1 };
    var bounded = expression.WithinSeconds.HasValue && history.Count > 0
      ? history.Where(x => x.At >= history[history.Count - 1].At.AddSeconds(-expression.WithinSeconds.Value)).ToArray()
      : history.ToArray();
    if (string.Equals(expression.Op, "COUNT", StringComparison.OrdinalIgnoreCase)
        && expression.Children?.Count == 1) {
      var required = Math.Max(1, expression.Count.GetValueOrDefault());
      var current = bounded.Count(value => EventMatches(expression.Children[0], value));
      return new TriggerProgress { Current = Math.Min(current, required), Required = required };
    }
    return new TriggerProgress { Current = Eval(expression, bounded) ? 1 : 0, Required = 1 };
  }
  public static TriggerProgress Measure(TriggerExpression expression,IReadOnlyList<RuntimeEvent> history,TriggerEvaluationContext context) {
    history??=Array.Empty<RuntimeEvent>();if(expression==null)return new TriggerProgress{Current=0,Required=1};var bounded=Bound(expression,history);if(string.Equals(expression.Op,"COUNT",StringComparison.OrdinalIgnoreCase)&&expression.Children?.Count==1){var required=Math.Max(1,expression.Count.GetValueOrDefault());var current=bounded.Count(value=>EventMatches(expression.Children[0],value));return new TriggerProgress{Current=Math.Min(current,required),Required=required};}if(string.Equals(expression.Op,"THRESHOLD",StringComparison.OrdinalIgnoreCase))return AdaptiveEvaluator.Progress(expression,context);if(string.Equals(expression.Op,"SPATIAL",StringComparison.OrdinalIgnoreCase))return SpatialEvaluator.Progress(expression,bounded,context);return new TriggerProgress{Current=Eval(expression,bounded,context)?1:0,Required=1};
  }
  public static int EventProgress(TriggerExpression expression,IReadOnlyList<RuntimeEvent> history){history??=Array.Empty<RuntimeEvent>();return EventProgressNode(expression,Bound(expression,history));}
  static bool Eval(TriggerExpression x, IReadOnlyList<RuntimeEvent> h) {
    if (x == null) return false;
    var op=(x.Op??"").ToUpperInvariant();
    if(op=="EVENT") return h.Any(v=>EventMatches(x,v));
    var c=x.Children??new();
    if(op=="ANY") return c.Any(v=>Eval(v,h));
    if(op=="ALL") return c.All(v=>Eval(v,h));
    if(op=="COUNT") return c.Count==1 && h.Count(v=>EventMatches(c[0],v))>=x.Count.GetValueOrDefault();
    if(op=="SEQUENCE") { int i=0; foreach(var v in h) if(i<c.Count&&EventMatches(c[i],v)) i++; return i==c.Count; }
    return false;
  }
  static bool Eval(TriggerExpression x,IReadOnlyList<RuntimeEvent> h,TriggerEvaluationContext context){if(x==null)return false;var op=(x.Op??"").ToUpperInvariant();if(op=="THRESHOLD")return AdaptiveEvaluator.Satisfied(x,context);if(op=="SPATIAL")return SpatialEvaluator.Satisfied(x,h,context);if(op=="EVENT")return h.Any(v=>EventMatches(x,v));var c=x.Children??new();if(op=="ANY")return c.Any(v=>Eval(v,h,context));if(op=="ALL")return c.All(v=>Eval(v,h,context));if(op=="COUNT")return c.Count==1&&h.Count(v=>EventMatches(c[0],v))>=x.Count.GetValueOrDefault();if(op=="SEQUENCE"){int i=0;foreach(var v in h)if(i<c.Count&&EventMatches(c[i],v))i++;return i==c.Count;}return false;}
  static TriggerClauseTrace BuildBoundedTrace(TriggerExpression expression,IReadOnlyList<RuntimeEvent> history,TriggerEvaluationContext context=null) {
    var nodeCount=CountNodes(expression);
    var full=BuildTrace(expression,history,context,nodeCount,128,out _);
    if(TraceBytes(full)<=MaxTraceSerializedBytes)return full;
    TriggerClauseTrace best=null;var bestNodes=0;var bestCharacters=0;
    for(var maxCharacters=128;maxCharacters>=8;maxCharacters/=2) {
      var low=1;var high=nodeCount;TriggerClauseTrace fit=null;var fitNodes=0;
      while(low<=high) {
        var budget=low+(high-low)/2;
        var candidate=BuildTrace(expression,history,context,budget,maxCharacters,out var used);
        candidate.Truncated=true;
        if(TraceBytes(candidate)<=MaxTraceSerializedBytes){fit=candidate;fitNodes=used;low=budget+1;}else high=budget-1;
      }
      if(fit!=null&&(fitNodes>bestNodes||(fitNodes==bestNodes&&maxCharacters>bestCharacters))){best=fit;bestNodes=fitNodes;bestCharacters=maxCharacters;}
    }
    return best??new TriggerClauseTrace{Op="",Satisfied=false,Current=0,Required=1,Truncated=true};
  }
  static TriggerClauseTrace BuildTrace(TriggerExpression expression,IReadOnlyList<RuntimeEvent> history,TriggerEvaluationContext context,int nodeBudget,int maxCharacters,out int used) {
    var remaining=Math.Max(1,nodeBudget);var trace=ExplainNode(expression,history,context,true,ref remaining,maxCharacters);used=Math.Max(1,nodeBudget)-remaining;return trace;
  }
  static TriggerClauseTrace ExplainNode(TriggerExpression x,IReadOnlyList<RuntimeEvent> h,TriggerEvaluationContext context,bool isRoot,ref int remaining,int maxCharacters) {
    remaining--;
    if(x==null)return new(){Satisfied=false,Current=0,Required=1};
    var op=(x.Op??"").ToUpperInvariant();
    var children=x.Children??new();var stringsTruncated=false;
    var trace=new TriggerClauseTrace{Op=Trim(op,maxCharacters,ref stringsTruncated),Event=Trim(x.Event,maxCharacters,ref stringsTruncated),Target=Trim(x.Target,maxCharacters,ref stringsTruncated),Satisfied=context==null?Eval(x,h):Eval(x,h,context),WithinSeconds=isRoot?x.WithinSeconds:null};
    if(op=="EVENT") {
      // Actuals may only come from an event the clause could have considered: same name and,
      // when the clause names a target, the same target. A name-only fallback would harvest
      // all-satisfied where rows from an event the target check already rejected.
      var exact=h.Reverse().FirstOrDefault(v=>EventMatches(x,v));
      var candidate=exact??h.Reverse().FirstOrDefault(v=>string.Equals(x.Event,v.Name,StringComparison.OrdinalIgnoreCase)&&(string.IsNullOrWhiteSpace(x.Target)||string.Equals(x.Target,v.Target,StringComparison.OrdinalIgnoreCase)));
      var where=(x.Where??new()).OrderBy(v=>v.Key,StringComparer.Ordinal).Select(expected=>{
        string actual=null;var found=candidate?.Fields!=null&&candidate.Fields.TryGetValue(expected.Key,out actual);
        return new TriggerWhereTrace{Field=expected.Key,Expected=expected.Value,Actual=found?actual:null,Satisfied=found&&string.Equals(actual,expected.Value,StringComparison.OrdinalIgnoreCase)};
      }).ToArray();
      foreach(var item in where.OrderBy(v=>v.Satisfied?1:0).ThenBy(v=>v.Field,StringComparer.Ordinal).Take(MaxTraceWhereEntries).OrderBy(v=>v.Field,StringComparer.Ordinal)) {
        trace.Where.Add(new(){Field=Trim(item.Field,maxCharacters,ref stringsTruncated),Expected=Trim(item.Expected,maxCharacters,ref stringsTruncated),Actual=Trim(item.Actual,maxCharacters,ref stringsTruncated),Satisfied=item.Satisfied});
      }
      if(where.Length>MaxTraceWhereEntries)trace.Truncated=true;
      trace.Current=trace.Satisfied?1:0;trace.Required=1;if(stringsTruncated)trace.Truncated=true;return trace;
    }
    if(op=="THRESHOLD") {
      var observation=AdaptiveEvaluator.Observe(x,context);
      trace.Current=observation.Current;trace.Required=observation.Required;
      trace.Where.Add(new TriggerWhereTrace{Field=Trim(x.Measure,maxCharacters,ref stringsTruncated),Expected=Trim(observation.Expected,maxCharacters,ref stringsTruncated),Actual=Trim(observation.Actual,maxCharacters,ref stringsTruncated),Satisfied=trace.Satisfied});
      if(stringsTruncated)trace.Truncated=true;return trace;
    }
    if(op=="SPATIAL") {
      var observation=SpatialEvaluator.Observe(x,h,context);
      trace.Current=observation.Current;trace.Required=observation.Required;
      trace.AreaId=observation.AreaId;trace.Spatial=x.Spatial;trace.AnchorSha256=observation.AnchorSha256;trace.RadiusMeters=observation.RadiusMeters;
      if(observation.ResolvedCenter.HasValue){var p=observation.ResolvedCenter.Value;trace.ResolvedCenter=new SpatialContractPoint(p.X,p.Y,p.Z);}
      if(observation.ObservedPosition.HasValue){var p=observation.ObservedPosition.Value;trace.ObservedPosition=new SpatialContractPoint(p.X,p.Y,p.Z);}
      trace.DistanceMeters=observation.DistanceMeters;
      trace.Where.Add(new TriggerWhereTrace{Field=Trim(x.Spatial,maxCharacters,ref stringsTruncated),Expected=Trim(observation.Expected,maxCharacters,ref stringsTruncated),Actual=Trim(observation.Actual,maxCharacters,ref stringsTruncated),Satisfied=trace.Satisfied});
      if(stringsTruncated)trace.Truncated=true;return trace;
    }
    foreach(var child in children) {
      if(remaining<=0){trace.Truncated=true;break;}
      trace.Children.Add(ExplainNode(child,h,context,false,ref remaining,maxCharacters));
    }
    if(op=="ANY") {trace.Current=children.Count(v=>context==null?Eval(v,h):Eval(v,h,context));trace.Required=1;}
    else if(op=="ALL") {trace.Current=children.Count(v=>context==null?Eval(v,h):Eval(v,h,context));trace.Required=children.Count;}
    else if(op=="COUNT") {
      var required=Math.Max(1,x.Count.GetValueOrDefault());
      var current=children.Count==1?h.Count(v=>EventMatches(children[0],v)):0;
      // Creator-facing progress deliberately saturates at the threshold, matching Measure.
      trace.Current=Math.Min(current,required);trace.Required=required;
    }
    else if(op=="SEQUENCE") {
      int index=0;foreach(var value in h)if(index<children.Count&&EventMatches(children[index],value))index++;
      trace.Current=index;trace.Required=children.Count;trace.SequenceIndex=index;
    }
    else {trace.Current=0;trace.Required=1;}
    if(stringsTruncated)trace.Truncated=true;return trace;
  }
  static int CountNodes(TriggerExpression expression)=>expression==null?1:1+(expression.Children??new()).Sum(CountNodes);
  static IReadOnlyList<RuntimeEvent> Bound(TriggerExpression expression,IReadOnlyList<RuntimeEvent> history)=>expression?.WithinSeconds.HasValue==true&&history.Count>0?history.Where(x=>x.At>=history[history.Count-1].At.AddSeconds(-expression.WithinSeconds.Value)).ToArray():history.ToArray();
  static int EventProgressNode(TriggerExpression x,IReadOnlyList<RuntimeEvent> h){if(x==null)return 0;var op=(x.Op??"").ToUpperInvariant();if(op=="THRESHOLD"||op=="SPATIAL")return 0;if(op=="EVENT")return h.Any(v=>EventMatches(x,v))?1:0;var c=x.Children??new();if(op=="COUNT")return c.Count==1?Math.Min(h.Count(v=>EventMatches(c[0],v)),Math.Max(1,x.Count.GetValueOrDefault())):0;if(op=="SEQUENCE"){int i=0;foreach(var v in h)if(i<c.Count&&EventMatches(c[i],v))i++;return i;}return c.Sum(v=>EventProgressNode(v,h));}
  static int TraceBytes(TriggerClauseTrace trace)=>Encoding.UTF8.GetByteCount(JsonConvert.SerializeObject(trace,Formatting.Indented));
  static string Trim(string value,int max,ref bool truncated){if(value==null||value.Length<=max)return value;truncated=true;return value.Substring(0,max);}
  static bool EventMatches(TriggerExpression x,RuntimeEvent v){if(x==null||!string.Equals(x.Op,"EVENT",StringComparison.OrdinalIgnoreCase)||!string.Equals(x.Event,v.Name,StringComparison.OrdinalIgnoreCase))return false;if(!string.IsNullOrWhiteSpace(x.Target)&&!string.Equals(x.Target,v.Target,StringComparison.OrdinalIgnoreCase))return false;foreach(var p in x.Where??new()){if(v.Fields==null||!v.Fields.TryGetValue(p.Key,out var value)||!string.Equals(value,p.Value,StringComparison.OrdinalIgnoreCase))return false;}return true;}
}

public sealed class QuestPackManifest {
  [JsonProperty("schema")] public string Schema { get; set; } = "comfy-quest-pack/v2";
  [JsonProperty("pack_id")] public string PackId { get; set; }
  [JsonProperty("version")] public string Version { get; set; }
  [JsonProperty("content_hash")] public string ContentHash { get; set; }
}

public enum QuestPackLane { Production, Dev }

public sealed class PackCandidate { public string Path {get;set;} public QuestPackManifest Manifest {get;set;} public string Sha256 {get;set;} public string ContentHash {get;set;} public QuestPackLane Lane {get;set;} public IReadOnlyList<ContractDiagnostic> Diagnostics {get;set;} public IReadOnlyList<string> Titles {get;set;}=Array.Empty<string>(); public bool IsValid=>Diagnostics.Count==0; }

public sealed class ActiveSet {
  [JsonProperty("schema")] public string Schema {get;set;}
  [JsonProperty("pack_id")] public string PackId {get;set;}
  [JsonProperty("version")] public string Version {get;set;}
  [JsonProperty("content_hash")] public string ContentHash {get;set;}
  [JsonProperty("package_sha256")] public string PackageSha256 {get;set;}
  [JsonProperty("source")] public string Source {get;set;}
  [JsonProperty("activated_utc")] public DateTimeOffset ActivatedUtc {get;set;}
  [JsonProperty("activation_id", NullValueHandling=NullValueHandling.Ignore)] public string ActivationId {get;set;}
  /// <summary>Which experience of a multi-experience pack is bound and run. Additive and nullable
  /// on purpose, the same shape activation_id uses: an absent selector means the pack holds exactly
  /// one, which is every pack written before this field existed. Activation deliberately does not
  /// carry it forward — a new revision may not contain the selected experience, and a silently
  /// wrong selection is worse than an explicit re-selection.</summary>
  [JsonProperty("experience_id", NullValueHandling=NullValueHandling.Ignore)] public string ExperienceId {get;set;}
  [JsonProperty("source_channel", NullValueHandling=NullValueHandling.Ignore)] public string SourceChannel {get;set;}
  [JsonProperty("previous_activation_id", NullValueHandling=NullValueHandling.Ignore)] public string PreviousActivationId {get;set;}
}

/// <summary>Which experience document of an activated pack Runtime binds and runs.
/// <para>QuestPackStore already validates, hashes, and compiles <em>N</em> documents. Before the
/// selector existed both Runtime call sites simply refused the second one with
/// <c>active_experience_ambiguous</c>, which is the single check that kept a guild from shipping
/// and binding as one unit. Resolution lives here rather than at either call site so that the two
/// cannot drift apart, and so it is testable without the game.</para></summary>
public static class ActiveExperienceResolver {
  /// <summary>Declared before the feature ships, per NFR-BOUND-001. Selection reads every entry to
  /// match an id, so the read is bounded by an explicit number rather than by whatever the archive
  /// happens to hold. A guild is authored content, not a catalog.</summary>
  public const int MaxSelectableExperiences=64;
  public const string Missing="active_experience_missing";
  public const string Ambiguous="active_experience_ambiguous";
  public const string NotInPack="active_experience_not_in_pack";
  public const string TooMany="active_experience_count_exceeded";
  public const string Unreadable="active_experience_unreadable";

  /// <summary>Every experience id in the pack, in archive order. Empty when the pack cannot be
  /// read: this answers "what could I have selected", never "what is selected".</summary>
  public static IReadOnlyList<string> Ids(ZipArchive zip){return Scan(zip,out var documents,out _)?documents.Select(value=>value.Id).ToArray():Array.Empty<string>();}

  /// <summary>All raw experience documents in deterministic archive order. Runtime uses this to
  /// resolve an authored continuation without changing the active selector merely to inspect it.</summary>
  public static bool TryResolveAll(ZipArchive zip,out IReadOnlyList<ResolvedExperience> resolved,out string error){
    if(!Scan(zip,out var documents,out error)){resolved=Array.Empty<ResolvedExperience>();return false;}
    var available=documents.Select(value=>value.Id).ToArray();
    foreach(var document in documents)document.Available=available;
    resolved=documents;
    return true;
  }

  /// <summary>Resolve the bound document. An absent or blank <paramref name="requestedId"/> means
  /// "the pack holds exactly one" — every pack shipped before this field existed — and resolves
  /// exactly as it did then, including the same diagnostic when it holds more.</summary>
  public static bool TryResolve(ZipArchive zip,string requestedId,out ResolvedExperience resolved,out string error){
    resolved=null;
    if(!Scan(zip,out var documents,out error))return false;
    var wanted=string.IsNullOrWhiteSpace(requestedId)?null:requestedId.Trim();
    if(wanted==null){
      if(documents.Count>1){error=Ambiguous;return false;}
      resolved=documents[0];
    } else {
      resolved=documents.Find(value=>string.Equals(value.Id,wanted,StringComparison.Ordinal));
      if(resolved==null){error=NotInPack;return false;}
    }
    resolved.Available=documents.Select(value=>value.Id).ToArray();
    error=null;
    return true;
  }

  static bool Scan(ZipArchive zip,out List<ResolvedExperience> documents,out string error){
    documents=new List<ResolvedExperience>();
    error=Unreadable;
    if(zip==null)return false;
    var entries=zip.Entries
      .Where(value=>value.FullName.StartsWith("experiences/",StringComparison.Ordinal)
        &&value.FullName.EndsWith(".json",StringComparison.Ordinal))
      .OrderBy(value=>value.FullName,StringComparer.Ordinal).ToArray();
    if(entries.Length==0){error=Missing;return false;}
    if(entries.Length>MaxSelectableExperiences){error=TooMany;return false;}
    var seen=new HashSet<string>(StringComparer.Ordinal);
    foreach(var entry in entries){
      string json,id;
      try{
        using var reader=new StreamReader(entry.Open());
        json=reader.ReadToEnd();
        id=JsonConvert.DeserializeObject<ExperienceDocument>(json)?.Id;
      }catch{error=Unreadable;return false;}
      if(string.IsNullOrWhiteSpace(id)){error=Unreadable;return false;}
      // Two documents under one id can never be selected between, so this stays ambiguous
      // rather than resolving to whichever one the archive listed first.
      if(!seen.Add(id)){error=Ambiguous;return false;}
      documents.Add(new ResolvedExperience{Id=id,Json=json,EntryName=entry.FullName});
    }
    error=null;
    return true;
  }

  public sealed class ResolvedExperience {
    public string Id {get;set;}
    public string Json {get;set;}
    public string EntryName {get;set;}
    public IReadOnlyList<string> Available {get;set;}=Array.Empty<string>();
  }
}

public sealed class QuestPackStore {
  public const int MaxArchiveEntries=512;
  public const long MaxExpandedBytes=8L*1024*1024;
  public const int MaxManifestBytes=64*1024;
  public const int MaxActivationHistory=10;
  readonly string root;
  public QuestPackStore(string rootPath){root=Path.GetFullPath(rootPath??throw new ArgumentNullException(nameof(rootPath)));}
  public IReadOnlyList<PackCandidate> CheckInbox(ISet<string> events=null)=>CheckLane(QuestPackLane.Production,events);
  public IReadOnlyList<PackCandidate> CheckDevInbox(ISet<string> events=null)=>CheckLane(QuestPackLane.Dev,events);
  public PackCandidate Inspect(string path,ISet<string> events=null)=>InspectLane(path,QuestPackLane.Production,events);
  public PackCandidate InspectDev(string path,ISet<string> events=null)=>InspectLane(path,QuestPackLane.Dev,events);
  IReadOnlyList<PackCandidate> CheckLane(QuestPackLane lane,ISet<string> events){var inbox=LaneRoot(lane);if(!Directory.Exists(inbox))return Array.Empty<PackCandidate>();return Directory.GetFiles(inbox,"*.questpack").Select(x=>InspectLane(x,lane,events)).ToArray();}
  PackCandidate InspectLane(string path,QuestPackLane lane,ISet<string> events){
    var production=events==null;
    events??=RuntimeProductionEventCatalog.CreateSet();
    var errors=new List<ContractDiagnostic>();
    var titles=new List<string>();
    var documents=new List<ExperienceDocument>();
    var full=Path.GetFullPath(path);
    if(!string.Equals(Path.GetDirectoryName(full),LaneRoot(lane),StringComparison.OrdinalIgnoreCase))
      errors.Add(new("pack.path","$",lane==QuestPackLane.Dev
        ?"Pack must be directly inside the runtime dev inbox."
        :"Pack must be directly inside the runtime inbox."));
    QuestPackManifest manifest=null;
    string sha=null,contentHash=null;
    try{
      sha=HashFile(full);
      using var zip=ZipFile.OpenRead(full);
      if(zip.Entries.Count>MaxArchiveEntries)errors.Add(new("pack.entries","$","Archive exceeds 512 entries."));
      long expanded=0;
      foreach(var entry in zip.Entries){
        if(entry.Length>MaxExpandedBytes-expanded){errors.Add(new("pack.expanded_size","$","Archive expands beyond 8 MiB."));break;}
        expanded+=entry.Length;
        if(!SafeEntry(entry.FullName))errors.Add(new("pack.path","$","Unsafe or unsupported archive entry."));
      }
      var manifests=zip.Entries.Where(x=>x.FullName=="manifest.json").ToArray();
      if(manifests.Length!=1)throw new InvalidDataException("Exactly one manifest.json is required");
      if(manifests[0].Length>MaxManifestBytes)errors.Add(new("pack.manifest_size","$.manifest","Manifest exceeds 64 KiB."));
      using(var reader=new StreamReader(manifests[0].Open()))manifest=JsonConvert.DeserializeObject<QuestPackManifest>(reader.ReadToEnd());
      var experiences=zip.Entries.Where(x=>x.FullName.StartsWith("experiences/",StringComparison.Ordinal)&&x.FullName.EndsWith(".json",StringComparison.Ordinal)).OrderBy(x=>x.FullName,StringComparer.Ordinal).ToArray();
      var contentEntries=new List<KeyValuePair<string,byte[]>>();
      foreach(var item in experiences){
        if(item.Length>ExperienceSchema.MaxDocumentBytes){errors.Add(new("document.size",item.FullName,"Experience exceeds 1 MiB."));continue;}
        using var input=item.Open();
        using var bytes=new MemoryStream();
        input.CopyTo(bytes);
        contentEntries.Add(new(item.FullName,bytes.ToArray()));
      }
      contentHash=QuestPackContent.ComputeHash(contentEntries);
      foreach(var item in contentEntries){
        var json=System.Text.Encoding.UTF8.GetString(item.Value);
        var compiled=production?ExperienceCompiler.CompileProductionJson(json):ExperienceCompiler.CompileJson(json,events);
        errors.AddRange(compiled.Diagnostics);
        if(compiled.Document!=null)documents.Add(compiled.Document);
        if(!string.IsNullOrWhiteSpace(compiled.Document?.Title))titles.Add(compiled.Document.Title);
      }
      ValidatePackReferences(documents,errors);
      if(experiences.Length==0)errors.Add(new("pack.experiences","$","Pack has no experience documents."));
      if(manifest==null||manifest.Schema!="comfy-quest-pack/v2"||string.IsNullOrWhiteSpace(manifest.PackId)||!SemanticVersion.TryParse(manifest.Version,out _))
        errors.Add(new("pack.manifest","$.manifest","Pack schema, id, and semantic version are required."));
      if(manifest!=null&&!string.IsNullOrWhiteSpace(manifest.ContentHash)&&!string.Equals(manifest.ContentHash,contentHash,StringComparison.OrdinalIgnoreCase))
        errors.Add(new("pack.hash","$.manifest.content_hash","Declared canonical content hash does not match experience documents."));
    }catch(Exception e){errors.Add(new("pack.read","$",e.Message));}
    return new(){Path=full,Manifest=manifest,Sha256=sha,ContentHash=contentHash,Lane=lane,Diagnostics=errors,Titles=titles};
  }

  static void ValidatePackReferences(IReadOnlyList<ExperienceDocument> documents,List<ContractDiagnostic> errors){
    var unique=documents.Where(value=>value!=null&&!string.IsNullOrWhiteSpace(value.Id))
      .GroupBy(value=>value.Id,StringComparer.Ordinal).ToArray();
    foreach(var duplicate in unique.Where(group=>group.Count()!=1))
      errors.Add(new("prerequisite.experience_duplicate","$.experiences","Experience ids must be unique within a pack."));
    var byId=unique.Where(group=>group.Count()==1).ToDictionary(group=>group.Key,group=>group.Single(),StringComparer.Ordinal);
    foreach(var document in byId.Values)
      foreach(var prerequisite in document.Prerequisites??new List<string>())
        if(!byId.ContainsKey(prerequisite))
          errors.Add(new("prerequisite.missing","$.experiences."+document.Id+".prerequisites","Prerequisite '"+prerequisite+"' is not present in this pack."));
    foreach(var document in byId.Values)
      foreach(var successor in document.SuccessorExperienceIds??new List<string>())
        if(!byId.ContainsKey(successor))
          errors.Add(new("successor.missing","$.experiences."+document.Id+".successor_experience_ids","Successor '"+successor+"' is not present in this pack."));
    var state=new Dictionary<string,int>(StringComparer.Ordinal);
    foreach(var id in byId.Keys.OrderBy(value=>value,StringComparer.Ordinal))Visit(id);

    var successorState=new Dictionary<string,int>(StringComparer.Ordinal);
    foreach(var id in byId.Keys.OrderBy(value=>value,StringComparer.Ordinal))VisitSuccessor(id);

    void Visit(string id){
      if(state.TryGetValue(id,out var known)){if(known==1)errors.Add(new("prerequisite.cycle","$.experiences."+id+".prerequisites","Prerequisite graph contains a cycle."));return;}
      state[id]=1;
      foreach(var dependency in byId[id].Prerequisites??new List<string>())if(byId.ContainsKey(dependency))Visit(dependency);
      state[id]=2;
    }

    void VisitSuccessor(string id){
      if(successorState.TryGetValue(id,out var known)){if(known==1)errors.Add(new("successor.cycle","$.experiences."+id+".successor_experience_ids","Successor graph contains a cycle."));return;}
      successorState[id]=1;
      foreach(var successor in byId[id].SuccessorExperienceIds??new List<string>())if(byId.ContainsKey(successor))VisitSuccessor(successor);
      successorState[id]=2;
    }
  }
  static bool SafeEntry(string name){if(string.IsNullOrWhiteSpace(name)||name.Contains("\\")||name.Contains("..")||Path.IsPathRooted(name))return false;if(name=="manifest.json")return true;return (name.StartsWith("experiences/",StringComparison.Ordinal)||name.StartsWith("quests/",StringComparison.Ordinal))&&name.EndsWith(".json",StringComparison.Ordinal)&&name.Count(c=>c=='/')==1;}
  public IReadOnlyList<PackCandidate> ListVersions(ISet<string> events=null)=>CheckInbox(events).Where(x=>x.IsValid).OrderByDescending(x=>SemanticVersion.Parse(x.Manifest.Version)).ThenBy(x=>x.Manifest.PackId,StringComparer.Ordinal).ToArray();
  // An already-current latest returns without re-activating: an idle repeat press must not
  // archive a fresh epoch, because ten of them would evict the entire genuine rollback history.
  public PackCandidate LoadLatest(ISet<string> events=null){var valid=ListVersions(events).ToArray();if(valid.Length==0)return null;EnsureNoCollisions(valid);ActiveSet active=null;try{active=ReadActive();}catch{}if(active!=null&&string.Equals(active.PackId,valid[0].Manifest.PackId,StringComparison.Ordinal)&&string.Equals(active.Version,valid[0].Manifest.Version,StringComparison.Ordinal)&&string.Equals(active.ContentHash,valid[0].ContentHash,StringComparison.OrdinalIgnoreCase))return valid[0];return ActivateCandidate(valid[0]);}
  public PackCandidate LoadVersion(string packId,string version,ISet<string> events=null){var valid=ListVersions(events).ToArray();EnsureNoCollisions(valid);var chosen=valid.SingleOrDefault(x=>string.Equals(x.Manifest.PackId,packId,StringComparison.Ordinal)&&string.Equals(x.Manifest.Version,version,StringComparison.Ordinal));return chosen==null?null:ActivateCandidate(chosen);}
  public PackCandidate LoadDevRevision(string source,ISet<string> events=null){if(source!=Path.GetFileName(source))throw new InvalidOperationException("dev_source_invalid");var candidate=InspectDev(Path.Combine(LaneRoot(QuestPackLane.Dev),source),events);if(!candidate.IsValid)throw new InvalidOperationException("dev_candidate_invalid");return ActivateCandidate(candidate);}
  public ActiveSet ReadActive()=>ReadActiveFile(Path.Combine(root,"active","active-set.json"),"active_set_unreadable");
  /// <summary>Every experience id the activated pack offers, for a creator choosing between them.</summary>
  public IReadOnlyList<string> ActiveExperienceIds(){var active=ReadActive();if(active==null)return Array.Empty<string>();var package=ActivePackagePath(active);if(!File.Exists(package))return Array.Empty<string>();try{using var zip=ZipFile.OpenRead(package);return ActiveExperienceResolver.Ids(zip);}catch{return Array.Empty<string>();}}
  /// <summary>Bind one experience of the already-activated pack. Selection is not an activation: it
  /// mints no activation id and archives no history, because nothing about the installed content
  /// changed — only which of its experiences Runtime answers to. Selecting what is already selected
  /// is a no-op rather than a rewrite, so a repeated request cannot churn the file the engine
  /// watches for cache invalidation.</summary>
  public ActiveSet SelectExperience(string experienceId)=>SetExperienceSelection(experienceId,null,false);
  /// <summary>Select within one exact activation. Runtime continuation uses this after its durable
  /// decision so a concurrent pack activation cannot be overwritten by an older campaign.</summary>
  public ActiveSet SelectExperienceForActivation(string expectedActivationId,string experienceId){
    if(!ValidActivationId(expectedActivationId))throw new InvalidOperationException("active_activation_id_invalid");
    if(string.IsNullOrWhiteSpace(experienceId))throw new InvalidOperationException("select_experience_id_required");
    return SetExperienceSelection(experienceId,expectedActivationId,false);
  }
  /// <summary>Undo a selector changed as one step of a failed binding transaction. The activation
  /// identity makes this incapable of overwriting a newer content activation; null restores the
  /// unselected state used by a freshly activated guild pack.</summary>
  public ActiveSet RestoreExperienceSelection(string expectedActivationId,string experienceId){
    if(!ValidActivationId(expectedActivationId))throw new InvalidOperationException("active_activation_id_invalid");
    return SetExperienceSelection(experienceId,expectedActivationId,true);
  }
  ActiveSet SetExperienceSelection(string experienceId,string expectedActivationId,bool allowEmpty){
    if(!allowEmpty&&string.IsNullOrWhiteSpace(experienceId))throw new InvalidOperationException("select_experience_id_required");
    var target=Path.Combine(root,"active","active-set.json");
    var active=ReadActiveFile(target,"active_set_unreadable");
    ValidateActive(active,"active_set_invalid");
    if(expectedActivationId!=null&&!string.Equals(active.ActivationId,expectedActivationId,StringComparison.Ordinal))
      throw new InvalidOperationException("active_activation_mismatch");
    var package=ActivePackagePath(active);
    if(!File.Exists(package))throw new InvalidOperationException("active_package_missing");
    if(!string.IsNullOrWhiteSpace(experienceId))using(var zip=ZipFile.OpenRead(package))
      if(!ActiveExperienceResolver.TryResolve(zip,experienceId,out _,out var error))throw new InvalidOperationException(error);
    if(string.Equals(active.ExperienceId,experienceId,StringComparison.Ordinal))return active;
    active.ExperienceId=experienceId;
    WriteActiveFile(target,JsonConvert.SerializeObject(active,Formatting.Indented));
    return active;
  }
  string ActivePackagePath(ActiveSet active)=>Path.Combine(LaneRoot(ParseLane(active.SourceChannel)),active.Source);
  public IReadOnlyList<ActiveSet> ActivationHistory(){var history=HistoryRoot();if(!Directory.Exists(history))return Array.Empty<ActiveSet>();return Directory.GetFiles(history,"act-*.json").Select(path=>{try{return ReadActiveFile(path,"activation_history_unreadable");}catch{return null;}}).Where(value=>value!=null&&ValidActivationId(value.ActivationId)).OrderByDescending(value=>value.ActivatedUtc).ToArray();}
  public PackCandidate Rollback(ISet<string> events=null){var active=ReadActive();if(active!=null&&ValidActivationId(active.ActivationId))return string.IsNullOrWhiteSpace(active.PreviousActivationId)?null:Rollback(active.PreviousActivationId,events);return RollbackLegacy(events);}
  public PackCandidate Rollback(string activationId,ISet<string> events=null){if(!ValidActivationId(activationId))throw new InvalidOperationException("rollback_activation_id_invalid");var previous=ReadActiveFile(Path.Combine(HistoryRoot(),activationId+".json"),"previous_active_set_unreadable");ValidateActive(previous,"previous_active_set_invalid");var lane=ParseLane(previous.SourceChannel);var candidate=InspectLane(Path.Combine(LaneRoot(lane),previous.Source),lane,events);ValidateCandidate(previous,candidate);return ActivateCandidate(candidate,previous.PreviousActivationId,true);}
  void EnsureNoCollisions(IReadOnlyList<PackCandidate> valid){var collision=valid.GroupBy(x=>x.Manifest.PackId+"\n"+x.Manifest.Version,StringComparer.Ordinal).Any(g=>g.Select(x=>x.ContentHash).Distinct(StringComparer.OrdinalIgnoreCase).Count()>1);if(collision)throw new InvalidOperationException("same_version_hash_collision");}
  PackCandidate ActivateCandidate(PackCandidate chosen,string previousActivationId=null,bool previousSpecified=false){var active=Path.Combine(root,"active");Directory.CreateDirectory(active);var target=Path.Combine(active,"active-set.json");var current=ReadActiveFile(target,"active_set_unreadable");string retainedExperienceId=null;if(current!=null){Archive(current);if(!previousSpecified)previousActivationId=current.ActivationId;
      // A guild revision is normally the same collection of experiences with new authored bytes.
      // Preserve an explicit selector only for the same pack and only when the new archive still
      // resolves that exact id once. Removed, duplicated, unreadable, or cross-pack ids clear just
      // as before, so activation never guesses which experience Runtime should answer to.
      if(string.Equals(current.PackId,chosen.Manifest.PackId,StringComparison.Ordinal)
          &&!string.IsNullOrWhiteSpace(current.ExperienceId))try{using var zip=ZipFile.OpenRead(chosen.Path);if(ActiveExperienceResolver.TryResolve(zip,current.ExperienceId,out _,out _))retainedExperienceId=current.ExperienceId;}catch{}}
    var activated=DateTimeOffset.UtcNow;var activationId="act-"+activated.ToString("yyyyMMdd'T'HHmmssfff'Z'",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8);var json=JsonConvert.SerializeObject(new ActiveSet{Schema="comfy-quest-active-set/v1",PackId=chosen.Manifest.PackId,Version=chosen.Manifest.Version,ContentHash=chosen.ContentHash,PackageSha256=chosen.Sha256,Source=Path.GetFileName(chosen.Path),ActivatedUtc=activated,ActivationId=activationId,SourceChannel=LaneName(chosen.Lane),PreviousActivationId=previousActivationId,ExperienceId=retainedExperienceId},Formatting.Indented);WriteActiveFile(target,json,Path.Combine(active,"active-set.previous.json"));return chosen;}
  PackCandidate RollbackLegacy(ISet<string> events){var previousPath=Path.Combine(root,"active","active-set.previous.json");if(!File.Exists(previousPath))return null;var previous=ReadActiveFile(previousPath,"previous_active_set_unreadable");ValidateActive(previous,"previous_active_set_invalid");var lane=ParseLane(previous.SourceChannel);var candidate=InspectLane(Path.Combine(LaneRoot(lane),previous.Source),lane,events);ValidateCandidate(previous,candidate);return ActivateCandidate(candidate,previous.PreviousActivationId,true);}
  void Archive(ActiveSet value){if(value==null||!ValidActivationId(value.ActivationId))return;var history=HistoryRoot();Directory.CreateDirectory(history);var target=Path.Combine(history,value.ActivationId+".json");if(!File.Exists(target))File.WriteAllText(target,JsonConvert.SerializeObject(value,Formatting.Indented));var readable=Directory.GetFiles(history,"act-*.json").Select(path=>{try{return new{Path=path,Value=ReadActiveFile(path,"activation_history_unreadable")};}catch{return null;}}).Where(item=>item!=null&&item.Value!=null&&ValidActivationId(item.Value.ActivationId)).OrderByDescending(item=>item.Value.ActivatedUtc).ToArray();foreach(var stale in readable.Skip(MaxActivationHistory))File.Delete(stale.Path);}
  ActiveSet ReadActiveFile(string path,string error){if(!File.Exists(path))return null;try{string json=null;for(var attempt=0;attempt<5;attempt++){try{using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);using var reader=new StreamReader(stream);json=reader.ReadToEnd();break;}catch(IOException)when(attempt<4){Thread.Sleep(10);}catch(UnauthorizedAccessException)when(attempt<4){Thread.Sleep(10);}}return JsonConvert.DeserializeObject<ActiveSet>(json);}catch(Exception e){throw new InvalidOperationException(error,e);}}
  static void WriteActiveFile(string target,string json,string backup=null){var temp=target+".tmp-"+Guid.NewGuid().ToString("N");try{File.WriteAllText(temp,json);for(var attempt=0;;attempt++){try{if(File.Exists(target))File.Replace(temp,target,backup);else File.Move(temp,target);return;}catch(IOException)when(attempt<4){Thread.Sleep(10);}catch(UnauthorizedAccessException)when(attempt<4){Thread.Sleep(10);}}}finally{try{if(File.Exists(temp))File.Delete(temp);}catch{}}}
  void ValidateActive(ActiveSet value,string error){if(value==null||value.Schema!="comfy-quest-active-set/v1"||value.Source!=Path.GetFileName(value.Source))throw new InvalidOperationException(error);}
  static void ValidateCandidate(ActiveSet expected,PackCandidate candidate){if(!candidate.IsValid||candidate.Manifest.PackId!=expected.PackId||candidate.Manifest.Version!=expected.Version||!string.Equals(candidate.ContentHash,expected.ContentHash,StringComparison.OrdinalIgnoreCase)||!string.Equals(candidate.Sha256,expected.PackageSha256,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("previous_active_content_mismatch");}
  string LaneRoot(QuestPackLane lane)=>Path.Combine(root,lane==QuestPackLane.Dev?"inbox-dev":"inbox");
  string HistoryRoot()=>Path.Combine(root,"active","history");
  static string LaneName(QuestPackLane lane)=>lane==QuestPackLane.Dev?"dev":"production";
  static QuestPackLane ParseLane(string value)=>string.Equals(value,"dev",StringComparison.OrdinalIgnoreCase)?QuestPackLane.Dev:QuestPackLane.Production;
  static bool ValidActivationId(string value){if(string.IsNullOrWhiteSpace(value)||value.Length!=32||!value.StartsWith("act-",StringComparison.Ordinal)||value[12]!='T'||value[22]!='Z'||value[23]!='-')return false;return value.Skip(4).Take(8).All(char.IsDigit)&&value.Skip(13).Take(9).All(char.IsDigit)&&value.Skip(24).All(c=>(c>='0'&&c<='9')||(c>='a'&&c<='f'));}
  static string HashFile(string p){using var s=File.OpenRead(p);using var h=SHA256.Create();return BitConverter.ToString(h.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
}

public static class QuestPackContent {
  public static string ComputeHash(IEnumerable<KeyValuePair<string,byte[]>> entries){using var content=new MemoryStream();foreach(var item in entries.OrderBy(x=>x.Key,StringComparer.Ordinal)){var name=System.Text.Encoding.UTF8.GetBytes(item.Key+"\n");content.Write(name,0,name.Length);content.Write(item.Value,0,item.Value.Length);}content.Position=0;using var hasher=SHA256.Create();return BitConverter.ToString(hasher.ComputeHash(content)).Replace("-","").ToLowerInvariant();}
}

public readonly struct SemanticVersion : IComparable<SemanticVersion>{public readonly int Major,Minor,Patch;SemanticVersion(int a,int b,int c){Major=a;Minor=b;Patch=c;}public static bool TryParse(string s,out SemanticVersion v){v=default;var p=s?.Split('.');if(p?.Length!=3||!int.TryParse(p[0],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var a)||!int.TryParse(p[1],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var b)||!int.TryParse(p[2],System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var c)||a<0||b<0||c<0||!string.Equals(s,$"{a}.{b}.{c}",StringComparison.Ordinal))return false;v=new(a,b,c);return true;}public static SemanticVersion Parse(string s)=>TryParse(s,out var v)?v:throw new FormatException("Invalid semantic version.");public int CompareTo(SemanticVersion o){var x=Major.CompareTo(o.Major);if(x!=0)return x;x=Minor.CompareTo(o.Minor);return x!=0?x:Patch.CompareTo(o.Patch);}}
