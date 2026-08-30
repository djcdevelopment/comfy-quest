namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class ExperienceSchema {
  public const string Id = "comfy-quest-experience/v2";
  public const int MaxDocumentBytes = 1024 * 1024;
  public const int MaxStages = 64;
  public const int MaxTriggerLeaves = 128;
  public const int MaxActions = 256;
  public const int MaxExpressionDepth = 3;
  public const int MaxPrerequisites = 64;
  public const int MaxSuccessors = 16;
  public const string TimerElapsedEvent = "timer_elapsed";
  public const string ExperienceStartedEvent = "experience_started";
  public const string ChatReceivedEvent = CooperativeEventContract.ChatReceivedEvent;
  public const string PlayerDiedEvent = ProgressionEventContract.PlayerDiedEvent;
}

public sealed class ExperienceDocument {
  [JsonProperty("schema")] public string Schema { get; set; }
  [JsonProperty("id")] public string Id { get; set; }
  [JsonProperty("title")] public string Title { get; set; }
  [JsonProperty("entry_stage")] public string EntryStage { get; set; }
  [JsonProperty("stages")] public List<ExperienceStage> Stages { get; set; }
  [JsonProperty("bindings")] public List<ExperienceBinding> Bindings { get; set; }
  [JsonProperty("spatial_areas", NullValueHandling=NullValueHandling.Ignore)] public List<SpatialArea> SpatialAreas { get; set; }
  [JsonProperty("prerequisites", NullValueHandling=NullValueHandling.Ignore)] public List<string> Prerequisites { get; set; }
  /// <summary>Authored continuation preference within this exact pack. Runtime considers the ids
  /// in order after a successful terminal outcome and starts the first eligible experience.</summary>
  [JsonProperty("successor_experience_ids", NullValueHandling=NullValueHandling.Ignore)] public List<string> SuccessorExperienceIds { get; set; }
}

/// <summary>A first-class Runtime v2 sphere. The frame owns resolution semantics; triggers only
/// name this area so radius and provenance cannot drift between routes.</summary>
public sealed class SpatialArea {
  [JsonProperty("id")] public string Id { get; set; }
  [JsonProperty("shape")] public string Shape { get; set; } = "sphere";
  [JsonProperty("frame")] public string Frame { get; set; }
  [JsonProperty("center", NullValueHandling=NullValueHandling.Ignore)] public SpatialContractPoint Center { get; set; }
  [JsonProperty("radius_meters")] public double RadiusMeters { get; set; }
  [JsonProperty("source_anchor", NullValueHandling=NullValueHandling.Ignore)] public SpatialAreaSource SourceAnchor { get; set; }
}

public sealed class SpatialAreaSource {
  [JsonProperty("anchor_id")] public string AnchorId { get; set; }
  [JsonProperty("content_sha256")] public string ContentSha256 { get; set; }
  [JsonProperty("snapshot")] public SpatialSnapshotReference Snapshot { get; set; }
  [JsonProperty("piece")] public SpatialPieceReference Piece { get; set; }
  [JsonProperty("producer")] public SpatialProducerReference Producer { get; set; }
}

public sealed class ExperienceStage {
  [JsonProperty("id")] public string Id { get; set; }
  [JsonProperty("entry_actions")] public List<ExperienceAction> EntryActions { get; set; }
  [JsonProperty("transitions")] public List<ExperienceTransition> Transitions { get; set; }
}

public sealed class ExperienceTransition {
  [JsonProperty("id")] public string Id { get; set; }
  [JsonProperty("priority")] public int Priority { get; set; }
  [JsonProperty("when")] public TriggerExpression When { get; set; }
  [JsonProperty("actions")] public List<ExperienceAction> Actions { get; set; }
  [JsonProperty("next_stage")] public string NextStage { get; set; }
  [JsonProperty("outcome")] public string Outcome { get; set; }
}

public sealed class TriggerExpression {
  [JsonProperty("op")] public string Op { get; set; }
  [JsonProperty("event")] public string Event { get; set; }
  [JsonProperty("target")] public string Target { get; set; }
  [JsonProperty("where")] public Dictionary<string,string> Where { get; set; }
  [JsonProperty("count")] public int? Count { get; set; }
  [JsonProperty("within_seconds")] public int? WithinSeconds { get; set; }
  [JsonProperty("measure", NullValueHandling=NullValueHandling.Ignore)] public string Measure { get; set; }
  [JsonProperty("comparison", NullValueHandling=NullValueHandling.Ignore)] public string Comparison { get; set; }
  [JsonProperty("value", NullValueHandling=NullValueHandling.Ignore)] public int? Value { get; set; }
  [JsonProperty("spatial", NullValueHandling=NullValueHandling.Ignore)] public string Spatial { get; set; }
  [JsonProperty("area_id", NullValueHandling=NullValueHandling.Ignore)] public string AreaId { get; set; }
  [JsonProperty("action_id", NullValueHandling=NullValueHandling.Ignore)] public string ActionId { get; set; }
  [JsonProperty("children")] public List<TriggerExpression> Children { get; set; }
}

