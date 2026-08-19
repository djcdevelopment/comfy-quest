namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

/// <summary>Durable identities for objects created by one reviewed spawn action.</summary>
public sealed class SpawnExecutionStore {
  readonly object gate=new(); readonly string path;
  public SpawnExecutionStore(string runtimeRoot){path=Path.Combine(Path.GetFullPath(runtimeRoot),"state","spawned-objects.json");}
  public void Record(SpawnedObject value){if(value==null||string.IsNullOrWhiteSpace(value.ActionKey))throw new ArgumentException("Spawn identity is required.");lock(gate){var state=Read();state.Objects.RemoveAll(x=>x.UserId==value.UserId&&x.ObjectId==value.ObjectId);state.Objects.Add(value);Write(state);}}
  public IReadOnlyList<SpawnedObject> ForAction(string actionKey){lock(gate){return Read().Objects.Where(x=>string.Equals(x.ActionKey,actionKey,StringComparison.Ordinal)).ToArray();}}
  public IReadOnlyList<SpawnedObject> ForOwnerAction(string ownerKey,string actionId){lock(gate){var prefix=(ownerKey??"")+"|";return Read().Objects.Where(x=>x.ActionKey!=null&&x.ActionKey.StartsWith(prefix,StringComparison.Ordinal)&&string.Equals(x.ActionId,actionId,StringComparison.Ordinal)).ToArray();}}
  public IReadOnlyList<SpawnedObject> ForOwner(string ownerKey){lock(gate){var prefix=(ownerKey??"")+"|";return Read().Objects.Where(x=>x.ActionKey!=null&&x.ActionKey.StartsWith(prefix,StringComparison.Ordinal)).ToArray();}}
  public void Remove(IEnumerable<SpawnedObject> values){lock(gate){var state=Read();var ids=new HashSet<string>((values??Array.Empty<SpawnedObject>()).Select(x=>x.UserId+":"+x.ObjectId),StringComparer.Ordinal);state.Objects.RemoveAll(x=>ids.Contains(x.UserId+":"+x.ObjectId));Write(state);}}
  State Read(){if(!File.Exists(path))return new();try{return JsonConvert.DeserializeObject<State>(File.ReadAllText(path))??new();}catch{return new();}}
  void Write(State state){Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(state,Formatting.Indented));if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);}
  sealed class State{[JsonProperty("schema")]public string Schema{get;set;}="comfy-quest-spawn-ledger/v1";[JsonProperty("objects")]public List<SpawnedObject> Objects{get;set;}=new();}
}

public sealed class SpawnedObject {
  [JsonProperty("action_key")]public string ActionKey{get;set;}
  [JsonProperty("content_hash")]public string ContentHash{get;set;}
  [JsonProperty("action_id")]public string ActionId{get;set;}
  [JsonProperty("zdo_user_id")]public long UserId{get;set;}
  [JsonProperty("zdo_object_id")]public uint ObjectId{get;set;}
}
