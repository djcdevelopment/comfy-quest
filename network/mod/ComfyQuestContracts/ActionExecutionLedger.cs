namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

/// <summary>Durable claim-before-execute ledger for idempotent v1 actions.</summary>
public sealed class ActionExecutionLedger {
  readonly object gate=new();readonly string path;
  public ActionExecutionLedger(string runtimeRoot){path=Path.Combine(Path.GetFullPath(runtimeRoot),"state","executed-actions.json");}
  public bool TryClaim(string key){if(string.IsNullOrWhiteSpace(key)||key.Length>1024)throw new ArgumentException("Stable action key is required.",nameof(key));lock(gate){var state=Read();if(state.Unreadable||state.Keys.Contains(key))return false;state.Keys.Add(key);Directory.CreateDirectory(Path.GetDirectoryName(path));var temp=path+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(state,Formatting.Indented));if(File.Exists(path))File.Replace(temp,path,path+".previous");else File.Move(temp,path);return true;}}
  State Read(){if(!File.Exists(path))return new();try{return JsonConvert.DeserializeObject<State>(File.ReadAllText(path))??new();}catch{return new State{Unreadable=true};}}
  sealed class State {[JsonProperty("schema")]public string Schema{get;set;}="comfy-quest-action-ledger/v1";[JsonProperty("keys")]public HashSet<string> Keys{get;set;}=new(StringComparer.Ordinal);[JsonIgnore]public bool Unreadable{get;set;}}
}
