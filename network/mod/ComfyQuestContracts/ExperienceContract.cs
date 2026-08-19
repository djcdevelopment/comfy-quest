namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

public static class ExperienceSchema {
  public const string Id = "comfy-quest-experience/v1";
  public const int MaxDocumentBytes = 1024 * 1024;
  public const int MaxStages = 64;
  public const int MaxTriggerLeaves = 128;
  public const int MaxActions = 256;
  public const int MaxExpressionDepth = 3;
  public const string TimerElapsedEvent = "timer_elapsed";
  public const string ChatReceivedEvent = CooperativeEventContract.ChatReceivedEvent;
}

public sealed class ExperienceDocument {
  [JsonProperty("schema")] public string Schema { get; set; }
  [JsonProperty("id")] public string Id { get; set; }
  [JsonProperty("title")] public string Title { get; set; }
  [JsonProperty("entry_stage")] public string EntryStage { get; set; }
  [JsonProperty("stages")] public List<ExperienceStage> Stages { get; set; }
  [JsonProperty("bindings")] public List<ExperienceBinding> Bindings { get; set; }
  [JsonProperty("anchors", NullValueHandling=NullValueHandling.Ignore)] public List<ExperienceAnchor> Anchors { get; set; }
}

/// <summary>A named authored position creators can reference from spatial predicates.</summary>
public sealed class ExperienceAnchor {
  [JsonProperty("id")] public string Id { get; set; }
  [JsonProperty("x")] public double X { get; set; }
  [JsonProperty("y")] public double Y { get; set; }
  [JsonProperty("z")] public double Z { get; set; }
}

/// <summary>Canonical spatial reference: an authored anchor, the bound Charm, the player, or explicit coordinates.</summary>
public sealed class AreaAnchor {
  [JsonProperty("kind")] public string Kind { get; set; }
  [JsonProperty("anchor_id", NullValueHandling=NullValueHandling.Ignore)] public string AnchorId { get; set; }
  [JsonProperty("x", NullValueHandling=NullValueHandling.Ignore)] public double? X { get; set; }
  [JsonProperty("y", NullValueHandling=NullValueHandling.Ignore)] public double? Y { get; set; }
  [JsonProperty("z", NullValueHandling=NullValueHandling.Ignore)] public double? Z { get; set; }
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
  [JsonProperty("anchor", NullValueHandling=NullValueHandling.Ignore)] public AreaAnchor Anchor { get; set; }
  [JsonProperty("radius", NullValueHandling=NullValueHandling.Ignore)] public int? Radius { get; set; }
  [JsonProperty("children")] public List<TriggerExpression> Children { get; set; }
}

public sealed class AdaptiveMeasureDefinition {
  public AdaptiveMeasureDefinition(string name,string label,string unit,int minimum,int maximum,string palette){Name=name;Label=label;Unit=unit;Minimum=minimum;Maximum=maximum;Palette=palette;}
  public string Name { get; }
  public string Label { get; }
  public string Unit { get; }
  public int Minimum { get; }
  public int Maximum { get; }
  public string Palette { get; }
}

