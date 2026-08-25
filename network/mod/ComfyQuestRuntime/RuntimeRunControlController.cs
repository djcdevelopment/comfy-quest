namespace ComfyQuestRuntime;

using System;
using System.IO;
using System.Linq;
using ComfyQuestContracts;
using Newtonsoft.Json;

sealed class RuntimeSpawnResetAdapter : IRuntimeSpawnResetAdapter {
  const string Prefix="comfyQuestRuntime.";
  public RuntimeSpawnResetObservation Inspect(SpawnedObject value) {
    if(value==null)return State("mismatch","spawn_row_missing");
    var zdo=ZDOMan.instance?.GetZDO(new ZDOID(value.UserId,value.ObjectId));
    if(zdo==null)return State("already_absent");
    if(zdo.GetString(Prefix+"spawnedContentHash","")!=value.ContentHash
        ||zdo.GetString(Prefix+"spawnedActionId","")!=value.ActionId
        ||zdo.GetString(Prefix+"spawnedActionKey","")!=value.ActionKey)
      return State("mismatch","spawn_marker_mismatch");
    if(ZDOMan.instance==null||zdo.GetOwner()!=ZDOMan.GetSessionID())return State("foreign","spawn_not_locally_owned");
    var view=ZNetScene.instance?.FindInstance(zdo);
    if(view!=null&&!view.IsOwner())return State("foreign","spawn_view_not_locally_owned");
    return State("owned");
  }
  public RuntimeSpawnResetObservation Cleanup(SpawnedObject value) {
    var inspected=Inspect(value);if(!inspected.Safe||inspected.State=="already_absent")return inspected;
    try {
      var zdo=ZDOMan.instance?.GetZDO(new ZDOID(value.UserId,value.ObjectId));
      if(zdo==null)return State("already_absent");
      var view=ZNetScene.instance?.FindInstance(zdo);
      if(view!=null){view.ClaimOwnership();view.Destroy();}
      else{zdo.SetOwner(ZDOMan.GetSessionID());ZDOMan.instance.DestroyZDO(zdo);}
      return State("owned","removed");
    } catch(Exception e){return State("failed","spawn_cleanup_failed:"+e.GetType().Name);}
  }
  static RuntimeSpawnResetObservation State(string state,string detail=null)=>new(){State=state,Detail=detail};
}

