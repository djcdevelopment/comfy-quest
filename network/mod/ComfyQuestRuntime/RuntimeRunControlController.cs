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

/// <summary>Identity-pinned mailbox for reset preview/apply. It cannot select content,
/// run commands, inject input, or name a filesystem path.</summary>
sealed class RuntimeRunControlController {
  const int MaxRequestBytes=16*1024; const double PollSeconds=.5;
  readonly string requestPath,receiptDirectory;readonly RuntimeExperienceEngine engine;readonly RuntimeReceiptStore runtimeReceipts;readonly Func<bool> privateConfirmed,worldLoaded;readonly Func<string> worldUid;readonly Action<string> log;readonly IRuntimeSpawnResetAdapter adapter;double nextPoll;
  public RuntimeRunControlController(string runtimeRoot,RuntimeExperienceEngine runtimeEngine,RuntimeReceiptStore receipts,Func<bool> isPrivateConfirmed,Func<bool> isWorldLoaded,Func<string> currentWorldUid,Action<string> logger=null,IRuntimeSpawnResetAdapter spawnAdapter=null){var root=Path.GetFullPath(runtimeRoot??throw new ArgumentNullException(nameof(runtimeRoot)));engine=runtimeEngine??throw new ArgumentNullException(nameof(runtimeEngine));runtimeReceipts=receipts??throw new ArgumentNullException(nameof(receipts));privateConfirmed=isPrivateConfirmed??(()=>false);worldLoaded=isWorldLoaded??(()=>false);worldUid=currentWorldUid??(()=>string.Empty);log=logger??(_=>{});adapter=spawnAdapter??new RuntimeSpawnResetAdapter();requestPath=Path.Combine(root,"requests","run-control.json");receiptDirectory=Path.Combine(root,"receipts","run-control");}
  public void Poll(double realtimeSeconds){if(realtimeSeconds<nextPoll)return;nextPoll=realtimeSeconds+PollSeconds;if(!File.Exists(requestPath))return;RuntimeRunControlRequest request=null;string error=null;try{var info=new FileInfo(requestPath);if(DateTime.UtcNow-info.LastWriteTimeUtc<TimeSpan.FromMilliseconds(300))return;if(info.Length<=0||info.Length>MaxRequestBytes)error="request_size_invalid";else{request=JsonConvert.DeserializeObject<RuntimeRunControlRequest>(File.ReadAllText(requestPath));RuntimeRunControlRequestPolicy.Validate(request,DateTimeOffset.UtcNow,out error);}}catch(Exception e){error="request_read_failed:"+e.GetType().Name;}Consume();if(error!=null){if(RuntimeRunControlRequestPolicy.CanAddressReceipt(request))Write(request,"rejected",error,null,null);log("[run-control] rejected: "+error);return;}Dispatch(request);}
  void Dispatch(RuntimeRunControlRequest request){var actual=worldLoaded()?worldUid():string.Empty;if(!string.Equals(request.ExpectedMachine,Environment.MachineName,StringComparison.OrdinalIgnoreCase)){Write(request,"rejected","runtime_machine_mismatch",null,null);return;}if(string.IsNullOrWhiteSpace(actual)){Write(request,"rejected","runtime_world_not_loaded",null,null);return;}if(actual!=request.ExpectedWorldUid){Write(request,"rejected","runtime_world_mismatch",null,null);return;}var run=engine.RunCoordinator().Registry.Find(request.RunId);if(run==null||run.Scope.WorldId!=actual){Write(request,"rejected","runtime_run_mismatch",null,null);return;}try{if(request.Operation=="preview_reset"){var preview=engine.RunCoordinator().Preview(request.RunId,DateTimeOffset.UtcNow,adapter);Write(request,"previewed","reset_preview_ready",preview,null);return;}if(!privateConfirmed()){Write(request,"rejected","private_world_confirmation_required",null,null);return;}var result=engine.RunCoordinator().Apply(request.RunId,request.PreviewToken,DateTimeOffset.UtcNow,adapter,()=>engine.ClearRunEphemera(run.StateKey));Write(request,result.State=="completed"?"completed":"failed",result.Detail??result.State,null,result);return;}catch(Exception e){Write(request,"rejected",e.Message,null,null);}}
  void Write(RuntimeRunControlRequest request,string state,string detail,RuntimeResetPreview preview,RuntimeResetResult result){try{if(!RuntimeRunControlRequestPolicy.CanAddressReceipt(request))throw new InvalidDataException("request_receipt_identity_invalid");var receipt=new RuntimeRunControlReceipt{RequestId=request.RequestId,Operation=request.Operation,State=state,Detail=detail,Machine=Environment.MachineName,WorldUid=worldLoaded()?worldUid():string.Empty,CompletedUtc=DateTimeOffset.UtcNow,Preview=preview,Result=result};Directory.CreateDirectory(receiptDirectory);string path=Path.Combine(receiptDirectory,request.RequestId+".json");string temp=path+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(receipt,Formatting.Indented));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);PruneReceipts(128);var run=engine.RunCoordinator().Registry.Find(result?.NewRunId??request.RunId);runtimeReceipts.Write(new RuntimeReceipt{Operation="run_reset",Status=state,Error=state=="completed"?null:detail,RunId=run?.RunId??request.RunId,WorldId=run?.Scope?.WorldId,ExperienceId=run?.Scope?.ExperienceId,ContentHash=run?.Scope?.ContentHash,BindingZdo=run?.Scope?.BindingZdo,CorrelationId=result?.ResetId??preview?.PreviewToken,Diagnostics=Array.Empty<ContractDiagnostic>()});}catch(Exception e){log("[run-control] receipt failed: "+e.Message);}}
  void PruneReceipts(int keep){try{foreach(var path in new DirectoryInfo(receiptDirectory).GetFiles("*.json").OrderByDescending(x=>x.LastWriteTimeUtc).Skip(Math.Max(0,keep)).Select(x=>x.FullName))File.Delete(path);}catch(Exception e){log("[run-control] receipt retention failed: "+e.Message);}}
  void Consume(){try{if(File.Exists(requestPath))File.Delete(requestPath);}catch(Exception e){log("[run-control] cleanup failed: "+e.Message);}}
}
