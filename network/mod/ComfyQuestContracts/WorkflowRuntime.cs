namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

public sealed class WorkflowIdentity {
  [JsonProperty("world_id")] public string WorldId {get;set;}
  [JsonProperty("character_id")] public string CharacterId {get;set;}
  [JsonProperty("binding_zdo")] public string BindingZdo {get;set;}
  [JsonProperty("content_hash")] public string ContentHash {get;set;}
  public string Key=>string.Join("|",WorldId,CharacterId,BindingZdo,ContentHash);
}

public sealed class WorkflowProgress {
  [JsonProperty("stage_id")] public string StageId {get;set;}
  [JsonProperty("outcome")] public string Outcome {get;set;}
  [JsonProperty("history")] public List<RuntimeEvent> History {get;set;}=new();
  [JsonProperty("pending_transition_id")] public string PendingTransitionId {get;set;}
  [JsonProperty("pending_from_stage_id")] public string PendingFromStageId {get;set;}
  [JsonProperty("pending_next_stage_id")] public string PendingNextStageId {get;set;}
  [JsonProperty("pending_outcome")] public string PendingOutcome {get;set;}
  [JsonProperty("stage_entered_utc", NullValueHandling=NullValueHandling.Ignore)] public DateTimeOffset? StageEnteredUtc {get;set;}
  [JsonProperty("last_event_utc", NullValueHandling=NullValueHandling.Ignore)] public DateTimeOffset? LastEventUtc {get;set;}
  [JsonProperty("last_progress_utc", NullValueHandling=NullValueHandling.Ignore)] public DateTimeOffset? LastProgressUtc {get;set;}
}

public sealed class WorkflowDecision {
  public WorkflowIdentity Identity {get;set;}
  public string StageId {get;set;}
  public ExperienceTransition Transition {get;set;}
  public bool IsPendingReplay {get;set;}
  [JsonIgnore] public DateTimeOffset? EventAt {get;set;}
  [JsonIgnore] public TriggerEvaluationContext EvaluationContext {get;set;}
  [JsonIgnore] public bool EventMadeProgress {get;set;}
}

/// <summary>Atomic, bounded, Unity-free stage state. A transition remains pending until Runtime completes its actions.</summary>
public sealed class WorkflowStateStore {
  const int MaxHistory=128;readonly object gate=new();readonly string path;
  public WorkflowStateStore(string runtimeRoot){path=Path.Combine(Path.GetFullPath(runtimeRoot),"state","workflow-states.json");}
  public WorkflowDecision Begin(WorkflowIdentity identity,ExperienceDocument document,RuntimeEvent evt,SpatialFacts spatial=null){if(identity==null||document==null||evt==null)throw new ArgumentNullException();lock(gate){var file=Read();if(file.Unreadable)return null;if(!file.States.TryGetValue(identity.Key,out var state)){state=new WorkflowProgress{StageId=document.EntryStage,StageEnteredUtc=evt.At,LastEventUtc=evt.At,LastProgressUtc=evt.At};file.States[identity.Key]=state;}if(!string.IsNullOrWhiteSpace(state.Outcome))return null;var stage=document.Stages?.FirstOrDefault(x=>x.Id==state.StageId);if(stage==null)return null;if(!string.IsNullOrWhiteSpace(state.PendingTransitionId)){var pending=stage.Transitions?.FirstOrDefault(x=>x.Id==state.PendingTransitionId);var eventAt=state.LastEventUtc??state.History.LastOrDefault()?.At;if(pending==null||!eventAt.HasValue)return null;var before=state.History.Count>0?state.History.Take(state.History.Count-1).ToArray():Array.Empty<RuntimeEvent>();return new(){Identity=identity,StageId=stage.Id,Transition=pending,IsPendingReplay=true,EventAt=eventAt,EvaluationContext=Context(state,eventAt.Value,document,spatial),EventMadeProgress=Progress(stage,state.History)>Progress(stage,before)};}var context=Context(state,evt.At,document,spatial);var progressBefore=Progress(stage,state.History);state.LastEventUtc=evt.At;state.History.Add(evt);if(state.History.Count>MaxHistory)state.History.RemoveRange(0,state.History.Count-MaxHistory);var madeProgress=Progress(stage,state.History)>progressBefore;var transition=(stage.Transitions??new()).OrderByDescending(x=>x.Priority).ThenBy(x=>x.Id,StringComparer.Ordinal).FirstOrDefault(x=>TriggerEvaluator.Matches(x.When,state.History,context));if(transition==null){if(madeProgress)state.LastProgressUtc=evt.At;Write(file);return null;}state.PendingTransitionId=transition.Id;state.PendingFromStageId=stage.Id;state.PendingNextStageId=transition.NextStage;state.PendingOutcome=transition.Outcome;Write(file);return new(){Identity=identity,StageId=stage.Id,Transition=transition,EventAt=evt.At,EvaluationContext=context,EventMadeProgress=madeProgress};}}
  public bool Complete(WorkflowDecision decision){if(decision?.Identity==null||decision.Transition==null)return false;lock(gate){var file=Read();if(file.Unreadable||!file.States.TryGetValue(decision.Identity.Key,out var state)||state.PendingTransitionId!=decision.Transition.Id||state.StageId!=decision.StageId)return false;var nextStage=string.IsNullOrWhiteSpace(state.PendingNextStageId)?state.StageId:state.PendingNextStageId;var advances=!string.Equals(state.StageId,nextStage,StringComparison.Ordinal);if(advances&&!decision.EventAt.HasValue)return false;if(advances){state.StageEnteredUtc=decision.EventAt.Value;state.LastProgressUtc=decision.EventAt.Value;}else if(decision.EventMadeProgress&&decision.EventAt.HasValue)state.LastProgressUtc=decision.EventAt.Value;state.StageId=nextStage;state.Outcome=state.PendingOutcome;state.History.Clear();state.PendingTransitionId=null;state.PendingFromStageId=null;state.PendingNextStageId=null;state.PendingOutcome=null;Write(file);return true;}}
  public WorkflowProgress Get(WorkflowIdentity identity){if(identity==null)return null;lock(gate){var file=Read();return !file.Unreadable&&file.States.TryGetValue(identity.Key,out var state)?state:null;}}
  static TriggerEvaluationContext Context(WorkflowProgress state,DateTimeOffset at,ExperienceDocument document,SpatialFacts spatial)=>new(){At=at,StageEnteredUtc=state.StageEnteredUtc,LastProgressUtc=state.LastProgressUtc,BindingPosition=spatial?.BindingPosition,SpawnedPositions=spatial?.SpawnedPositions,AuthoredAnchors=SpatialEvaluator.AnchorMap(document)};
  static int Progress(ExperienceStage stage,IReadOnlyList<RuntimeEvent> history)=>(stage.Transitions??new()).Sum(x=>TriggerEvaluator.EventProgress(x.When,history));
  StateFile Read(){if(!File.Exists(path))return new();try{var file=JsonConvert.DeserializeObject<StateFile>(File.ReadAllText(path))??new();file.States??=new(StringComparer.Ordinal);foreach(var state in file.States.Values){if(state==null){file.Unreadable=true;break;}state.History??=new();}return file;}catch{return new StateFile{Unreadable=true};}}
  void Write(StateFile file){Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(file,Formatting.Indented));if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);}
  sealed class StateFile {[JsonProperty("schema")]public string Schema{get;set;}="comfy-quest-workflow-state/v1";[JsonProperty("states")]public Dictionary<string,WorkflowProgress> States{get;set;}=new(StringComparer.Ordinal);[JsonIgnore]public bool Unreadable{get;set;}}
}
