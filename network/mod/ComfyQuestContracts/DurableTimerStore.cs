namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

public sealed class DurableTimer {
  [JsonProperty("key")] public string Key {get;set;}
  [JsonProperty("identity")] public WorkflowIdentity Identity {get;set;}
  [JsonProperty("timer_id")] public string TimerId {get;set;}
  [JsonProperty("due_utc")] public DateTimeOffset DueUtc {get;set;}
}

/// <summary>UTC-backed engine timers. Due records remain durable until Runtime acknowledges delivery.</summary>
public sealed class DurableTimerStore {
  const int MaxTimers=4096,MaxStateBytes=4*1024*1024;readonly object gate=new();readonly string path;
  public DurableTimerStore(string runtimeRoot){path=Path.Combine(Path.GetFullPath(runtimeRoot),"state","timers.json");}
  public void Start(WorkflowIdentity identity,string timerId,DateTimeOffset dueUtc){if(identity==null||string.IsNullOrWhiteSpace(timerId))throw new ArgumentException("Timer identity is required.");lock(gate){var file=Read();if(file.Unreadable)throw new InvalidDataException("timer_state_unreadable");var key=identity.Key+"|"+timerId;if(!file.Timers.ContainsKey(key)&&file.Timers.Count>=MaxTimers)throw new InvalidOperationException("timer_state_limit");file.Timers[key]=new(){Key=key,Identity=identity,TimerId=timerId,DueUtc=dueUtc};Write(file);}}
  public bool Cancel(WorkflowIdentity identity,string timerId){if(identity==null||string.IsNullOrWhiteSpace(timerId))return false;lock(gate){var file=Read();if(file.Unreadable)throw new InvalidDataException("timer_state_unreadable");var removed=file.Timers.Remove(identity.Key+"|"+timerId);if(removed)Write(file);return removed;}}
  public IReadOnlyList<DurableTimer> Due(DateTimeOffset now,int limit=32)=>Due(now,null,limit);
  public IReadOnlyList<DurableTimer> Due(DateTimeOffset now,Func<WorkflowIdentity,bool> include,int limit=32){lock(gate){var file=Read();if(file.Unreadable)throw new InvalidDataException("timer_state_unreadable");return file.Timers.Values.Where(x=>x.DueUtc<=now&&(include==null||include(x.Identity))).OrderBy(x=>x.DueUtc).ThenBy(x=>x.Key,StringComparer.Ordinal).Take(Math.Max(0,Math.Min(limit,32))).ToArray();}}
  /// <summary>Timers still counting down, soonest first, so a running deadline can be shown while it matters.</summary>
  public IReadOnlyList<DurableTimer> Pending(DateTimeOffset now,int limit=8)=>Pending(now,null,limit);
  public IReadOnlyList<DurableTimer> Pending(DateTimeOffset now,Func<WorkflowIdentity,bool> include,int limit=8){lock(gate){var file=Read();if(file.Unreadable)throw new InvalidDataException("timer_state_unreadable");return file.Timers.Values.Where(x=>x.DueUtc>now&&(include==null||include(x.Identity))).OrderBy(x=>x.DueUtc).ThenBy(x=>x.Key,StringComparer.Ordinal).Take(Math.Max(0,Math.Min(limit,32))).ToArray();}}
  public bool Acknowledge(string key){if(string.IsNullOrWhiteSpace(key))return false;lock(gate){var file=Read();if(file.Unreadable)throw new InvalidDataException("timer_state_unreadable");var removed=file.Timers.Remove(key);if(removed)Write(file);return removed;}}
  public bool TryForOwner(string ownerKey,out IReadOnlyList<DurableTimer> timers){lock(gate){var file=Read();var prefix=(ownerKey??"")+"|";timers=file.Unreadable?Array.Empty<DurableTimer>():file.Timers.Values.Where(x=>x.Key!=null&&x.Key.StartsWith(prefix,StringComparison.Ordinal)).OrderBy(x=>x.DueUtc).ThenBy(x=>x.Key,StringComparer.Ordinal).ToArray();return !file.Unreadable;}}
  public int RemoveOwner(string ownerKey){if(string.IsNullOrWhiteSpace(ownerKey))return 0;lock(gate){var file=Read();if(file.Unreadable)throw new InvalidDataException("timer_state_unreadable");var prefix=ownerKey+"|";var keys=file.Timers.Keys.Where(x=>x.StartsWith(prefix,StringComparison.Ordinal)).ToArray();foreach(var key in keys)file.Timers.Remove(key);if(keys.Length>0)Write(file);return keys.Length;}}
  StateFile Read(){if(!File.Exists(path))return new();try{var info=new FileInfo(path);if(info.Length<=0||info.Length>MaxStateBytes)return new StateFile{Unreadable=true};var file=JsonConvert.DeserializeObject<StateFile>(File.ReadAllText(path))??new();file.Timers??=new(StringComparer.Ordinal);if(file.Schema!="comfy-quest-timers/v1"||file.Timers.Count>MaxTimers||file.Timers.Any(x=>string.IsNullOrWhiteSpace(x.Key)||x.Key.Length>1024||x.Value==null||x.Value.Identity==null||x.Value.Key!=x.Key||string.IsNullOrWhiteSpace(x.Value.TimerId)) )file.Unreadable=true;return file;}catch{return new StateFile{Unreadable=true};}}
  void Write(StateFile file){Directory.CreateDirectory(Path.GetDirectoryName(path));var json=JsonConvert.SerializeObject(file,Formatting.Indented);if(Encoding.UTF8.GetByteCount(json)>MaxStateBytes)throw new InvalidDataException("timer_state_too_large");var temp=path+".tmp";File.WriteAllText(temp,json);if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);}
  sealed class StateFile {[JsonProperty("schema")]public string Schema{get;set;}="comfy-quest-timers/v1";[JsonProperty("timers")]public Dictionary<string,DurableTimer> Timers{get;set;}=new(StringComparer.Ordinal);[JsonIgnore]public bool Unreadable{get;set;}}
}
