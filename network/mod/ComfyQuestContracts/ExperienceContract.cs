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
  [JsonProperty("children")] public List<TriggerExpression> Children { get; set; }
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
  static readonly HashSet<string> Ops = new(StringComparer.OrdinalIgnoreCase) { "EVENT", "ANY", "ALL", "COUNT", "SEQUENCE" };
  static readonly HashSet<string> Actions = new(StringComparer.OrdinalIgnoreCase) { "message", "arcane_sight", "counter_set", "counter_add", "timer_start", "timer_cancel", "stage_activate", "experience_complete", "experience_fail", "grant_item", "spawn", "clear_spawned" };

  public static CompiledExperience CompileJson(string json, ISet<string> canonicalEvents = null) {
    var errors = new List<ContractDiagnostic>();
    if (json == null || System.Text.Encoding.UTF8.GetByteCount(json) > ExperienceSchema.MaxDocumentBytes)
      return new CompiledExperience(null, new[] { new ContractDiagnostic("document.size", "$", "Document must be present and no larger than 1 MiB.") });
    ExperienceDocument doc;
    try { doc = JsonConvert.DeserializeObject<ExperienceDocument>(json); }
    catch (Exception e) { return new CompiledExperience(null, new[] { new ContractDiagnostic("document.json", "$", e.Message) }); }
    Validate(doc, errors, canonicalEvents);
    return new CompiledExperience(doc, errors);
  }

  static void Validate(ExperienceDocument d, List<ContractDiagnostic> e, ISet<string> events) {
    if (d == null) { e.Add(new("document.empty", "$", "Experience is empty.")); return; }
    if (d.Schema != ExperienceSchema.Id) e.Add(new("schema.unsupported", "$.schema", $"Expected {ExperienceSchema.Id}."));
    if (string.IsNullOrWhiteSpace(d.Id)) e.Add(new("id.required", "$.id", "A stable experience id is required."));
    var stages = d.Stages ?? new();
    if (stages.Count == 0 || stages.Count > ExperienceSchema.MaxStages) e.Add(new("stages.bounds", "$.stages", "An experience requires 1..64 stages."));
    var ids = new HashSet<string>(StringComparer.Ordinal);
    var stageIds = new HashSet<string>(stages.Where(x=>x!=null).Select(x=>x.Id).Where(x=>!string.IsNullOrWhiteSpace(x)), StringComparer.Ordinal);
    if (!stageIds.Contains(d.EntryStage)) e.Add(new("entry.missing", "$.entry_stage", "Entry stage does not exist."));
    int leaves=0, actions=0;
    var edges = new Dictionary<string,List<string>>(StringComparer.Ordinal);
    foreach (var s in stages) {
      if (s == null || !TakeId(ids,s?.Id)) e.Add(new("id.duplicate", "$.stages", "Stage, transition, binding, and action ids must be unique and non-empty."));
      if (s == null) continue;
      edges[s.Id] = new(); actions += ValidateActions(s.EntryActions, ids, e, $"$.stages.{s.Id}.entry_actions");
      foreach (var t in (s.Transitions ?? new()).OrderByDescending(x=>x.Priority).ThenBy(x=>x.Id,StringComparer.Ordinal)) {
        if (!TakeId(ids,t.Id)) e.Add(new("id.duplicate", $"$.stages.{s.Id}.transitions", "Duplicate or empty transition id."));
        ValidateExpr(t.When,1,ref leaves,e,$"$.stages.{s.Id}.transitions.{t.Id}.when",events);
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
  static void ValidateExpr(TriggerExpression x,int depth,ref int leaves,List<ContractDiagnostic> e,string path,ISet<string> events){if(x==null){e.Add(new("trigger.required",path,"Trigger expression is required."));return;}if(depth>ExperienceSchema.MaxExpressionDepth)e.Add(new("trigger.depth",path,"Expression depth exceeds three."));if(!Ops.Contains(x.Op??"")){e.Add(new("trigger.op",path,"Unsupported trigger operator."));return;}if(string.Equals(x.Op,"EVENT",StringComparison.OrdinalIgnoreCase)){leaves++;var engineTimer=string.Equals(x.Event,ExperienceSchema.TimerElapsedEvent,StringComparison.OrdinalIgnoreCase);var engineChat=string.Equals(x.Event,ExperienceSchema.ChatReceivedEvent,StringComparison.OrdinalIgnoreCase);if(string.IsNullOrWhiteSpace(x.Event)||(events!=null&&!events.Contains(x.Event)&&!engineTimer&&!engineChat))e.Add(new("event.unknown",path+".event","Event is not in the canonical catalog or engine event registry."));if(engineTimer&&(x.Where==null||!x.Where.TryGetValue("timer_id",out var timerId)||!Stable(timerId)))e.Add(new("timer.id",path+".where.timer_id","timer_elapsed requires a stable timer_id clause."));if(engineChat&&(x.Where==null||!x.Where.TryGetValue("actor_role",out var actorRole)||(actorRole!=CooperativeEventContract.PeerRole&&actorRole!=CooperativeEventContract.ListenHostRole)))e.Add(new("chat.actor_role",path+".where.actor_role","chat_received requires actor_role peer or listen_host."));if(x.Children?.Count>0)e.Add(new("trigger.leaf_children",path,"EVENT cannot have children."));return;}var children=x.Children??new();if(children.Count==0)e.Add(new("trigger.children",path,"Composite trigger requires children."));if(string.Equals(x.Op,"COUNT",StringComparison.OrdinalIgnoreCase)){if(x.Count.GetValueOrDefault()<1||x.Count>128)e.Add(new("trigger.count",path+".count","COUNT must be bounded from 1 to 128."));if(children.Count!=1||!string.Equals(children[0]?.Op,"EVENT",StringComparison.OrdinalIgnoreCase))e.Add(new("trigger.count_clause",path,"COUNT requires exactly one event clause."));}if(string.Equals(x.Op,"SEQUENCE",StringComparison.OrdinalIgnoreCase)&&children.Any(c=>!string.Equals(c?.Op,"EVENT",StringComparison.OrdinalIgnoreCase)))e.Add(new("trigger.sequence_clause",path,"SEQUENCE children must be event clauses."));if(x.WithinSeconds.HasValue&&(x.WithinSeconds<1||x.WithinSeconds>86400))e.Add(new("trigger.window",path+".within_seconds","Timing window must be 1..86400 seconds."));foreach(var c in children)ValidateExpr(c,depth+1,ref leaves,e,path+".children",events);}
  static void DetectCycles(Dictionary<string,List<string>> g,List<ContractDiagnostic> e){var state=new Dictionary<string,int>();Func<string,bool> visit=null;visit=n=>{state[n]=1;foreach(var m in g[n]){if(!state.TryGetValue(m,out var s)){if(visit(m))return true;}else if(s==1)return true;}state[n]=2;return false;};foreach(var n in g.Keys)if(!state.ContainsKey(n)&&visit(n)){e.Add(new("graph.cycle","$.stages","Experience graphs must be acyclic in v1."));return;}}
}
