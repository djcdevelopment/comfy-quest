namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;

public sealed class RuntimeReceipt {
  // Receipt v1 permits additive nullable fields; reserve a schema v2 for changed field meaning.
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
  [JsonProperty("experience_id", NullValueHandling=NullValueHandling.Ignore)] public string ExperienceId { get; set; }
  [JsonProperty("run_id", NullValueHandling=NullValueHandling.Ignore)] public string RunId { get; set; }
  [JsonProperty("world_id", NullValueHandling=NullValueHandling.Ignore)] public string WorldId { get; set; }
  [JsonProperty("event_name", NullValueHandling=NullValueHandling.Ignore)] public string EventName { get; set; }
  [JsonProperty("event_target", NullValueHandling=NullValueHandling.Ignore)] public string EventTarget { get; set; }
  [JsonProperty("actor_role", NullValueHandling=NullValueHandling.Ignore)] public string ActorRole { get; set; }
  [JsonProperty("current_stage_id", NullValueHandling=NullValueHandling.Ignore)] public string CurrentStageId { get; set; }
  [JsonProperty("next_stage_id", NullValueHandling=NullValueHandling.Ignore)] public string NextStageId { get; set; }
  [JsonProperty("current_count", NullValueHandling=NullValueHandling.Ignore)] public int? CurrentCount { get; set; }
  [JsonProperty("required_count", NullValueHandling=NullValueHandling.Ignore)] public int? RequiredCount { get; set; }
  [JsonProperty("activation_id", NullValueHandling=NullValueHandling.Ignore)] public string ActivationId { get; set; }
  [JsonProperty("correlation_id", NullValueHandling=NullValueHandling.Ignore)] public string CorrelationId { get; set; }
  [JsonProperty("stage_entered_utc", NullValueHandling=NullValueHandling.Ignore)] public DateTimeOffset? StageEnteredUtc { get; set; }
  // The creator-loop row taxonomy, stamped where the receipt is written so no reader ever
  // re-derives it from rendered copy. Null (all pre-taxonomy receipts) fails closed to plumbing.
  [JsonProperty("evidence_kind", NullValueHandling=NullValueHandling.Ignore)] public string EvidenceKind { get; set; }
  [JsonProperty("evidence", NullValueHandling=NullValueHandling.Ignore)] public TriggerClauseTrace Evidence { get; set; }
  [JsonProperty("rejected_evidence", NullValueHandling=NullValueHandling.Ignore)] public IReadOnlyList<RejectedTransitionEvidence> RejectedEvidence { get; set; }
  [JsonProperty("diagnostics")] public IReadOnlyList<ContractDiagnostic> Diagnostics { get; set; }
}

/// <summary>Retention for evidence stores, which is not the same thing as retention for logs.
/// <para><c>NFR-OBS-001</c>: "Receipt and evidence stores have explicit size/age retention with
/// archive/export before deletion. Reads stay bounded, and pruning one run cannot break another
/// run's audit chain." Audit C3 found both halves violated from opposite ends — one store had no
/// cap at all, the other deleted the tail of a flat directory shared by every run.</para>
/// <para><b>Archiving moves the file; it never rewrites it.</b> Receipts are read back as
/// authority, so the retention boundary must not be a point where evidence changes shape. A
/// correlated proof set that straddles the boundary is still complete, just in two directories,
/// and both are enumerable. Deletion happens only at the archive bound, and only with a receipt
/// naming what went, so a gap in the chain is never silent.</para></summary>
public static class ReceiptRetention {
  /// <summary>Files whose name ends this way are half-written and belong to nobody yet.</summary>
  const string Temp=".tmp";

  /// <summary>A directory-name component that cannot escape its parent. Scope keys come from run
  /// ids, and the id policy that admits them also admits "." and ".." — safe as an identifier,
  /// not safe as a path segment.</summary>
  public static bool SafeScope(string value)=>
    !string.IsNullOrWhiteSpace(value)
    &&value.Length<=96
    &&value!="."
    &&value!=".."
    &&value.All(c=>char.IsLetterOrDigit(c)||c=='-'||c=='_'||c=='.');

