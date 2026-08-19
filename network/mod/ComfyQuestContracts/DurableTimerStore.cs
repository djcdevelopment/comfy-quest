namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

public sealed class DurableTimer {
  [JsonProperty("key")] public string Key {get;set;}
  [JsonProperty("identity")] public WorkflowIdentity Identity {get;set;}
  [JsonProperty("timer_id")] public string TimerId {get;set;}
  [JsonProperty("due_utc")] public DateTimeOffset DueUtc {get;set;}
}

/// <summary>UTC-backed engine timers. Due records remain durable until Runtime acknowledges delivery.</summary>
public sealed class DurableTimerStore {
  readonly object gate=new();readonly string path;
  public DurableTimerStore(string runtimeRoot){path=Path.Combine(Path.GetFullPath(runtimeRoot),"state","timers.json");}
  public void Start(WorkflowIdentity identity,string timerId,DateTimeOffset dueUtc){if(identity==null||string.IsNullOrWhiteSpace(timerId))throw new ArgumentException("Timer identity is required.");lock(gate){var file=Read();if(file.Unreadable)throw new InvalidDataException("timer_state_unreadable");var key=identity.Key+"|"+timerId;file.Timers[key]=new(){Key=key,Identity=identity,TimerId=timerId,DueUtc=dueUtc};Write(file);}}
  public bool Cancel(WorkflowIdentity identity,string timerId){if(identity==null||string.IsNullOrWhiteSpace(timerId))return false;lock(gate){var file=Read();if(file.Unreadable)return false;var removed=file.Timers.Remove(identity.Key+"|"+timerId);if(removed)Write(file);return removed;}}
  public IReadOnlyList<DurableTimer> Due(DateTimeOffset now,int limit=32){lock(gate){var file=Read();if(file.Unreadable)return Array.Empty<DurableTimer>();return file.Timers.Values.Where(x=>x.DueUtc<=now).OrderBy(x=>x.DueUtc).ThenBy(x=>x.Key,StringComparer.Ordinal).Take(Math.Max(0,Math.Min(limit,32))).ToArray();}}
  /// <summary>Timers still counting down, soonest first, so a running deadline can be shown while it matters.</summary>
  public IReadOnlyList<DurableTimer> Pending(DateTimeOffset now,int limit=8){lock(gate){var file=Read();if(file.Unreadable)return Array.Empty<DurableTimer>();return file.Timers.Values.Where(x=>x.DueUtc>now).OrderBy(x=>x.DueUtc).ThenBy(x=>x.Key,StringComparer.Ordinal).Take(Math.Max(0,Math.Min(limit,32))).ToArray();}}
  public bool Acknowledge(string key){if(string.IsNullOrWhiteSpace(key))return false;lock(gate){var file=Read();if(file.Unreadable)return false;var removed=file.Timers.Remove(key);if(removed)Write(file);return removed;}}
  StateFile Read(){if(!File.Exists(path))return new();try{return JsonConvert.DeserializeObject<StateFile>(File.ReadAllText(path))??new();}catch{return new StateFile{Unreadable=true};}}
  void Write(StateFile file){Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(file,Formatting.Indented));if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);}
  sealed class StateFile {[JsonProperty("schema")]public string Schema{get;set;}="comfy-quest-timers/v1";[JsonProperty("timers")]public Dictionary<string,DurableTimer> Timers{get;set;}=new(StringComparer.Ordinal);[JsonIgnore]public bool Unreadable{get;set;}}
}
