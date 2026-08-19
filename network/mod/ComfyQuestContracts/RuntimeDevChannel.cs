namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;

public sealed class RuntimeDevChannelStatus {
  [JsonProperty("schema")] public string Schema { get; set; } = "comfy-quest-dev-channel-status/v1";
  [JsonProperty("session_id")] public string SessionId { get; set; }
  [JsonProperty("observed_utc")] public DateTimeOffset ObservedUtc { get; set; }
  [JsonProperty("armed")] public bool Armed { get; set; }
  [JsonProperty("armed_utc", NullValueHandling=NullValueHandling.Ignore)] public DateTimeOffset? ArmedUtc { get; set; }
  [JsonProperty("state")] public string State { get; set; }
  [JsonProperty("correlation_id", NullValueHandling=NullValueHandling.Ignore)] public string CorrelationId { get; set; }
  [JsonProperty("active_pack_id", NullValueHandling=NullValueHandling.Ignore)] public string ActivePackId { get; set; }
  [JsonProperty("active_version", NullValueHandling=NullValueHandling.Ignore)] public string ActiveVersion { get; set; }
  [JsonProperty("active_content_hash", NullValueHandling=NullValueHandling.Ignore)] public string ActiveContentHash { get; set; }
  [JsonProperty("active_activation_id", NullValueHandling=NullValueHandling.Ignore)] public string ActiveActivationId { get; set; }
  [JsonProperty("current_stage_id", NullValueHandling=NullValueHandling.Ignore)] public string CurrentStageId { get; set; }
  [JsonProperty("last_rejection", NullValueHandling=NullValueHandling.Ignore)] public string LastRejection { get; set; }
}

public sealed class RuntimeDevChannelStatusStore {
  public const int MaxStatusBytes = 64 * 1024;
  readonly string path;
  readonly object gate = new();

  public RuntimeDevChannelStatusStore(string runtimeRoot) {
    path = System.IO.Path.Combine(System.IO.Path.GetFullPath(runtimeRoot), "status", "dev-channel.json");
  }

  public string Path => path;

  public void Write(RuntimeDevChannelStatus status) {
    if (status == null) throw new ArgumentNullException(nameof(status));
    if (status.Schema != "comfy-quest-dev-channel-status/v1")
      throw new InvalidOperationException("dev_channel_status_schema_invalid");
    lock (gate) {
      Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
      var temp = path + ".tmp";
      File.WriteAllText(temp, JsonConvert.SerializeObject(status, Formatting.Indented));
      if (File.Exists(path)) File.Replace(temp, path, null);
      else File.Move(temp, path);
    }
  }

  public RuntimeDevChannelStatus Read() {
    try {
      var info = new FileInfo(path);
      if (!info.Exists || info.Length is < 0 or > MaxStatusBytes) return null;
      var status = JsonConvert.DeserializeObject<RuntimeDevChannelStatus>(File.ReadAllText(path));
      return status?.Schema == "comfy-quest-dev-channel-status/v1" ? status : null;
    } catch { return null; }
  }
}

public sealed class RuntimeDevPollResult {
  public bool Activated { get; set; }
  public string Message { get; set; }
  public ActiveSet ActiveSet { get; set; }
  public string CorrelationId { get; set; }
}

/// <summary>Session-scoped pull coordinator. The browser can place bytes but only an armed
/// Runtime instance can validate and activate them.</summary>
public sealed class RuntimeDevChannelCoordinator {
  readonly string runtimeRoot;
  readonly QuestPackStore packs;
  readonly RuntimeReceiptStore receipts;
  readonly RuntimeDevChannelStatusStore status;
  readonly HashSet<string> observed = new(StringComparer.Ordinal);
  readonly Func<ActiveSet,string,IReadOnlyList<RuntimeReceipt>> rebind;
  string lastRejection;
  string correlationId;
  DateTimeOffset? armedUtc;

  public RuntimeDevChannelCoordinator(string root,Func<ActiveSet,string,IReadOnlyList<RuntimeReceipt>> rebindDevBindings=null) {
    runtimeRoot=Path.GetFullPath(root??throw new ArgumentNullException(nameof(root)));
    packs=new QuestPackStore(runtimeRoot);
    receipts=new RuntimeReceiptStore(runtimeRoot);
    status=new RuntimeDevChannelStatusStore(runtimeRoot);
    rebind=rebindDevBindings;
    SessionId="dev-session-"+Guid.NewGuid().ToString("N");
  }

  public string SessionId { get; }
  public bool Armed { get; private set; }
  public string LastRejection => lastRejection;

  public void Arm(DateTimeOffset now) {
    if(Armed)return;
    Directory.CreateDirectory(Path.Combine(runtimeRoot,"inbox-dev"));
    observed.Clear();
    foreach(var path in Directory.GetFiles(Path.Combine(runtimeRoot,"inbox-dev"),"*.questpack"))observed.Add(Path.GetFileName(path));
    Armed=true;armedUtc=now;lastRejection=null;correlationId=null;
    receipts.Write(new RuntimeReceipt{Operation="dev_channel",Status="armed",AtUtc=now,Diagnostics=Array.Empty<ContractDiagnostic>()});
    WriteStatus(now,"watching",null);
  }