  /// <summary>Move everything past the retention window into the archive, oldest first. Returns
  /// how many moved. Ordering and age both come from write time, so nothing is opened.</summary>
  public static int Archive(string liveDirectory,string archiveDirectory,int keep,TimeSpan maxAge,DateTimeOffset now){
    if(string.IsNullOrWhiteSpace(liveDirectory)||!Directory.Exists(liveDirectory))return 0;
    var live=new DirectoryInfo(liveDirectory).GetFiles("*.json")
      .Where(file=>!file.Name.EndsWith(Temp,StringComparison.Ordinal))
      .OrderByDescending(file=>file.LastWriteTimeUtc).ThenBy(file=>file.Name,StringComparer.Ordinal).ToArray();
    var cutoff=maxAge<=TimeSpan.Zero?DateTime.MinValue:now.UtcDateTime-maxAge;
    var moved=0;
    for(var index=0;index<live.Length;index++){
      var file=live[index];
      if(index<Math.Max(0,keep)&&file.LastWriteTimeUtc>=cutoff)continue;
      if(Move(file,archiveDirectory))moved++;
    }
    return moved;
  }

  /// <summary>Drop the oldest archived receipts past the archive bound. This is the only place
  /// evidence is destroyed, which is why it reports what it destroyed rather than returning a
  /// count and leaving the caller to guess.</summary>
  public static ArchiveEviction Evict(string archiveDirectory,int keep){
    if(string.IsNullOrWhiteSpace(archiveDirectory)||!Directory.Exists(archiveDirectory))return ArchiveEviction.None;
    var archived=new DirectoryInfo(archiveDirectory).GetFiles("*.json",SearchOption.AllDirectories)
      .Where(file=>!file.Name.EndsWith(Temp,StringComparison.Ordinal))
      .OrderByDescending(file=>file.LastWriteTimeUtc).ThenBy(file=>file.Name,StringComparer.Ordinal).ToArray();
    if(archived.Length<=Math.Max(0,keep))return ArchiveEviction.None;
    var doomed=archived.Skip(Math.Max(0,keep)).ToArray();
    var oldest=doomed[doomed.Length-1];
    var newest=doomed[0];
    var removed=0;
    foreach(var file in doomed){try{file.Delete();removed++;}catch{}}
    return removed==0?ArchiveEviction.None:new ArchiveEviction{
      Count=removed,
      OldestUtc=new DateTimeOffset(oldest.LastWriteTimeUtc,TimeSpan.Zero),
      NewestUtc=new DateTimeOffset(newest.LastWriteTimeUtc,TimeSpan.Zero),
      Directory=archiveDirectory,
    };
  }

  static bool Move(FileInfo file,string archiveDirectory){
    try{
      Directory.CreateDirectory(archiveDirectory);
      var target=Path.Combine(archiveDirectory,file.Name);
      // The same request id cannot be archived twice, but a resumed session could retry: keep the
      // archived copy rather than overwriting a file that is already evidence.
      if(File.Exists(target)){file.Delete();return true;}
      file.MoveTo(target);
      return true;
    }catch{return false;}
  }

  public sealed class ArchiveEviction {
    public static readonly ArchiveEviction None=new(){Count=0};
    public int Count {get;set;}
    public DateTimeOffset OldestUtc {get;set;}
    public DateTimeOffset NewestUtc {get;set;}
    public string Directory {get;set;}
    public bool Any=>Count>0;
  }
}