public sealed class AdaptiveMeasureDefinition {
  public AdaptiveMeasureDefinition(string name,string label,string unit,string unitSingular,int minimum,int maximum,int defaultValue,string sourceEvent,bool requiresSpawnAction,string palette){Name=name;Label=label;Unit=unit;UnitSingular=unitSingular;Minimum=minimum;Maximum=maximum;DefaultValue=defaultValue;SourceEvent=sourceEvent;RequiresSpawnAction=requiresSpawnAction;Palette=palette;}
  public string Name { get; }
  public string Label { get; }
  public string Unit { get; }
  public string UnitSingular { get; }
  public int Minimum { get; }
  public int Maximum { get; }
  public int DefaultValue { get; }
  /// <summary>Normalized event this measure counts, so activation subscribes it even without an authored EVENT clause.</summary>
  public string SourceEvent { get; }
  /// <summary>True when the measure counts objects staged by one authored spawn action and must name it.</summary>
  public bool RequiresSpawnAction { get; }
  public string Palette { get; }
  public string UnitFor(double value)=>value==1d&&UnitSingular!=null?UnitSingular:Unit;
}

/// <summary>Closed, advanced-only measures backed by persisted workflow facts and this workflow's spawn ledger.</summary>
public static class AdaptiveMeasureCatalog {
  static readonly AdaptiveMeasureDefinition[] Definitions = {
    new("time_since_stage_entered","Time in this stage","seconds","second",1,86400,30,null,false,"extended"),
    new("time_since_progress","Time since quest progress","seconds","second",1,86400,30,null,false,"extended"),
    new("player_deaths_in_stage","Player deaths in this stage","deaths","death",1,64,1,ExperienceSchema.PlayerDiedEvent,false,"extended"),
    new("spawned_enemies_remaining","Staged objects still present","enemies","enemy",1,128,1,null,true,"extended"),
    new("spawned_enemies_cleared","Staged objects no longer present","enemies","enemy",1,128,1,null,true,"extended")
  };
  public const string RemainingMeasure = "spawned_enemies_remaining";
  public const string ClearedMeasure = "spawned_enemies_cleared";
  public const string DeathsMeasure = "player_deaths_in_stage";
  public static IReadOnlyList<AdaptiveMeasureDefinition> All => Definitions;
  public static bool TryGet(string name,out AdaptiveMeasureDefinition definition){definition=Definitions.FirstOrDefault(x=>string.Equals(x.Name,name,StringComparison.Ordinal));return definition!=null;}
}

public sealed class SpatialPredicateDefinition {
  public SpatialPredicateDefinition(string name,string label,bool requiresValue,int valueMinimum,int valueMaximum,string valueUnit,bool allowsPlayerAnchor,string palette){Name=name;Label=label;RequiresValue=requiresValue;ValueMinimum=valueMinimum;ValueMaximum=valueMaximum;ValueUnit=valueUnit;AllowsPlayerAnchor=allowsPlayerAnchor;Palette=palette;}
  public string Name { get; }
  public string Label { get; }
  public bool RequiresValue { get; }
  public int ValueMinimum { get; }
  public int ValueMaximum { get; }
  public string ValueUnit { get; }
  public bool AllowsPlayerAnchor { get; }
  public string Palette { get; }
}

/// <summary>Closed, advanced-only spatial predicates evaluated from observed positions. The player
/// anchor is admitted only where the player is not already the predicate's subject.</summary>
public static class SpatialPredicateCatalog {
  public const int RadiusMinimum = 1;
  public const int RadiusMaximum = 100;
  public const int MaxAnchors = 32;
  public const double MaxWorldCoordinate = 10500;
  static readonly SpatialPredicateDefinition[] Definitions = {
    new("within_radius","Player within radius of an anchor",false,0,0,null,false,"extended"),
    new("entered","Player entered an area",false,0,0,null,false,"extended"),
    new("left","Player left an area",false,0,0,null,false,"extended"),
    new("remained","Player remained in an area",true,1,86400,"seconds",false,"extended"),
    new("count_in_area","Tracked spawned objects in an area",true,1,128,"objects",true,"extended")
  };
  public static IReadOnlyList<SpatialPredicateDefinition> All => Definitions;
  public static bool TryGet(string name,out SpatialPredicateDefinition definition){definition=Definitions.FirstOrDefault(x=>string.Equals(x.Name,name,StringComparison.Ordinal));return definition!=null;}
}

public sealed class ExperienceAction {
  [JsonProperty("id")] public string Id { get; set; }
  [JsonProperty("type")] public string Type { get; set; }
  [JsonExtensionData] public IDictionary<string,JToken> Parameters { get; set; }
}

public sealed class ExperienceBinding {
  [JsonProperty("id")] public string Id { get; set; }
  [JsonProperty("experience_id")] public string ExperienceId { get; set; }
  [JsonProperty("target_kinds")] public List<string> TargetKinds { get; set; }
}

public sealed class ContractDiagnostic {
  public ContractDiagnostic(string code, string path, string message) { Code=code; Path=path; Message=message; }
  public string Code { get; }
  public string Path { get; }
  public string Message { get; }
}