  public void Disarm(DateTimeOffset now) {
    if(!Armed){WriteStatus(now,"disarmed",null);return;}
    Armed=false;armedUtc=null;correlationId=null;
    receipts.Write(new RuntimeReceipt{Operation="dev_channel",Status="disarmed",AtUtc=now,Diagnostics=Array.Empty<ContractDiagnostic>()});
    WriteStatus(now,"disarmed",null);
  }

  public RuntimeDevPollResult Poll(DateTimeOffset now,string currentStageId=null) {
    if(!Armed){WriteStatus(now,"disarmed",currentStageId);return new(){Message="Dev channel disarmed."};}
    var directory=Path.Combine(runtimeRoot,"inbox-dev");
    Directory.CreateDirectory(directory);
    var pending=Directory.GetFiles(directory,"*.questpack")
      .Where(path=>!observed.Contains(Path.GetFileName(path)))
      .OrderBy(path=>File.GetLastWriteTimeUtc(path)).ThenBy(path=>path,StringComparer.Ordinal).ToArray();
    if(pending.Length==0){WriteStatus(now,"watching",currentStageId);return new(){Message="Dev channel armed; watching for revisions."};}
    RuntimeDevPollResult result=null;
    foreach(var path in pending){observed.Add(Path.GetFileName(path));result=Activate(path,now,currentStageId);}
    return result??new(){Message="Dev channel armed; watching for revisions."};
  }

  public void Heartbeat(DateTimeOffset now,string currentStageId=null)=>WriteStatus(now,Armed?"watching":"disarmed",currentStageId);

  RuntimeDevPollResult Activate(string path,DateTimeOffset now,string currentStageId) {
    var candidate=packs.InspectDev(path);
    correlationId="dev-"+now.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N").Substring(0,8);
    if(!candidate.IsValid){lastRejection="pack_invalid";Write(new RuntimeReceipt{Operation="dev_validation",Status="rejected",Error=lastRejection,CorrelationId=correlationId,Diagnostics=candidate.Diagnostics});WriteStatus(now,"rejected",currentStageId);return new(){Message="Dev revision rejected: "+lastRejection,CorrelationId=correlationId};}
    try {
      packs.LoadDevRevision(Path.GetFileName(path));
      var active=packs.ReadActive();
      Write(Stage("dev_transfer","accepted",candidate,active));
      Write(Stage("dev_validation","accepted",candidate,active));
      Write(Stage("dev_activation","activated",candidate,active));
      if(rebind!=null)try{foreach(var receipt in rebind(active,correlationId)??Array.Empty<RuntimeReceipt>())Write(receipt);}catch(Exception e){lastRejection="dev_rebind_failed: "+e.Message;Write(Stage("dev_rebind","rejected",candidate,active,lastRejection));WriteStatus(now,"active",currentStageId);return new(){Activated=true,Message="Dev revision activated; rebind failed: "+e.Message,ActiveSet=active,CorrelationId=correlationId};}
      lastRejection=null;WriteStatus(now,"active",currentStageId);
      return new(){Activated=true,Message="Playing dev revision "+candidate.Manifest.Version+".",ActiveSet=active,CorrelationId=correlationId};
    } catch(Exception e) {
      lastRejection=e.Message;Write(Stage("dev_activation","rejected",candidate,null,lastRejection));WriteStatus(now,"rejected",currentStageId);
      return new(){Message="Dev revision rejected: "+lastRejection,CorrelationId=correlationId};
    }
  }

  RuntimeReceipt Stage(string operation,string state,PackCandidate candidate,ActiveSet active,string error=null)=>new(){Operation=operation,Status=state,Error=error,PackId=candidate.Manifest?.PackId,Version=candidate.Manifest?.Version,ContentHash=candidate.ContentHash,ActivationId=active?.ActivationId,CorrelationId=correlationId,Diagnostics=candidate.Diagnostics??Array.Empty<ContractDiagnostic>()};
  void Write(RuntimeReceipt receipt){receipt.CorrelationId=receipt.CorrelationId??correlationId;receipts.Write(receipt);}
  void WriteStatus(DateTimeOffset now,string state,string currentStageId){ActiveSet active=null;try{active=packs.ReadActive();}catch{lastRejection="active_set_unreadable";state="rejected";}status.Write(new RuntimeDevChannelStatus{SessionId=SessionId,ObservedUtc=now,Armed=Armed,ArmedUtc=armedUtc,State=state,CorrelationId=correlationId,ActivePackId=active?.PackId,ActiveVersion=active?.Version,ActiveContentHash=active?.ContentHash,ActiveActivationId=active?.ActivationId,CurrentStageId=currentStageId,LastRejection=lastRejection});}
}
