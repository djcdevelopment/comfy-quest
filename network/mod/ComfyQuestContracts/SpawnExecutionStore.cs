namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

/// <summary>Durable identities for objects created by one reviewed spawn action.</summary>
public sealed class SpawnExecutionStore {
  const int MaxObjects=4096,MaxStateBytes=4*1024*1024;readonly object gate=new(); readonly string path;
  public SpawnExecutionStore(string runtimeRoot){path=Path.Combine(Path.GetFullPath(runtimeRoot),"state","spawned-objects.json");}
  public void Record(SpawnedObject value){if(value==null||string.IsNullOrWhiteSpace(value.ActionKey)||value.ActionKey.Length>1024)throw new ArgumentException("Spawn identity is required.");lock(gate){var state=Read();if(state.Unreadable)throw new InvalidDataException("spawn_state_unreadable");var exists=state.Objects.Any(x=>x.UserId==value.UserId&&x.ObjectId==value.ObjectId);if(!exists&&state.Objects.Count>=MaxObjects)throw new InvalidOperationException("spawn_state_limit");state.Objects.RemoveAll(x=>x.UserId==value.UserId&&x.ObjectId==value.ObjectId);state.Objects.Add(value);Write(state);}}
  public IReadOnlyList<SpawnedObject> ForAction(string actionKey){lock(gate){var state=Read();if(state.Unreadable)throw new InvalidDataException("spawn_state_unreadable");return state.Objects.Where(x=>string.Equals(x.ActionKey,actionKey,StringComparison.Ordinal)).ToArray();}}
  public IReadOnlyList<SpawnedObject> ForOwnerAction(string ownerKey,string actionId){lock(gate){var state=Read();if(state.Unreadable)throw new InvalidDataException("spawn_state_unreadable");var prefix=(ownerKey??"")+"|";return state.Objects.Where(x=>x.ActionKey!=null&&x.ActionKey.StartsWith(prefix,StringComparison.Ordinal)&&string.Equals(x.ActionId,actionId,StringComparison.Ordinal)).ToArray();}}
  public IReadOnlyList<SpawnedObject> ForOwner(string ownerKey){lock(gate){var state=Read();if(state.Unreadable)throw new InvalidDataException("spawn_state_unreadable");var prefix=(ownerKey??"")+"|";return state.Objects.Where(x=>x.ActionKey!=null&&x.ActionKey.StartsWith(prefix,StringComparison.Ordinal)).ToArray();}}
  /// <summary>Owner rows plus whether the ledger could be read at all, so a damaged ledger reports
  /// "unknown" instead of "no objects" to any fact derived from it.</summary>
  public bool TryForOwner(string ownerKey,out IReadOnlyList<SpawnedObject> rows){lock(gate){var state=Read();var prefix=(ownerKey??"")+"|";rows=state.Unreadable?Array.Empty<SpawnedObject>():state.Objects.Where(x=>x.ActionKey!=null&&x.ActionKey.StartsWith(prefix,StringComparison.Ordinal)).ToArray();return !state.Unreadable;}}
  public void Remove(IEnumerable<SpawnedObject> values){lock(gate){var state=Read();if(state.Unreadable)throw new InvalidDataException("spawn_state_unreadable");var ids=new HashSet<string>((values??Array.Empty<SpawnedObject>()).Select(x=>x.UserId+":"+x.ObjectId),StringComparer.Ordinal);state.Objects.RemoveAll(x=>ids.Contains(x.UserId+":"+x.ObjectId));Write(state);}}
  State Read(){if(!File.Exists(path))return new();try{var info=new FileInfo(path);if(info.Length<=0||info.Length>MaxStateBytes)return new State{Unreadable=true};var state=JsonConvert.DeserializeObject<State>(File.ReadAllText(path))??new();state.Objects??=new();if(state.Schema!="comfy-quest-spawn-ledger/v1"||state.Objects.Count>MaxObjects||state.Objects.Any(x=>x==null||string.IsNullOrWhiteSpace(x.ActionKey)||x.ActionKey.Length>1024))state.Unreadable=true;return state;}catch{return new State{Unreadable=true};}}
  void Write(State state){Directory.CreateDirectory(Path.GetDirectoryName(path));var json=JsonConvert.SerializeObject(state,Formatting.Indented);if(Encoding.UTF8.GetByteCount(json)>MaxStateBytes)throw new InvalidDataException("spawn_state_too_large");var temp=path+".tmp";File.WriteAllText(temp,json);if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);}
  sealed class State{[JsonProperty("schema")]public string Schema{get;set;}="comfy-quest-spawn-ledger/v1";[JsonProperty("objects")]public List<SpawnedObject> Objects{get;set;}=new();[JsonIgnore]public bool Unreadable{get;set;}}
}

public sealed class SpawnedObject {
  [JsonProperty("action_key")]public string ActionKey{get;set;}
  [JsonProperty("run_id", NullValueHandling=NullValueHandling.Ignore)]public string RunId{get;set;}
  [JsonProperty("world_id", NullValueHandling=NullValueHandling.Ignore)]public string WorldId{get;set;}
  [JsonProperty("experience_id", NullValueHandling=NullValueHandling.Ignore)]public string ExperienceId{get;set;}
  [JsonProperty("content_hash")]public string ContentHash{get;set;}
  [JsonProperty("action_id")]public string ActionId{get;set;}
  [JsonProperty("kind", NullValueHandling=NullValueHandling.Ignore)]public string Kind{get;set;}
  [JsonProperty("prefab", NullValueHandling=NullValueHandling.Ignore)]public string Prefab{get;set;}
  [JsonProperty("x", NullValueHandling=NullValueHandling.Ignore)]public float? X{get;set;}
  [JsonProperty("y", NullValueHandling=NullValueHandling.Ignore)]public float? Y{get;set;}
  [JsonProperty("z", NullValueHandling=NullValueHandling.Ignore)]public float? Z{get;set;}
  [JsonProperty("zdo_user_id")]public long UserId{get;set;}
  [JsonProperty("zdo_object_id")]public uint ObjectId{get;set;}
}
