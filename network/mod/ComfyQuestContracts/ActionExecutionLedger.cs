namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

/// <summary>Durable claim-before-execute ledger for idempotent v1 actions.</summary>
public sealed class ActionExecutionLedger {
  const int MaxClaims=32768,MaxStateBytes=8*1024*1024;
  readonly object gate=new();
  readonly string path;

  public ActionExecutionLedger(string runtimeRoot){
    path=Path.Combine(Path.GetFullPath(runtimeRoot),"state","executed-actions.json");
  }

  /// <summary>Legacy one-step claim retained for callers that do not execute a fallible mutation.</summary>
  public bool TryClaim(string key){
    ValidateKey(key);
    lock(gate){
      var state=Readable();
      if(state.Keys.Contains(key)||state.Pending.Contains(key))return false;
      EnsureCapacity(state);
      state.Keys.Add(key);
      Write(state);
      return true;
    }
  }

  /// <summary>Persist intent before a world mutation without calling it complete. A process loss
  /// after the mutation leaves an explicit pending claim, which fails closed instead of silently
  /// replaying a possibly successful reward.</summary>
  public bool TryReserve(string key){
    ValidateKey(key);
    lock(gate){
      var state=Readable();
      if(state.Keys.Contains(key)||state.Pending.Contains(key))return false;
      EnsureCapacity(state);
      state.Pending.Add(key);
      Write(state);
      return true;
    }
  }

  public ActionExecutionState ExecutionState(string key){
    ValidateKey(key);
    lock(gate){
      var state=Readable();
      if(state.Keys.Contains(key))return ActionExecutionState.Committed;
      return state.Pending.Contains(key)
          ? ActionExecutionState.Pending : ActionExecutionState.Unclaimed;
    }
  }

  public void Commit(string key){
    ValidateKey(key);
    lock(gate){
      var state=Readable();
      if(state.Keys.Contains(key))return;
      if(!state.Pending.Remove(key))throw new InvalidOperationException("action_reservation_missing");
      state.Keys.Add(key);
      Write(state);
    }
  }

  /// <summary>Release only a still-pending reservation after the caller has proved that its
  /// attempted mutation was fully compensated. Committed claims are never reopened.</summary>
  public bool Release(string key){
    ValidateKey(key);
    lock(gate){
      var state=Readable();
      if(!state.Pending.Remove(key))return false;
      Write(state);
      return true;
    }
  }

  public bool TryForOwner(string ownerKey,out IReadOnlyList<string> keys){
    lock(gate){
      var state=Read();
      var prefix=(ownerKey??"")+"|";
      keys=state.Unreadable?Array.Empty<string>():state.Keys.Concat(state.Pending)
          .Where(x=>x!=null&&x.StartsWith(prefix,StringComparison.Ordinal))
          .OrderBy(x=>x,StringComparer.Ordinal).ToArray();
      return !state.Unreadable;
    }
  }

  public int RemoveOwner(string ownerKey){
    if(string.IsNullOrWhiteSpace(ownerKey))return 0;
    lock(gate){
      var state=Readable();
      var prefix=ownerKey+"|";
      var completed=state.Keys.RemoveWhere(x=>x.StartsWith(prefix,StringComparison.Ordinal));
      var pending=state.Pending.RemoveWhere(x=>x.StartsWith(prefix,StringComparison.Ordinal));
      if(completed+pending>0)Write(state);
      return completed+pending;
    }
  }

  static void ValidateKey(string key){
    if(string.IsNullOrWhiteSpace(key)||key.Length>1024)
      throw new ArgumentException("Stable action key is required.",nameof(key));
  }

  static void EnsureCapacity(State state){
    if(state.Keys.Count+state.Pending.Count>=MaxClaims)
      throw new InvalidOperationException("action_state_limit");
  }

  State Read(){
    if(!File.Exists(path))return new();
    try{
      var info=new FileInfo(path);
      if(info.Length<=0||info.Length>MaxStateBytes)return new State{Unreadable=true};
      var state=JsonConvert.DeserializeObject<State>(File.ReadAllText(path))??new();
      state.Keys??=new(StringComparer.Ordinal);
      state.Pending??=new(StringComparer.Ordinal);
      if(state.Schema=="comfy-quest-action-ledger/v1"){
        // v1 held only completed one-step claims. Migration is lossless; the next write upgrades it.
        // A v1-shaped file carrying a pending set came from prerelease reservation bytes that an
        // older Runtime could not understand; never erase that uncertainty during migration.
        if(state.Pending.Count>0)state.Unreadable=true;
        else state.Schema="comfy-quest-action-ledger/v2";
      }
      if(state.Schema!="comfy-quest-action-ledger/v2"
          ||state.Keys.Count+state.Pending.Count>MaxClaims
          ||state.Keys.Overlaps(state.Pending)
          ||state.Keys.Concat(state.Pending).Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>1024))
        state.Unreadable=true;
      return state;
    }catch{return new State{Unreadable=true};}
  }

  State Readable(){
    var state=Read();
    if(state.Unreadable)throw new InvalidDataException("action_state_unreadable");
    return state;
  }

  void Write(State state){
    Directory.CreateDirectory(Path.GetDirectoryName(path));
    var json=JsonConvert.SerializeObject(state,Formatting.Indented);
    if(Encoding.UTF8.GetByteCount(json)>MaxStateBytes)
      throw new InvalidDataException("action_state_too_large");
    var temp=path+".tmp";
    File.WriteAllText(temp,json);
    if(File.Exists(path))File.Replace(temp,path,path+".previous");
    else File.Move(temp,path);
  }

  sealed class State {
    [JsonProperty("schema")]public string Schema{get;set;}="comfy-quest-action-ledger/v2";
    [JsonProperty("keys")]public HashSet<string> Keys{get;set;}=new(StringComparer.Ordinal);
    [JsonProperty("pending",DefaultValueHandling=DefaultValueHandling.Ignore)]
    public HashSet<string> Pending{get;set;}=new(StringComparer.Ordinal);
    [JsonIgnore]public bool Unreadable{get;set;}
  }
}

public enum ActionExecutionState { Unclaimed, Pending, Committed }