/// <summary>Closed, advanced-only measures backed by persisted workflow facts.</summary>
public static class AdaptiveMeasureCatalog {
  static readonly AdaptiveMeasureDefinition[] Definitions = {
    new("time_since_stage_entered","Time in this stage","seconds",1,86400,"extended"),
    new("time_since_progress","Time since quest progress","seconds",1,86400,"extended")
  };
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
  static readonly HashSet<string> Actions = new(StringComparer.OrdinalIgnoreCase) { "message", "arcane_sight", "counter_set", "counter_add", "timer_start", "timer_cancel", "stage_activate", "experience_complete", "experience_fail", "grant_item", "spawn", "clear_spawned" };

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
    try { doc = JsonConvert.DeserializeObject<ExperienceDocument>(json); }
    catch (Exception e) { return new CompiledExperience(null, new[] { new ContractDiagnostic("document.json", "$", e.Message) }); }
    Validate(doc, errors, canonicalEvents, productionConstraints);
    return new CompiledExperience(doc, errors);
  }

  static void Validate(ExperienceDocument d, List<ContractDiagnostic> e, ISet<string> events,
      bool productionConstraints) {
    if (d == null) { e.Add(new("document.empty", "$", "Experience is empty.")); return; }
    if (d.Schema != ExperienceSchema.Id) e.Add(new("schema.unsupported", "$.schema", $"Expected {ExperienceSchema.Id}."));
    if (string.IsNullOrWhiteSpace(d.Id)) e.Add(new("id.required", "$.id", "A stable experience id is required."));
    var stages = d.Stages ?? new();
    if (stages.Count == 0 || stages.Count > ExperienceSchema.MaxStages) e.Add(new("stages.bounds", "$.stages", "An experience requires 1..64 stages."));
    var ids = new HashSet<string>(StringComparer.Ordinal);
    var stageIds = new HashSet<string>(stages.Where(x=>x!=null).Select(x=>x.Id).Where(x=>!string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal);
    if (!stageIds.Contains(d.EntryStage)) e.Add(new("entry.missing", "$.entry_stage", "Entry stage does not exist."));
    var anchors = d.Anchors ?? new();
    var anchorIds = new HashSet<string>(StringComparer.Ordinal);
    if (anchors.Count > SpatialPredicateCatalog.MaxAnchors) e.Add(new("anchors.bounds", "$.anchors", "At most 32 authored anchors are allowed."));
    foreach (var anchor in anchors) {
      if (anchor == null || !TakeId(ids, anchor.Id)) e.Add(new("id.duplicate", "$.anchors", "Anchor ids must be unique stable identifiers."));
      else anchorIds.Add(anchor.Id);
      if (anchor != null && !(BoundedCoordinate(anchor.X) && BoundedCoordinate(anchor.Y) && BoundedCoordinate(anchor.Z)))
        e.Add(new("anchor.coordinates", "$.anchors." + (anchor.Id ?? "anchor"), "Anchor coordinates must be finite and within the reviewed world bounds."));
    }
    int leaves=0, actions=0;
    var edges = new Dictionary<string,List<string>>(StringComparer.Ordinal);
    foreach (var s in stages) {
      if (s == null || !TakeId(ids,s?.Id)) e.Add(new("id.duplicate", "$.stages", "Stage, transition, binding, and action ids must be unique and non-empty."));
      if (s == null) continue;
      edges[s.Id] = new(); actions += ValidateActions(s.EntryActions, ids, e, $"$.stages.{s.Id}.entry_actions");
      foreach (var t in (s.Transitions ?? new()).OrderByDescending(x=>x.Priority).ThenBy(x=>x.Id,StringComparer.Ordinal)) {
        if (!TakeId(ids,t.Id)) e.Add(new("id.duplicate", $"$.stages.{s.Id}.transitions", "Duplicate or empty transition id."));
        ValidateExpr(t.When,1,ref leaves,e,$"$.stages.{s.Id}.transitions.{t.Id}.when",events,
            productionConstraints,anchorIds);
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
  static int ValidateActions(List<ExperienceAction> list,HashSet<string> ids,List<ContractDiagnostic> e,string path){int n=0;foreach(var a in list??new()){n++;var actionPath=path+"."+(a?.Id??n.ToString());if(!TakeId(ids,a?.Id))e.Add(new("id.duplicate",path,"Duplicate or empty action id."));if(a==null||!Actions.Contains(a.Type??"")){if(a!=null)e.Add(new("action.unsupported",actionPath,$"Action '{a.Type}' is not in the v1 registry."));continue;}ValidateActionParameters(a,e,actionPath);}return n;}
  static void ValidateActionParameters(ExperienceAction a,List<ContractDiagnostic> e,string path){var p=a.Parameters??new Dictionary<string,JToken>();var type=a.Type.ToLowerInvariant();HashSet<string> allowed;switch(type){case "message":allowed=new(StringComparer.Ordinal){"text"};RequireString(p,"text",1,500,e,path);break;case "arcane_sight":allowed=new(StringComparer.Ordinal){"effect"};OptionalString(p,"effect",64,e,path);break;case "counter_set":allowed=new(StringComparer.Ordinal){"counter_id","value"};RequireStable(p,"counter_id",e,path);RequireInt(p,"value",-1000000,1000000,e,path);break;case "counter_add":allowed=new(StringComparer.Ordinal){"counter_id","amount"};RequireStable(p,"counter_id",e,path);RequireInt(p,"amount",-1000000,1000000,e,path);break;case "timer_start":allowed=new(StringComparer.Ordinal){"timer_id","seconds"};RequireStable(p,"timer_id",e,path);RequireInt(p,"seconds",1,86400,e,path);break;case "timer_cancel":allowed=new(StringComparer.Ordinal){"timer_id"};RequireStable(p,"timer_id",e,path);break;case "stage_activate":allowed=new(StringComparer.Ordinal){"stage_id"};RequireStable(p,"stage_id",e,path);break;case "experience_complete":case "experience_fail":allowed=new(StringComparer.Ordinal){"reason"};OptionalString(p,"reason",200,e,path);break;case "grant_item":allowed=new(StringComparer.Ordinal){"item","quantity"};RequireStable(p,"item",e,path);RequireInt(p,"quantity",1,100,e,path);if(p.TryGetValue("item",out var item)&&p.TryGetValue("quantity",out var quantity)&&item.Type==JTokenType.String&&quantity.Type==JTokenType.Integer&&!MutationRegistry.TryGrant(item.Value<string>(),quantity.Value<int>(),out var grantError))e.Add(new(grantError,path,"Grant item or quantity is outside the reviewed v1 registry."));break;case "spawn":allowed=new(StringComparer.Ordinal){"kind","prefab","count","radius"};RequireEnum(p,"kind",new[]{"creature","item","piece"},e,path);RequireStable(p,"prefab",e,path);RequireInt(p,"count",1,16,e,path);RequireInt(p,"radius",0,30,e,path);if(p.TryGetValue("kind",out var kind)&&p.TryGetValue("prefab",out var prefab)&&kind.Type==JTokenType.String&&prefab.Type==JTokenType.String&&!MutationRegistry.CanSpawn(kind.Value<string>(),prefab.Value<string>()))e.Add(new("spawn_prefab_not_allowlisted",path,"Spawn kind and prefab are outside the reviewed v1 registry."));break;case "clear_spawned":allowed=new(StringComparer.Ordinal){"action_id"};RequireStable(p,"action_id",e,path);break;default:return;}foreach(var key in p.Keys)if(!allowed.Contains(key))e.Add(new("action.parameter_unknown",path+"."+key,"Parameter is not allowed for this action type."));}
  static void RequireStable(IDictionary<string,JToken> p,string key,List<ContractDiagnostic> e,string path){if(!p.TryGetValue(key,out var token)||token.Type!=JTokenType.String||!Stable(token.Value<string>()))e.Add(new("action.parameter",path+"."+key,"A stable identifier is required."));}
  static void RequireString(IDictionary<string,JToken> p,string key,int min,int max,List<ContractDiagnostic> e,string path){if(!p.TryGetValue(key,out var token)||token.Type!=JTokenType.String||token.Value<string>().Length<min||token.Value<string>().Length>max)e.Add(new("action.parameter",path+"."+key,$"Text length must be {min}..{max}."));}
  static void OptionalString(IDictionary<string,JToken> p,string key,int max,List<ContractDiagnostic> e,string path){if(p.TryGetValue(key,out var token)&&(token.Type!=JTokenType.String||token.Value<string>().Length>max))e.Add(new("action.parameter",path+"."+key,$"Text length must be at most {max}."));}
  static void RequireInt(IDictionary<string,JToken> p,string key,int min,int max,List<ContractDiagnostic> e,string path){if(!p.TryGetValue(key,out var token)||token.Type!=JTokenType.Integer||token.Value<long>()<min||token.Value<long>()>max)e.Add(new("action.parameter",path+"."+key,$"Integer must be {min}..{max}."));}
  static void RequireEnum(IDictionary<string,JToken> p,string key,IEnumerable<string> values,List<ContractDiagnostic> e,string path){if(!p.TryGetValue(key,out var token)||token.Type!=JTokenType.String||!values.Contains(token.Value<string>(),StringComparer.Ordinal))e.Add(new("action.parameter",path+"."+key,"Value is not in the closed registry."));}
  static bool Stable(string value)=>!string.IsNullOrWhiteSpace(value)&&value.Length<=64&&value.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_'||c=='$');
  static bool ProductionWhereValue(string eventName,string key,string value){if(string.IsNullOrWhiteSpace(value)||value.Length>128)return false;if(string.Equals(key,"projectile",StringComparison.OrdinalIgnoreCase))return value=="true";if(string.Equals(key,"actor_role",StringComparison.OrdinalIgnoreCase))return value==CooperativeEventContract.PeerRole||value==CooperativeEventContract.ListenHostRole;if(string.Equals(key,"timer_id",StringComparison.OrdinalIgnoreCase))return Stable(value);if(string.Equals(key,"amount",StringComparison.OrdinalIgnoreCase)){return double.TryParse(value,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out var amount)&&!double.IsNaN(amount)&&!double.IsInfinity(amount)&&amount>0;}if(string.Equals(key,"quantity",StringComparison.OrdinalIgnoreCase)){return int.TryParse(value,System.Globalization.NumberStyles.None,System.Globalization.CultureInfo.InvariantCulture,out var quantity)&&quantity>0;}return true;}
  static string ProductionTargetIssue(string eventName,string target){string policy=null,fixedTarget=null;IReadOnlyList<string> allowed=null;if(RuntimeProductionEventCatalog.TryGet(eventName,out var runtime)){policy=runtime.TargetPolicy;fixedTarget=runtime.FixedTarget;allowed=runtime.AllowedTargets;}else if(RuntimeProductionEventCatalog.TryGetEngine(eventName,out var engine)){policy=engine.TargetPolicy;fixedTarget=engine.FixedTarget;allowed=engine.AllowedTargets;}else return null;if(string.IsNullOrWhiteSpace(target))return null;if(target.Length>128)return "trigger.target_value";if(policy=="none")return "trigger.target_unsupported";if(policy=="fixed-output"&&!string.Equals(target,fixedTarget,StringComparison.OrdinalIgnoreCase))return "trigger.target_fixed";if(policy=="closed"&&!(allowed??Array.Empty<string>()).Contains(target,StringComparer.OrdinalIgnoreCase))return "trigger.target_value";return null;}
  static bool BoundedCoordinate(double value)=>!double.IsNaN(value)&&!double.IsInfinity(value)&&Math.Abs(value)<=SpatialPredicateCatalog.MaxWorldCoordinate;
  static void ValidateSpatial(TriggerExpression x,List<ContractDiagnostic> e,string path,ISet<string> anchorIds){
    SpatialPredicateCatalog.TryGet(x.Spatial,out var predicate);
    if(predicate==null)e.Add(new("spatial.predicate",path+".spatial","Predicate is not in the advanced spatial registry."));
    var anchor=x.Anchor;
    if(anchor==null)e.Add(new("spatial.anchor",path+".anchor","A spatial predicate requires an anchor."));
    else{
      var kind=anchor.Kind??"";
      var hasCoordinates=anchor.X.HasValue||anchor.Y.HasValue||anchor.Z.HasValue;
      if(string.Equals(kind,"authored",StringComparison.Ordinal)){
        if(!Stable(anchor.AnchorId)||anchorIds==null||!anchorIds.Contains(anchor.AnchorId))e.Add(new("spatial.anchor_reference",path+".anchor.anchor_id","The referenced authored anchor does not exist."));
        if(hasCoordinates)e.Add(new("spatial.anchor_fields",path+".anchor","An authored anchor carries only its anchor_id."));
      }
      else if(string.Equals(kind,"coordinates",StringComparison.Ordinal)){
        if(anchor.AnchorId!=null)e.Add(new("spatial.anchor_fields",path+".anchor","A coordinates anchor carries only x, y, and z."));
        if(!(anchor.X.HasValue&&anchor.Y.HasValue&&anchor.Z.HasValue&&BoundedCoordinate(anchor.X.Value)&&BoundedCoordinate(anchor.Y.Value)&&BoundedCoordinate(anchor.Z.Value)))
          e.Add(new("spatial.anchor_coordinates",path+".anchor","Coordinates must be complete, finite, and within the reviewed world bounds."));
      }
      else if(string.Equals(kind,"binding",StringComparison.Ordinal)||string.Equals(kind,"player",StringComparison.Ordinal)){
        if(anchor.AnchorId!=null||hasCoordinates)e.Add(new("spatial.anchor_fields",path+".anchor","This anchor kind carries no additional fields."));
      }
      else e.Add(new("spatial.anchor_kind",path+".anchor.kind","Anchor kind is not in the closed registry."));
      if(string.Equals(kind,"player",StringComparison.Ordinal)&&(predicate==null||!predicate.AllowsPlayerAnchor))
        e.Add(new("spatial.anchor_player",path+".anchor.kind","The player anchor applies only to predicates whose subject is not the player."));
    }
    if(!x.Radius.HasValue||x.Radius.Value<SpatialPredicateCatalog.RadiusMinimum||x.Radius.Value>SpatialPredicateCatalog.RadiusMaximum)
      e.Add(new("spatial.radius",path+".radius","Radius must be 1..100 whole meters."));
    if(predicate!=null){
      if(predicate.RequiresValue){if(!x.Value.HasValue||x.Value.Value<predicate.ValueMinimum||x.Value.Value>predicate.ValueMaximum)e.Add(new("spatial.value",path+".value","Value is outside the predicate's reviewed bounds."));}
      else if(x.Value.HasValue)e.Add(new("spatial.value",path+".value","This predicate does not take a value."));
    }
    if(x.Children?.Count>0)e.Add(new("spatial.children",path,"SPATIAL cannot have children."));
  }
  static void ValidateExpr(TriggerExpression x,int depth,ref int leaves,List<ContractDiagnostic> e,string path,ISet<string> events,bool productionConstraints,ISet<string> anchorIds=null){if(x==null){e.Add(new("trigger.required",path,"Trigger expression is required."));return;}if(depth>ExperienceSchema.MaxExpressionDepth)e.Add(new("trigger.depth",path,"Expression depth exceeds three."));if(!Ops.Contains(x.Op??"")){e.Add(new("trigger.op",path,"Unsupported trigger operator."));return;}if(string.Equals(x.Op,"EVENT",StringComparison.OrdinalIgnoreCase)){leaves++;var engineTimer=string.Equals(x.Event,ExperienceSchema.TimerElapsedEvent,StringComparison.OrdinalIgnoreCase);var engineChat=string.Equals(x.Event,ExperienceSchema.ChatReceivedEvent,StringComparison.OrdinalIgnoreCase);if(string.IsNullOrWhiteSpace(x.Event)||(events!=null&&!events.Contains(x.Event)&&!engineTimer&&!engineChat))e.Add(new("event.unknown",path+".event","Event is not in the canonical catalog or engine event registry."));if(engineTimer&&(x.Where==null||!x.Where.TryGetValue("timer_id",out var timerId)||!Stable(timerId)))e.Add(new("timer.id",path+".where.timer_id","timer_elapsed requires a stable timer_id clause."));if(engineChat&&(x.Where==null||!x.Where.TryGetValue("actor_role",out var actorRole)||(actorRole!=CooperativeEventContract.PeerRole&&actorRole!=CooperativeEventContract.ListenHostRole)))e.Add(new("chat.actor_role",path+".where.actor_role","chat_received requires actor_role peer or listen_host."));if(productionConstraints){if(!engineTimer&&!engineChat&&!RuntimeProductionEventCatalog.Contains(x.Event))e.Add(new("event.not_production",path+".event","Event has creator metadata but no shipping Runtime adapter."));var targetIssue=ProductionTargetIssue(x.Event,x.Target);if(targetIssue!=null)e.Add(new(targetIssue,path+".target","Target cannot be emitted by the Runtime adapter."));foreach(var field in x.Where??new Dictionary<string,string>()){if(!RuntimeProductionEventCatalog.IsAllowedWhere(x.Event,field.Key))e.Add(new("trigger.where_unsupported",path+".where."+field.Key,"Runtime adapter does not emit this constraint field."));else if(!ProductionWhereValue(x.Event,field.Key,field.Value))e.Add(new("trigger.where_value",path+".where."+field.Key,"Constraint value is outside the Runtime field policy."));else if(RuntimeProductionEventCatalog.TryGet(x.Event,out var runtime)&&runtime.FixedWhere.TryGetValue(field.Key,out var fixedValue)&&!string.Equals(field.Value,fixedValue,StringComparison.Ordinal))e.Add(new("trigger.where_fixed",path+".where."+field.Key,"Runtime adapter emits one fixed value for this field."));}}if(x.Children?.Count>0)e.Add(new("trigger.leaf_children",path,"EVENT cannot have children."));return;}if(string.Equals(x.Op,"THRESHOLD",StringComparison.OrdinalIgnoreCase)){leaves++;if(!AdaptiveMeasureCatalog.TryGet(x.Measure,out var measure))e.Add(new("threshold.measure",path+".measure","Measure is not in the advanced adaptive registry."));if(!string.Equals(x.Comparison,"gte",StringComparison.Ordinal))e.Add(new("threshold.comparison",path+".comparison","Only the bounded gte comparison is currently supported."));if(!x.Value.HasValue||x.Value.Value<(measure?.Minimum??1)||x.Value.Value>(measure?.Maximum??86400))e.Add(new("threshold.value",path+".value","Threshold is outside the measure's reviewed bounds."));if(x.Children?.Count>0)e.Add(new("threshold.children",path,"THRESHOLD cannot have children."));return;}if(string.Equals(x.Op,"SPATIAL",StringComparison.OrdinalIgnoreCase)){leaves++;ValidateSpatial(x,e,path,anchorIds);return;}var children=x.Children??new();if(children.Count==0)e.Add(new("trigger.children",path,"Composite trigger requires children."));if(string.Equals(x.Op,"COUNT",StringComparison.OrdinalIgnoreCase)){if(x.Count.GetValueOrDefault()<1||x.Count>128)e.Add(new("trigger.count",path+".count","COUNT must be bounded from 1 to 128."));if(children.Count!=1||!string.Equals(children[0]?.Op,"EVENT",StringComparison.OrdinalIgnoreCase))e.Add(new("trigger.count_clause",path,"COUNT requires exactly one event clause."));}if(string.Equals(x.Op,"SEQUENCE",StringComparison.OrdinalIgnoreCase)&&children.Any(c=>!string.Equals(c?.Op,"EVENT",StringComparison.OrdinalIgnoreCase)))e.Add(new("trigger.sequence_clause",path,"SEQUENCE children must be event clauses."));if(x.WithinSeconds.HasValue&&(x.WithinSeconds<1||x.WithinSeconds>86400))e.Add(new("trigger.window",path+".within_seconds","Timing window must be 1..86400 seconds."));foreach(var c in children)ValidateExpr(c,depth+1,ref leaves,e,path+".children",events,productionConstraints,anchorIds);}
  static bool ContainsEvent(TriggerExpression x)=>x!=null&&(string.Equals(x.Op,"EVENT",StringComparison.OrdinalIgnoreCase)||(x.Children??new()).Any(ContainsEvent));
  static void DetectCycles(Dictionary<string,List<string>> g,List<ContractDiagnostic> e){var state=new Dictionary<string,int>();Func<string,bool> visit=null;visit=n=>{state[n]=1;foreach(var m in g[n]){if(!state.TryGetValue(m,out var s)){if(visit(m))return true;}else if(s==1)return true;}state[n]=2;return false;};foreach(var n in g.Keys)if(!state.ContainsKey(n)&&visit(n)){e.Add(new("graph.cycle","$.stages","Experience graphs must be acyclic in v1."));return;}}
}
