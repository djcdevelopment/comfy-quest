namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

/// <summary>Durable claim-before-execute ledger for idempotent v1 actions.</summary>
public sealed class ActionExecutionLedger {
  const int MaxClaims=32768,MaxStateBytes=8*1024*1024;readonly object gate=new();readonly string path;
  public ActionExecutionLedger(string runtimeRoot){path=Path.Combine(Path.GetFullPath(runtimeRoot),"state","executed-actions.json");}
  public bool TryClaim(string key){if(string.IsNullOrWhiteSpace(key)||key.Length>1024)throw new ArgumentException("Stable action key is required.",nameof(key));lock(gate){var state=Read();if(state.Unreadable)throw new InvalidDataException("action_state_unreadable");if(state.Keys.Contains(key))return false;if(state.Keys.Count>=MaxClaims)throw new InvalidOperationException("action_state_limit");state.Keys.Add(key);Directory.CreateDirectory(Path.GetDirectoryName(path));var json=JsonConvert.SerializeObject(state,Formatting.Indented);if(Encoding.UTF8.GetByteCount(json)>MaxStateBytes)throw new InvalidDataException("action_state_too_large");var temp=path+".tmp";File.WriteAllText(temp,json);if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);return true;}}
  public bool TryForOwner(string ownerKey,out IReadOnlyList<string> keys){lock(gate){var state=Read();var prefix=(ownerKey??"")+"|";keys=state.Unreadable?Array.Empty<string>():state.Keys.Where(x=>x!=null&&x.StartsWith(prefix,StringComparison.Ordinal)).OrderBy(x=>x,StringComparer.Ordinal).ToArray();return !state.Unreadable;}}
  State Read(){if(!File.Exists(path))return new();try{var info=new FileInfo(path);if(info.Length<=0||info.Length>MaxStateBytes)return new State{Unreadable=true};var state=JsonConvert.DeserializeObject<State>(File.ReadAllText(path))??new();state.Keys??=new(StringComparer.Ordinal);if(state.Schema!="comfy-quest-action-ledger/v1"||state.Keys.Count>MaxClaims||state.Keys.Any(x=>string.IsNullOrWhiteSpace(x)||x.Length>1024))state.Unreadable=true;return state;}catch{return new State{Unreadable=true};}}
  sealed class State {[JsonProperty("schema")]public string Schema{get;set;}="comfy-quest-action-ledger/v1";[JsonProperty("keys")]public HashSet<string> Keys{get;set;}=new(StringComparer.Ordinal);[JsonIgnore]public bool Unreadable{get;set;}}
}