public sealed class CompiledExperience {
  internal CompiledExperience(ExperienceDocument document, IReadOnlyList<ContractDiagnostic> diagnostics) { Document=document; Diagnostics=diagnostics; }
  public ExperienceDocument Document { get; }
  public IReadOnlyList<ContractDiagnostic> Diagnostics { get; }
  public bool IsValid => Diagnostics.Count == 0;
}

public static class ExperienceCompiler {
  static readonly HashSet<string> Ops = new(StringComparer.OrdinalIgnoreCase) { "EVENT", "ANY", "ALL", "COUNT", "SEQUENCE", "THRESHOLD", "SPATIAL" };
  /// <summary>Exactly the actions Runtime executes. An action that compiles but has no executor
  /// fails its transition forever rather than visibly, so the registry admits only what ships.
  /// Terminal results are authored as a transition outcome, not as an action.</summary>
  static readonly HashSet<string> Actions = new(StringComparer.OrdinalIgnoreCase) { "message", "timer_start", "timer_cancel", "grant_item", "spawn", "clear_spawned" };

  public static CompiledExperience CompileJson(string json, ISet<string> canonicalEvents = null) {
    return Compile(json, canonicalEvents, false);
  }

  /// <summary>Compile against only events whose shipping Runtime adapter is implemented.</summary>
  public static CompiledExperience CompileProductionJson(string json) {
    return Compile(json, RuntimeProductionEventCatalog.CreateSet(), true);
  }

