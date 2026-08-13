namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

public sealed class RuntimeReceipt {
  [JsonProperty("schema")] public string Schema { get; set; } = "comfy-quest-runtime-receipt/v1";
  [JsonProperty("id")] public string Id { get; set; }
  [JsonProperty("at_utc")] public DateTimeOffset AtUtc { get; set; }
  [JsonProperty("operation")] public string Operation { get; set; }
  [JsonProperty("status")] public string Status { get; set; }
  [JsonProperty("error")] public string Error { get; set; }
  [JsonProperty("pack_id")] public string PackId { get; set; }
  [JsonProperty("version")] public string Version { get; set; }
  [JsonProperty("content_hash")] public string ContentHash { get; set; }
  [JsonProperty("candidate_count")] public int? CandidateCount { get; set; }
  [JsonProperty("valid_count")] public int? ValidCount { get; set; }
  [JsonProperty("stage_id")] public string StageId { get; set; }
  [JsonProperty("transition_id")] public string TransitionId { get; set; }
  [JsonProperty("action_id")] public string ActionId { get; set; }
  [JsonProperty("binding_zdo")] public string BindingZdo { get; set; }
  [JsonProperty("event_name", NullValueHandling=NullValueHandling.Ignore)] public string EventName { get; set; }
  [JsonProperty("event_target", NullValueHandling=NullValueHandling.Ignore)] public string EventTarget { get; set; }
  [JsonProperty("actor_role", NullValueHandling=NullValueHandling.Ignore)] public string ActorRole { get; set; }
  [JsonProperty("current_stage_id", NullValueHandling=NullValueHandling.Ignore)] public string CurrentStageId { get; set; }
  [JsonProperty("next_stage_id", NullValueHandling=NullValueHandling.Ignore)] public string NextStageId { get; set; }
  [JsonProperty("current_count", NullValueHandling=NullValueHandling.Ignore)] public int? CurrentCount { get; set; }
  [JsonProperty("required_count", NullValueHandling=NullValueHandling.Ignore)] public int? RequiredCount { get; set; }
  [JsonProperty("diagnostics")] public IReadOnlyList<ContractDiagnostic> Diagnostics { get; set; }
}

public sealed class RuntimeReceiptStore {
  readonly object gate = new(); readonly string directory;
  public RuntimeReceiptStore(string runtimeRoot){directory=Path.Combine(Path.GetFullPath(runtimeRoot),"receipts");}
  public string Write(RuntimeReceipt receipt){if(receipt==null)throw new ArgumentNullException(nameof(receipt));lock(gate){Directory.CreateDirectory(directory);receipt.AtUtc=receipt.AtUtc==default?DateTimeOffset.UtcNow:receipt.AtUtc;receipt.Id=string.IsNullOrWhiteSpace(receipt.Id)?receipt.AtUtc.ToString("yyyyMMddTHHmmssfffZ")+"-"+Guid.NewGuid().ToString("N"):receipt.Id;var safe=receipt.Id.Replace("/","_").Replace("\\","_");var target=Path.Combine(directory,safe+".json");var temp=target+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(receipt,Formatting.Indented));File.Move(temp,target);return target;}}
  public IReadOnlyList<string> List(int limit=50){if(!Directory.Exists(directory))return Array.Empty<string>();var files=Directory.GetFiles(directory,"*.json");Array.Sort(files,StringComparer.Ordinal);Array.Reverse(files);if(limit<0)limit=0;if(limit>200)limit=200;if(files.Length>limit)Array.Resize(ref files,limit);return files;}
}