/// <summary>Identity-pinned mailbox for reset preview/apply and experience selection. It cannot
/// install or alter content, run commands, inject input, or name a filesystem path: selection
/// chooses among the experiences the already-activated pack contains, and nothing else.</summary>
sealed class RuntimeRunControlController {
  const int MaxRequestBytes=16*1024; const double PollSeconds=.5;
  readonly string packRoot,requestPath,receiptDirectory;readonly RuntimeExperienceEngine engine;readonly RuntimeReceiptStore runtimeReceipts;readonly Func<bool> privateConfirmed,worldLoaded;readonly Func<string> worldUid;readonly Action<string> log;readonly IRuntimeSpawnResetAdapter adapter;double nextPoll;
  public RuntimeRunControlController(string runtimeRoot,RuntimeExperienceEngine runtimeEngine,RuntimeReceiptStore receipts,Func<bool> isPrivateConfirmed,Func<bool> isWorldLoaded,Func<string> currentWorldUid,Action<string> logger=null,IRuntimeSpawnResetAdapter spawnAdapter=null){var root=Path.GetFullPath(runtimeRoot??throw new ArgumentNullException(nameof(runtimeRoot)));engine=runtimeEngine??throw new ArgumentNullException(nameof(runtimeEngine));runtimeReceipts=receipts??throw new ArgumentNullException(nameof(receipts));privateConfirmed=isPrivateConfirmed??(()=>false);worldLoaded=isWorldLoaded??(()=>false);worldUid=currentWorldUid??(()=>string.Empty);log=logger??(_=>{});adapter=spawnAdapter??new RuntimeSpawnResetAdapter();packRoot=root;requestPath=Path.Combine(root,"requests","run-control.json");receiptDirectory=RuntimeRunControlReceipts.Root(root);}
  public void Poll(double realtimeSeconds){if(realtimeSeconds<nextPoll)return;nextPoll=realtimeSeconds+PollSeconds;if(!File.Exists(requestPath))return;RuntimeRunControlRequest request=null;string error=null;try{var info=new FileInfo(requestPath);if(DateTime.UtcNow-info.LastWriteTimeUtc<TimeSpan.FromMilliseconds(300))return;if(info.Length<=0||info.Length>MaxRequestBytes)error="request_size_invalid";else{request=JsonConvert.DeserializeObject<RuntimeRunControlRequest>(File.ReadAllText(requestPath));RuntimeRunControlRequestPolicy.Validate(request,DateTimeOffset.UtcNow,out error);}}catch(Exception e){error="request_read_failed:"+e.GetType().Name;}Consume();if(error!=null){if(RuntimeRunControlRequestPolicy.CanAddressReceipt(request))Write(request,"rejected",error,null,null);log("[run-control] rejected: "+error);return;}Dispatch(request);}
  void Dispatch(RuntimeRunControlRequest request){var actual=worldLoaded()?worldUid():string.Empty;if(!string.Equals(request.ExpectedMachine,Environment.MachineName,StringComparison.OrdinalIgnoreCase)){Write(request,"rejected","runtime_machine_mismatch",null,null);return;}if(string.IsNullOrWhiteSpace(actual)){Write(request,"rejected","runtime_world_not_loaded",null,null);return;}if(actual!=request.ExpectedWorldUid){Write(request,"rejected","runtime_world_mismatch",null,null);return;}if(request.Operation=="select_experience"){SelectExperience(request);return;}var run=engine.RunCoordinator().Registry.Find(request.RunId);if(run==null||run.Scope.WorldId!=actual){Write(request,"rejected","runtime_run_mismatch",null,null);return;}try{if(request.Operation=="preview_reset"){var preview=engine.RunCoordinator().Preview(request.RunId,DateTimeOffset.UtcNow,adapter);Write(request,"previewed","reset_preview_ready",preview,null);return;}if(!privateConfirmed()){Write(request,"rejected","private_world_confirmation_required",null,null);return;}var result=engine.RunCoordinator().Apply(request.RunId,request.PreviewToken,DateTimeOffset.UtcNow,adapter,()=>engine.ClearRunEphemera(run.StateKey));Write(request,result.State=="completed"?"completed":"failed",result.Detail??result.State,null,result);return;}catch(Exception e){Write(request,"rejected",e.Message,null,null);}}
  /// <summary>Bind one of the activated pack's experiences. It addresses the pack, not a run, so
  /// it runs before the run lookup — but behind the same machine, world, and private-world gates,
  /// because it changes what Runtime answers to.</summary>
  void SelectExperience(RuntimeRunControlRequest request){if(!privateConfirmed()){Write(request,"rejected","private_world_confirmation_required",null,null);return;}try{var updated=new QuestPackStore(packRoot).SelectExperience(request.ExperienceId);Write(request,"completed","experience_selected:"+updated.ExperienceId,null,null);}catch(Exception e){Write(request,"rejected",e.Message,null,null);}}
  void Write(RuntimeRunControlRequest request,string state,string detail,RuntimeResetPreview preview,RuntimeResetResult result){try{if(!RuntimeRunControlRequestPolicy.CanAddressReceipt(request))throw new InvalidDataException("request_receipt_identity_invalid");var receipt=new RuntimeRunControlReceipt{RequestId=request.RequestId,Operation=request.Operation,State=state,Detail=detail,Machine=Environment.MachineName,WorldUid=worldLoaded()?worldUid():string.Empty,CompletedUtc=DateTimeOffset.UtcNow,Preview=preview,Result=result};var scope=RuntimeRunControlReceipts.Scope(request.RunId);var scopeDirectory=RuntimeRunControlReceipts.ScopeDirectory(packRoot,scope);Directory.CreateDirectory(scopeDirectory);string path=RuntimeRunControlReceipts.ReceiptPath(packRoot,scope,request.RequestId);string temp=path+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(receipt,Formatting.Indented));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);PruneReceipts(scope);var addressed=result?.NewRunId??request.RunId;var run=string.IsNullOrWhiteSpace(addressed)?null:engine.RunCoordinator().Registry.Find(addressed);runtimeReceipts.Write(new RuntimeReceipt{Operation=request.Operation=="select_experience"?"select_experience":"run_reset",Status=state,Error=state=="completed"?null:detail,RunId=run?.RunId??addressed,WorldId=run?.Scope?.WorldId,ExperienceId=run?.Scope?.ExperienceId??request.ExperienceId,ContentHash=run?.Scope?.ContentHash,BindingZdo=run?.Scope?.BindingZdo,CorrelationId=result?.ResetId??preview?.PreviewToken??request.RequestId,Diagnostics=Array.Empty<ContractDiagnostic>()});}catch(Exception e){log("[run-control] receipt failed: "+e.Message);}}
  /// <summary>Retention, scoped to the run that was just written. Nothing here can read into
  /// another run's partition, which is the point: this used to order every receipt in one flat
  /// directory by write time and delete the tail, so a burst of resets on one run silently evicted
  /// an older run's audit chain (audit C3). Archiving moves files; only the archive bound deletes,
  /// and that writes a receipt saying so.</summary>
  void PruneReceipts(string scope){
    try{
      var now=DateTimeOffset.UtcNow;
      ReceiptRetention.Archive(
        RuntimeRunControlReceipts.ScopeDirectory(packRoot,scope),
        Path.Combine(RuntimeRunControlReceipts.ArchiveRoot(packRoot),RuntimeRunControlReceipts.Scope(scope)),
        RuntimeRunControlReceipts.MaxPerScope,TimeSpan.Zero,now);
      // The number of partitions is bounded too, or a long campaign trades one unbounded
      // directory for an unbounded number of small ones. Whole partitions move, so a retired
      // run's chain stays intact rather than losing its tail.
      var root=new DirectoryInfo(RuntimeRunControlReceipts.Root(packRoot));
      var partitions=root.GetDirectories()
        .Where(directory=>!string.Equals(directory.Name,"archive",StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(directory=>directory.LastWriteTimeUtc).ToArray();
      foreach(var retired in partitions.Skip(RuntimeRunControlReceipts.MaxScopes)){
        ReceiptRetention.Archive(retired.FullName,Path.Combine(RuntimeRunControlReceipts.ArchiveRoot(packRoot),retired.Name),0,TimeSpan.Zero,now);
        try{if(retired.GetFiles().Length==0)retired.Delete();}catch{}
      }
      var evicted=ReceiptRetention.Evict(RuntimeRunControlReceipts.ArchiveRoot(packRoot),RuntimeRunControlReceipts.MaxArchived);
      if(evicted.Any)runtimeReceipts.Write(new RuntimeReceipt{
        Operation="run_control_archive_evicted",Status="completed",AtUtc=now,CandidateCount=evicted.Count,
        EvidenceKind=CreatorEvidenceLine.KindName(CreatorEvidenceKind.Warning),
        Diagnostics=new[]{new ContractDiagnostic("run_control.archive_evicted","$","Run-control archive bound "+RuntimeRunControlReceipts.MaxArchived+" exceeded; "+evicted.Count+" receipt(s) were destroyed. Reset lineage older than this window can no longer be proven.")},
      });
    }catch(Exception e){log("[run-control] receipt retention failed: "+e.Message);}
  }
  void Consume(){try{if(File.Exists(requestPath))File.Delete(requestPath);}catch(Exception e){log("[run-control] cleanup failed: "+e.Message);}}
}