  static CompiledExperience Compile(
      string json, ISet<string> canonicalEvents, bool productionConstraints) {
    var errors = new List<ContractDiagnostic>();
    if (json == null || System.Text.Encoding.UTF8.GetByteCount(json) > ExperienceSchema.MaxDocumentBytes)
      return new CompiledExperience(null, new[] { new ContractDiagnostic("document.size", "$", "Document must be present and no larger than 1 MiB.") });
    ExperienceDocument doc;
    try {
      var token=JToken.Parse(json,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});
      var unknown=FindUnknownMember(token);
      if(unknown!=null)return new CompiledExperience(null,new[]{unknown});
      doc=token.ToObject<ExperienceDocument>();
    }
    catch (Exception e) { return new CompiledExperience(null, new[] { new ContractDiagnostic("document.json", "$", e.Message) }); }
    Validate(doc, errors, canonicalEvents, productionConstraints);
    return new CompiledExperience(doc, errors);
  }

  static void Validate(ExperienceDocument d, List<ContractDiagnostic> e, ISet<string> events,
      bool productionConstraints) {
    if (d == null) { e.Add(new("document.empty", "$", "Experience is empty.")); return; }
    if (d.Schema != ExperienceSchema.Id) e.Add(new("schema.unsupported", "$.schema", $"Expected {ExperienceSchema.Id}."));
    if (string.IsNullOrWhiteSpace(d.Id)) e.Add(new("id.required", "$.id", "A stable experience id is required."));
    var prerequisites = d.Prerequisites ?? new();
    if (prerequisites.Count > ExperienceSchema.MaxPrerequisites)
      e.Add(new("prerequisites.bounds", "$.prerequisites", "At most 64 prerequisite experience ids are allowed."));
    var prerequisiteIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var prerequisite in prerequisites) {
      if (!Stable(prerequisite) || !prerequisiteIds.Add(prerequisite))
        e.Add(new("prerequisite.invalid", "$.prerequisites", "Prerequisite experience ids must be unique stable identifiers."));
      else if (string.Equals(prerequisite, d.Id, StringComparison.Ordinal))
        e.Add(new("prerequisite.self", "$.prerequisites", "An experience cannot require itself."));
    }
    var successors = d.SuccessorExperienceIds ?? new();
    if (successors.Count > ExperienceSchema.MaxSuccessors)
      e.Add(new("successors.bounds", "$.successor_experience_ids", "At most 16 successor experience ids are allowed."));
    var successorIds = new HashSet<string>(StringComparer.Ordinal);
    foreach (var successor in successors) {
      if (!Stable(successor) || !successorIds.Add(successor))
        e.Add(new("successor.invalid", "$.successor_experience_ids", "Successor experience ids must be unique stable identifiers."));
      else if (string.Equals(successor, d.Id, StringComparison.Ordinal))
        e.Add(new("successor.self", "$.successor_experience_ids", "An experience cannot continue to itself."));
    }
    var stages = d.Stages ?? new();
    if (stages.Count == 0 || stages.Count > ExperienceSchema.MaxStages) e.Add(new("stages.bounds", "$.stages", "An experience requires 1..64 stages."));
    var ids = new HashSet<string>(StringComparer.Ordinal);
    var stageIds = new HashSet<string>(stages.Where(x=>x!=null).Select(x=>x.Id).Where(x=>!string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal);
    if (!stageIds.Contains(d.EntryStage)) e.Add(new("entry.missing", "$.entry_stage", "Entry stage does not exist."));
    var areas = d.SpatialAreas ?? new();
    var areaIds = new HashSet<string>(StringComparer.Ordinal);
    if (areas.Count > SpatialPredicateCatalog.MaxAnchors) e.Add(new("spatial_areas.bounds", "$.spatial_areas", "At most 32 spatial areas are allowed."));
    foreach (var area in areas) ValidateArea(area,ids,areaIds,e);
    int leaves=0, actions=0;
    var spawnActionIds = new HashSet<string>(SpawnActionIds(stages), StringComparer.Ordinal);
    var edges = new Dictionary<string,List<string>>(StringComparer.Ordinal);
    foreach (var s in stages) {
      if (s == null || !TakeId(ids,s?.Id)) e.Add(new("id.duplicate", "$.stages", "Stage, transition, binding, and action ids must be unique and non-empty."));
      if (s == null) continue;
      edges[s.Id] = new(); actions += ValidateActions(s.EntryActions, ids, e, $"$.stages.{s.Id}.entry_actions");
      foreach (var t in (s.Transitions ?? new()).OrderByDescending(x=>x.Priority).ThenBy(x=>x.Id,StringComparer.Ordinal)) {
        if (!TakeId(ids,t.Id)) e.Add(new("id.duplicate", $"$.stages.{s.Id}.transitions", "Duplicate or empty transition id."));
        ValidateExpr(t.When,1,ref leaves,e,$"$.stages.{s.Id}.transitions.{t.Id}.when",events,
            productionConstraints,areaIds,spawnActionIds);
        if(t.When!=null&&!ContainsEvent(t.When))e.Add(new("trigger.event_driver",$"$.stages.{s.Id}.transitions.{t.Id}.when","Adaptive triggers require at least one event clause to drive evaluation."));
        actions += ValidateActions(t.Actions,ids,e,$"$.stages.{s.Id}.transitions.{t.Id}.actions");
        bool terminal = t.Outcome=="complete" || t.Outcome=="fail";
        if ((string.IsNullOrWhiteSpace(t.NextStage) ? 0 : 1) + (terminal ? 1 : 0) != 1) e.Add(new("transition.destination", $"$.stages.{s.Id}.transitions.{t.Id}", "Specify exactly one next_stage or terminal outcome."));
        if (!string.IsNullOrWhiteSpace(t.NextStage)) { if (!stageIds.Contains(t.NextStage)) e.Add(new("transition.stage_missing", $"$.stages.{s.Id}.transitions.{t.Id}.next_stage", "Next stage does not exist.")); else edges[s.Id].Add(t.NextStage); }
      }
    }
    foreach(var b in d.Bindings ?? new()){if(!TakeId(ids,b?.Id))e.Add(new("id.duplicate","$.bindings","Duplicate or empty binding id."));foreach(var kind in b?.TargetKinds??new())if(kind!="player_built_piece"&&kind!="sign"&&kind!="item_stand"&&kind!="dedicated_charm")e.Add(new("binding.target_kind","$.bindings."+b.Id+".target_kinds","Binding target kind is not in the closed Charm registry."));}
    if(leaves>ExperienceSchema.MaxTriggerLeaves)e.Add(new("triggers.bounds","$.stages","At most 128 trigger leaves are allowed."));
    if(actions>ExperienceSchema.MaxActions)e.Add(new("actions.bounds","$.stages","At most 256 actions are allowed."));
    DetectCycles(edges,e);
  }
  static bool TakeId(HashSet<string> ids,string id)=>!string.IsNullOrWhiteSpace(id)&&ids.Add(id);
  static ContractDiagnostic FindUnknownMember(JToken token){
    if(token is not JObject root)return null;
    ContractDiagnostic found;
    if((found=Unknown(root,new[]{"schema","id","title","entry_stage","stages","bindings","spatial_areas","prerequisites","successor_experience_ids"},"$"))!=null)return found;
    foreach(var area in Objects(root["spatial_areas"])){
      var path="$.spatial_areas."+(area.Value<string>("id")??"area");
      if((found=Unknown(area,new[]{"id","shape","frame","center","radius_meters","source_anchor"},path))!=null)return found;
      if((found=Unknown(area["center"] as JObject,new[]{"x","y","z"},path+".center"))!=null)return found;
      if(area["source_anchor"] is JObject source){
        if((found=Unknown(source,new[]{"anchor_id","content_sha256","snapshot","piece","producer"},path+".source_anchor"))!=null)return found;
        if((found=Unknown(source["snapshot"] as JObject,new[]{"snapshot_id","world_id","file_sha256"},path+".source_anchor.snapshot"))!=null)return found;
        if((found=Unknown(source["piece"] as JObject,new[]{"zdo_index","prefab","position"},path+".source_anchor.piece"))!=null)return found;
        if((found=Unknown(source["piece"]?["position"] as JObject,new[]{"x","y","z"},path+".source_anchor.piece.position"))!=null)return found;
        if((found=Unknown(source["producer"] as JObject,new[]{"repository","revision"},path+".source_anchor.producer"))!=null)return found;
      }
    }
    foreach(var stage in Objects(root["stages"])){
      var stagePath="$.stages."+(stage.Value<string>("id")??"stage");
      if((found=Unknown(stage,new[]{"id","entry_actions","transitions"},stagePath))!=null)return found;
      foreach(var action in Objects(stage["entry_actions"]))if((found=UnknownAction(action,stagePath+".entry_actions"))!=null)return found;
      foreach(var transition in Objects(stage["transitions"])){
        var transitionPath=stagePath+".transitions."+(transition.Value<string>("id")??"transition");
        if((found=Unknown(transition,new[]{"id","priority","when","actions","next_stage","outcome"},transitionPath))!=null)return found;
        if((found=UnknownTrigger(transition["when"] as JObject,transitionPath+".when"))!=null)return found;
        foreach(var action in Objects(transition["actions"]))if((found=UnknownAction(action,transitionPath+".actions"))!=null)return found;
      }
    }
    foreach(var binding in Objects(root["bindings"]))if((found=Unknown(binding,new[]{"id","experience_id","target_kinds"},"$.bindings"))!=null)return found;
    return null;
  }
  static ContractDiagnostic UnknownTrigger(JObject value,string path){
    if(value==null)return null;
    var found=Unknown(value,new[]{"op","event","target","where","count","within_seconds","measure","comparison","value","spatial","area_id","action_id","children"},path);
    if(found!=null)return found;
    var index=0;foreach(var child in Objects(value["children"])){
      found=UnknownTrigger(child,path+".children["+(index++)+"]");if(found!=null)return found;
    }
    return null;
  }
  static ContractDiagnostic UnknownAction(JObject value,string path)=>Unknown(value,new[]{"id","type","text","timer_id","seconds","item","quantity","kind","prefab","count","radius","action_id"},path+"."+(value?.Value<string>("id")??"action"));
  static ContractDiagnostic Unknown(JObject value,IEnumerable<string> allowed,string path){
    if(value==null)return null;var names=new HashSet<string>(allowed,StringComparer.Ordinal);
    var property=value.Properties().FirstOrDefault(item=>!names.Contains(item.Name));
    return property==null?null:new ContractDiagnostic("document.unknown_member",path+"."+property.Name,"Unknown member '"+property.Name+"' is not part of Runtime Experience v2.");
  }
  static IEnumerable<JObject> Objects(JToken value)=>(value as JArray)?.OfType<JObject>()??Enumerable.Empty<JObject>();
  static IEnumerable<string> SpawnActionIds(List<ExperienceStage> stages)=>(stages??new()).Where(s=>s!=null).SelectMany(s=>(s.EntryActions??new()).Concat((s.Transitions??new()).SelectMany(t=>t?.Actions??new()))).Where(a=>a!=null&&string.Equals(a.Type,"spawn",StringComparison.OrdinalIgnoreCase)&&!string.IsNullOrWhiteSpace(a.Id)).Select(a=>a.Id);
  static int ValidateActions(List<ExperienceAction> list,HashSet<string> ids,List<ContractDiagnostic> e,string path){int n=0;foreach(var a in list??new()){n++;var actionPath=path+"."+(a?.Id??n.ToString());if(!TakeId(ids,a?.Id))e.Add(new("id.duplicate",path,"Duplicate or empty action id."));if(a==null||!Actions.Contains(a.Type??"")){if(a!=null)e.Add(new("action.unsupported",actionPath,$"Action '{a.Type}' is not in the v2 registry."));continue;}ValidateActionParameters(a,e,actionPath);}return n;}
  static void ValidateActionParameters(ExperienceAction a,List<ContractDiagnostic> e,string path){var p=a.Parameters??new Dictionary<string,JToken>();var type=a.Type.ToLowerInvariant();HashSet<string> allowed;switch(type){case "message":allowed=new(StringComparer.Ordinal){"text"};RequireString(p,"text",1,500,e,path);break;case "timer_start":allowed=new(StringComparer.Ordinal){"timer_id","seconds"};RequireStable(p,"timer_id",e,path);RequireInt(p,"seconds",1,86400,e,path);break;case "timer_cancel":allowed=new(StringComparer.Ordinal){"timer_id"};RequireStable(p,"timer_id",e,path);break;case "grant_item":allowed=new(StringComparer.Ordinal){"item","quantity"};RequireStable(p,"item",e,path);RequireInt(p,"quantity",1,100,e,path);if(p.TryGetValue("item",out var item)&&p.TryGetValue("quantity",out var quantity)&&item.Type==JTokenType.String&&quantity.Type==JTokenType.Integer&&!MutationRegistry.TryGrant(item.Value<string>(),quantity.Value<int>(),out var grantError))e.Add(new(grantError,path,"Grant item or quantity is outside the reviewed v1 registry."));break;case "spawn":allowed=new(StringComparer.Ordinal){"kind","prefab","count","radius"};RequireEnum(p,"kind",new[]{"creature","item","piece"},e,path);RequireStable(p,"prefab",e,path);RequireInt(p,"count",1,16,e,path);RequireInt(p,"radius",0,30,e,path);if(p.TryGetValue("kind",out var kind)&&p.TryGetValue("prefab",out var prefab)&&kind.Type==JTokenType.String&&prefab.Type==JTokenType.String&&!MutationRegistry.CanSpawn(kind.Value<string>(),prefab.Value<string>()))e.Add(new("spawn_prefab_not_allowlisted",path,"Spawn kind and prefab are outside the reviewed v1 registry."));break;case "clear_spawned":allowed=new(StringComparer.Ordinal){"action_id"};RequireStable(p,"action_id",e,path);break;default:return;}foreach(var key in p.Keys)if(!allowed.Contains(key))e.Add(new("action.parameter_unknown",path+"."+key,"Parameter is not allowed for this action type."));}
  static void RequireStable(IDictionary<string,JToken> p,string key,List<ContractDiagnostic> e,string path){if(!p.TryGetValue(key,out var token)||token.Type!=JTokenType.String||!Stable(token.Value<string>()))e.Add(new("action.parameter",path+"."+key,"A stable identifier is required."));}
  static void RequireString(IDictionary<string,JToken> p,string key,int min,int max,List<ContractDiagnostic> e,string path){if(!p.TryGetValue(key,out var token)||token.Type!=JTokenType.String||token.Value<string>().Length<min||token.Value<string>().Length>max)e.Add(new("action.parameter",path+"."+key,$"Text length must be {min}..{max}."));}
  static void OptionalString(IDictionary<string,JToken> p,string key,int max,List<ContractDiagnostic> e,string path){if(p.TryGetValue(key,out var token)&&(token.Type!=JTokenType.String||token.Value<string>().Length>max))e.Add(new("action.parameter",path+"."+key,$"Text length must be at most {max}."));}
  static void RequireInt(IDictionary<string,JToken> p,string key,int min,int max,List<ContractDiagnostic> e,string path){if(!p.TryGetValue(key,out var token)||token.Type!=JTokenType.Integer||token.Value<long>()<min||token.Value<long>()>max)e.Add(new("action.parameter",path+"."+key,$"Integer must be {min}..{max}."));}
  static void RequireEnum(IDictionary<string,JToken> p,string key,IEnumerable<string> values,List<ContractDiagnostic> e,string path){if(!p.TryGetValue(key,out var token)||token.Type!=JTokenType.String||!values.Contains(token.Value<string>(),StringComparer.Ordinal))e.Add(new("action.parameter",path+"."+key,"Value is not in the closed registry."));}
  static bool Stable(string value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=64&&value.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_'||c=='$');
  static bool ProductionWhereValue(string eventName,string key,string value){if(string.IsNullOrWhiteSpace(value)||value.Length>128)return false;if(string.Equals(key,"projectile",StringComparison.OrdinalIgnoreCase))return value=="true";if(string.Equals(key,"actor_role",StringComparison.OrdinalIgnoreCase))return value==CooperativeEventContract.PeerRole||value==CooperativeEventContract.ListenHostRole;if(string.Equals(key,"timer_id",StringComparison.OrdinalIgnoreCase))return Stable(value);if(string.Equals(key,"amount",StringComparison.OrdinalIgnoreCase)){return double.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var amount)&&!double.IsNaN(amount)&&!double.IsInfinity(amount)&&amount>0;}if(string.Equals(key,"quantity",StringComparison.OrdinalIgnoreCase)){return int.TryParse(value,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var quantity)&&quantity>0;}return true;}
  static string ProductionTargetIssue(string eventName,string target){string policy=null,fixedTarget=null;IReadOnlyList<string> allowed=null;if(RuntimeProductionEventCatalog.TryGet(eventName,out var runtime)){policy=runtime.TargetPolicy;fixedTarget=runtime.FixedTarget;allowed=runtime.AllowedTargets;}else if(RuntimeProductionEventCatalog.TryGetEngine(eventName,out var engine)){policy=engine.TargetPolicy;fixedTarget=engine.FixedTarget;allowed=engine.AllowedTargets;}else return null;if(string.IsNullOrWhiteSpace(target))return null;if(target.Length>128)return "trigger.target_value";if(policy=="none")return "trigger.target_unsupported";if(policy=="fixed-output"&&!string.Equals(target,fixedTarget,StringComparison.OrdinalIgnoreCase))return "trigger.target_fixed";if(policy=="closed"&&!(allowed??Array.Empty<string>()).Contains(target,StringComparer.OrdinalIgnoreCase))return "trigger.target_value";return null;}
  static bool BoundedCoordinate(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value)&&Math.Abs(value)<=SpatialPredicateCatalog.MaxWorldCoordinate;
  static void ValidateArea(SpatialArea area,HashSet<string> ids,HashSet<string> areaIds,List<ContractDiagnostic> e){
    var path="$.spatial_areas."+(area?.Id??"area");
    if(area==null||!TakeId(ids,area.Id)){e.Add(new("id.duplicate","$.spatial_areas","Area ids must be unique stable identifiers."));return;}
    areaIds.Add(area.Id);
    if(area.Shape!="sphere")e.Add(new("spatial_area.shape",path+".shape","Runtime v2 supports sphere areas."));
    if(area.Frame is not ("world" or "binding" or "player"))e.Add(new("spatial_area.frame",path+".frame","Area frame must be world, binding, or player."));
    if(double.IsNaN(area.RadiusMeters)||double.IsInfinity(area.RadiusMeters)||area.RadiusMeters<SpatialPredicateCatalog.RadiusMinimum||area.RadiusMeters>SpatialPredicateCatalog.RadiusMaximum)e.Add(new("spatial_area.radius",path+".radius_meters","Radius must be finite and 1..100 meters."));
    if(area.Frame=="world"){
      if(area.Center==null||!BoundedCoordinate(area.Center.X)||!BoundedCoordinate(area.Center.Y)||!BoundedCoordinate(area.Center.Z))e.Add(new("spatial_area.center",path+".center","A world area requires a complete finite center inside reviewed world bounds."));
    }else if(area.Center!=null)e.Add(new("spatial_area.center_unsupported",path+".center","Dynamic binding and player areas are centered on their live origin."));
    if(area.SourceAnchor!=null){
      try{
        if(area.Frame=="player")throw new SpatialContractException("anchor_mode_invalid");
        var source=new SpatialAnchorExchange{AnchorId=area.SourceAnchor.AnchorId,Mode=area.Frame=="world"?"world":"binding_relative",Shape=area.Shape,RadiusMeters=area.RadiusMeters,
          Snapshot=area.SourceAnchor.Snapshot,Piece=area.SourceAnchor.Piece,Producer=area.SourceAnchor.Producer,ContentSha256=area.SourceAnchor.ContentSha256};
        SpatialExchangeContract.ValidateAnchor(source,true);
        if(area.Frame=="world"&&(area.Center==null||area.Center.X!=source.Piece.Position.X||area.Center.Y!=source.Piece.Position.Y||area.Center.Z!=source.Piece.Position.Z))
          throw new SpatialContractException("anchor_center_mismatch");
      }catch(SpatialContractException){e.Add(new("spatial_area.source",path+".source_anchor","Imported source provenance does not match the verified anchor contract."));}
    }
  }
  static bool Sha(string value)=>value!=null&&value.Length==64&&value.All(Uri.IsHexDigit);
  static void ValidateSpatial(TriggerExpression x,List<ContractDiagnostic> e,string path,ISet<string> areaIds){
    SpatialPredicateCatalog.TryGet(x.Spatial,out var predicate);
    if(predicate==null)e.Add(new("spatial.predicate",path+".spatial","Predicate is not in the advanced spatial registry."));
    if(!Stable(x.AreaId)||areaIds==null||!areaIds.Contains(x.AreaId))e.Add(new("spatial.area_reference",path+".area_id","The referenced spatial area does not exist."));
    if(predicate!=null){
      if(predicate.RequiresValue){if(!x.Value.HasValue||x.Value.Value<predicate.ValueMinimum||x.Value.Value>predicate.ValueMaximum)e.Add(new("spatial.value",path+".value","Value is outside the predicate's reviewed bounds."));}
      else if(x.Value.HasValue)e.Add(new("spatial.value",path+".value","This predicate does not take a value."));
    }
    if(x.Children?.Count>0)e.Add(new("spatial.children",path,"SPATIAL cannot have children."));
  }
  static void ValidateThreshold(TriggerExpression x,List<ContractDiagnostic> e,string path,ISet<string> spawnActionIds){
    if(!AdaptiveMeasureCatalog.TryGet(x.Measure,out var measure))e.Add(new("threshold.measure",path+".measure","Measure is not in the advanced adaptive registry."));
    if(!string.Equals(x.Comparison,"gte",StringComparison.Ordinal))e.Add(new("threshold.comparison",path+".comparison","Only the bounded gte comparison is currently supported."));
    if(!x.Value.HasValue||x.Value.Value<(measure?.Minimum??1)||x.Value.Value>(measure?.Maximum??86400))e.Add(new("threshold.value",path+".value","Threshold is outside the measure's reviewed bounds."));
    if(measure!=null&&measure.RequiresSpawnAction){
      if(!Stable(x.ActionId)||spawnActionIds==null||!spawnActionIds.Contains(x.ActionId))e.Add(new("threshold.action_id",path+".action_id","This measure counts one authored spawn action, which does not exist."));
    }
    else if(x.ActionId!=null)e.Add(new("threshold.action_unsupported",path+".action_id","This measure does not count a spawn action."));
    if(x.Children?.Count>0)e.Add(new("threshold.children",path,"THRESHOLD cannot have children."));
  }
  static void ValidateExpr(TriggerExpression x,int depth,ref int leaves,List<ContractDiagnostic> e,string path,ISet<string> events,bool productionConstraints,ISet<string> areaIds=null,ISet<string> spawnActionIds=null){if(x==null){e.Add(new("trigger.required",path,"Trigger expression is required."));return;}if(depth>ExperienceSchema.MaxExpressionDepth)e.Add(new("trigger.depth",path,"Expression depth exceeds three."));if(!Ops.Contains(x.Op??"")){e.Add(new("trigger.op",path,"Unsupported trigger operator."));return;}if(string.Equals(x.Op,"EVENT",StringComparison.OrdinalIgnoreCase)){leaves++;var engineTimer=string.Equals(x.Event,ExperienceSchema.TimerElapsedEvent,StringComparison.OrdinalIgnoreCase);var engineChat=string.Equals(x.Event,ExperienceSchema.ChatReceivedEvent,StringComparison.OrdinalIgnoreCase);if(string.IsNullOrWhiteSpace(x.Event)||(events!=null&&!events.Contains(x.Event)&&!engineTimer&&!engineChat))e.Add(new("event.unknown",path+".event","Event is not in the canonical catalog or engine event registry."));if(engineTimer&&(x.Where==null||!x.Where.TryGetValue("timer_id",out var timerId)||!Stable(timerId)))e.Add(new("timer.id",path+".where.timer_id","timer_elapsed requires a stable timer_id clause."));if(engineChat&&(x.Where==null||!x.Where.TryGetValue("actor_role",out var actorRole)||(actorRole!=CooperativeEventContract.PeerRole&&actorRole!=CooperativeEventContract.ListenHostRole)))e.Add(new("chat.actor_role",path+".where.actor_role","chat_received requires actor_role peer or listen_host."));if(productionConstraints){if(!engineTimer&&!engineChat&&!RuntimeProductionEventCatalog.Contains(x.Event))e.Add(new("event.not_production",path+".event","Event has creator metadata but no shipping Runtime adapter."));var targetIssue=ProductionTargetIssue(x.Event,x.Target);if(targetIssue!=null)e.Add(new(targetIssue,path+".target","Target cannot be emitted by the Runtime adapter."));foreach(var field in x.Where??new Dictionary<string,string>()){if(!RuntimeProductionEventCatalog.IsAllowedWhere(x.Event,field.Key))e.Add(new("trigger.where_unsupported",path+".where."+field.Key,"Runtime adapter does not emit this constraint field."));else if(!ProductionWhereValue(x.Event,field.Key,field.Value))e.Add(new("trigger.where_value",path+".where."+field.Key,"Constraint value is outside the Runtime field policy."));else if(RuntimeProductionEventCatalog.TryGet(x.Event,out var runtime)&&runtime.FixedWhere.TryGetValue(field.Key,out var fixedValue)&&!string.Equals(field.Value,fixedValue,StringComparison.Ordinal))e.Add(new("trigger.where_fixed",path+".where."+field.Key,"Runtime adapter emits one fixed value for this field."));}}if(x.Children?.Count>0)e.Add(new("trigger.leaf_children",path,"EVENT cannot have children."));return;}if(string.Equals(x.Op,"THRESHOLD",StringComparison.OrdinalIgnoreCase)){leaves++;ValidateThreshold(x,e,path,spawnActionIds);return;}if(string.Equals(x.Op,"SPATIAL",StringComparison.OrdinalIgnoreCase)){leaves++;ValidateSpatial(x,e,path,areaIds);return;}var children=x.Children??new();if(children.Count==0)e.Add(new("trigger.children",path,"Composite trigger requires children."));if(string.Equals(x.Op,"COUNT",StringComparison.OrdinalIgnoreCase)){if(x.Count.GetValueOrDefault()<1||x.Count>128)e.Add(new("trigger.count",path+".count","COUNT must be bounded from 1 to 128."));if(children.Count!=1||!string.Equals(children[0]?.Op,"EVENT",StringComparison.OrdinalIgnoreCase))e.Add(new("trigger.count_clause",path,"COUNT requires exactly one event clause."));}if(string.Equals(x.Op,"SEQUENCE",StringComparison.OrdinalIgnoreCase)&&children.Any(c=>!string.Equals(c?.Op,"EVENT",StringComparison.OrdinalIgnoreCase)))e.Add(new("trigger.sequence_clause",path,"SEQUENCE children must be event clauses."));if(x.WithinSeconds.HasValue&&(x.WithinSeconds<1||x.WithinSeconds>86400))e.Add(new("trigger.window",path+".within_seconds","Timing window must be 1..86400 seconds."));foreach(var c in children)ValidateExpr(c,depth+1,ref leaves,e,path+".children",events,productionConstraints,areaIds,spawnActionIds);}
  static bool ContainsEvent(TriggerExpression x)=>x!=null&&(string.Equals(x.Op,"EVENT",StringComparison.OrdinalIgnoreCase)||(x.Children??new()).Any(ContainsEvent));
  static void DetectCycles(Dictionary<string,List<string>> g,List<ContractDiagnostic> e){var state=new Dictionary<string,int>();Func<string,bool> visit=null;visit=n=>{state[n]=1;foreach(var m in g[n]){if(!state.TryGetValue(m,out var s)){if(visit(m))return true;}else if(s==1)return true;}state[n]=2;return false;};foreach(var n in g.Keys)if(!state.ContainsKey(n)&&visit(n)){e.Add(new("graph.cycle","$.stages","Experience graphs must be acyclic in v2."));return;}}
}