public sealed class RuntimeReceiptStore {
  /// <summary>Explicit size and age retention, declared rather than implied. The live window is
  /// what the Observe surface reads; everything older is archived, not deleted.</summary>
  public const int MaxLiveReceipts=512;
  public const int MaxLiveAgeDays=30;
  /// <summary>The archive's own bound. Past this, evidence is destroyed — and says so.</summary>
  public const int MaxArchivedReceipts=4096;
  /// <summary>Receipts are written from the game loop, so retention runs on a bounded schedule
  /// rather than on every write. A sweep touches names and write times only; it opens nothing.</summary>
  public const int SweepEveryWrites=64;
  public const int MaxListLimit=200;
  readonly object gate = new(); readonly string directory,archiveDirectory; int writesSinceSweep;
  public RuntimeReceiptStore(string runtimeRoot){directory=Path.Combine(Path.GetFullPath(runtimeRoot),"receipts");archiveDirectory=Path.Combine(directory,"archive");}
  public string Write(RuntimeReceipt receipt){if(receipt==null)throw new ArgumentNullException(nameof(receipt));string target;lock(gate){Directory.CreateDirectory(directory);receipt.AtUtc=receipt.AtUtc==default?DateTimeOffset.UtcNow:receipt.AtUtc;receipt.Id=string.IsNullOrWhiteSpace(receipt.Id)?receipt.AtUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N"):receipt.Id;var safe=receipt.Id.Replace("/","_").Replace("\\","_");target=Path.Combine(directory,safe+".json");var temp=target+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(receipt,Formatting.Indented));File.Move(temp,target);}
    if(Interlocked.Increment(ref writesSinceSweep)>=SweepEveryWrites)Sweep(DateTimeOffset.UtcNow);
    return target;}
  /// <summary>Apply retention now. Archiving moves files, so nothing an operation wrote is lost at
  /// the live boundary; only the archive bound destroys anything, and that writes its own receipt
  /// so a reader can tell a pruned chain from an incomplete one.</summary>
  public int Sweep(DateTimeOffset now){
    lock(gate){
      Interlocked.Exchange(ref writesSinceSweep,0);
      var archived=ReceiptRetention.Archive(directory,archiveDirectory,MaxLiveReceipts,TimeSpan.FromDays(MaxLiveAgeDays),now);
      var evicted=ReceiptRetention.Evict(archiveDirectory,MaxArchivedReceipts);
      if(evicted.Any)WriteInternal(new RuntimeReceipt{
        Operation="receipt_archive_evicted",
        Status="completed",
        AtUtc=now,
        CandidateCount=evicted.Count,
        Error=null,
        EvidenceKind=CreatorEvidenceLine.KindName(CreatorEvidenceKind.Warning),
        Diagnostics=new[]{new ContractDiagnostic("receipts.archive_evicted","$","Archive bound "+MaxArchivedReceipts+" exceeded; "+evicted.Count+" receipt(s) written between "+evicted.OldestUtc.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture)+" and "+evicted.NewestUtc.ToUniversalTime().ToString("O",CultureInfo.InvariantCulture)+" were destroyed. A correlated proof set older than this window is no longer complete.")},
      });
      return archived;
    }
  }
  /// <summary>The live window, newest first. Bounded by the retention above rather than by whatever
  /// has accumulated, which is the half of NFR-OBS-001 about reads staying bounded.</summary>
  public IReadOnlyList<string> List(int limit=50)=>Newest(directory,limit,SearchOption.TopDirectoryOnly);
  /// <summary>Evidence that aged out of the live window. Archived receipts are the same bytes in a
  /// different directory, so proof that outlives the window stays reachable instead of merely
  /// surviving on disk.</summary>
  public IReadOnlyList<string> ListArchived(int limit=50)=>Newest(archiveDirectory,limit,SearchOption.AllDirectories);
  static IReadOnlyList<string> Newest(string root,int limit,SearchOption option){if(!Directory.Exists(root))return Array.Empty<string>();var files=Directory.GetFiles(root,"*.json",option);Array.Sort(files,StringComparer.Ordinal);Array.Reverse(files);if(limit<0)limit=0;if(limit>MaxListLimit)limit=MaxListLimit;if(files.Length>limit)Array.Resize(ref files,limit);return files;}
  void WriteInternal(RuntimeReceipt receipt){Directory.CreateDirectory(directory);receipt.Id=receipt.AtUtc.ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'",CultureInfo.InvariantCulture)+"-"+Guid.NewGuid().ToString("N");var target=Path.Combine(directory,receipt.Id+".json");var temp=target+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(receipt,Formatting.Indented));File.Move(temp,target);}
}
