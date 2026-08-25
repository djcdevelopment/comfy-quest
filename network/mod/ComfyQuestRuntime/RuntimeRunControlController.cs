namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
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

/// <summary>Bounded loaded-scene adapter for machine-driven Charm selection. It exposes only
/// nearby, locally-owned members of the existing closed target registry.</summary>
sealed class RuntimeBindingWorldAdapter : IRuntimeBindingAdapter {
  const string Prefix="comfyQuestRuntime.";
  const int MaxNearbyColliders=1024;
  public IReadOnlyList<RuntimeBindingCandidate> ListCandidates(){
    var player=Player.m_localPlayer??throw new InvalidOperationException("runtime_player_missing");
    var colliders=new UnityEngine.Collider[MaxNearbyColliders+1];
    var count=UnityEngine.Physics.OverlapSphereNonAlloc(player.transform.position,
      (float)RuntimeBindingCoordinator.MaxCandidateDistanceMetres,colliders);
    if(count>=colliders.Length)throw new InvalidOperationException("binding_candidate_scan_limit");
    var values=new System.Collections.Generic.List<RuntimeBindingCandidate>();
    var seen=new HashSet<string>(StringComparer.Ordinal);
    for(var index=0;index<count;index++){
      var wear=colliders[index]?.GetComponentInParent<WearNTear>();
      var view=wear?.GetComponent<ZNetView>();var zdo=view?.GetZDO();
      if(zdo==null||!seen.Add(zdo.m_uid.ToString())||!LocallyOwned(view))continue;
      var piece=wear.GetComponent<Piece>();
      if(piece==null||piece.GetCreator()==0L)continue;
      var distance=UnityEngine.Vector3.Distance(player.transform.position,wear.transform.position);
      if(distance>RuntimeBindingCoordinator.MaxCandidateDistanceMetres)continue;
      var kind=wear.GetComponent<Sign>()!=null?"sign":wear.GetComponent<ItemStand>()!=null?"item_stand":"player_built_piece";
      values.Add(new RuntimeBindingCandidate{BindingZdo=zdo.m_uid.ToString(),TargetKind=kind,
        Label=Name(piece,kind),DistanceMetres=Math.Round(distance,1,MidpointRounding.AwayFromZero)});
    }
    return values.OrderBy(value=>value.DistanceMetres).ThenBy(value=>value.TargetKind,StringComparer.Ordinal)
      .ThenBy(value=>value.BindingZdo,StringComparer.Ordinal).Take(RuntimeBindingCoordinator.MaxCandidates).ToArray();
  }
  public RuntimeBindingReference Read(string bindingZdo){
    var view=Find(bindingZdo,out var zdo);
    if(!LocallyOwned(view))throw new InvalidOperationException("binding_not_locally_owned");
    return new RuntimeBindingReference{PackId=Null(zdo.GetString(Prefix+"packId","")),
      ExperienceId=Null(zdo.GetString(Prefix+"experienceId","")),BindingId=Null(zdo.GetString(Prefix+"bindingId","")),
      Version=Null(zdo.GetString(Prefix+"version","")),ContentHash=Null(zdo.GetString(Prefix+"contentHash",""))};
  }
  public bool TryWrite(string bindingZdo,RuntimeBindingReference reference,out string error){
    error=null;
    try{
      var view=Find(bindingZdo,out var zdo);
      if(!LocallyOwned(view)){error="binding_not_locally_owned";return false;}
      zdo.Set(Prefix+"packId",reference?.PackId??"");
      zdo.Set(Prefix+"experienceId",reference?.ExperienceId??"");
      zdo.Set(Prefix+"bindingId",reference?.BindingId??"");
      zdo.Set(Prefix+"version",reference?.Version??"");
      zdo.Set(Prefix+"contentHash",reference?.ContentHash??"");
      return true;
    }catch(Exception e){error="binding_write_failed:"+e.GetType().Name;return false;}
  }
  static ZNetView Find(string identity,out ZDO zdo){
    var parts=(identity??"").Split(':');
    if(parts.Length!=2||!long.TryParse(parts[0],out var user)||!uint.TryParse(parts[1],out var objectId))throw new InvalidOperationException("binding_identity_invalid");
    zdo=ZDOMan.instance?.GetZDO(new ZDOID(user,objectId));
    if(zdo==null)throw new InvalidOperationException("binding_not_loaded");
    return ZNetScene.instance?.FindInstance(zdo)??throw new InvalidOperationException("binding_view_not_loaded");
  }
  static bool LocallyOwned(ZNetView view){try{return view!=null&&view.IsOwner()&&view.GetZDO()!=null&&view.GetZDO().GetOwner()==ZDOMan.GetSessionID();}catch{return false;}}
  static string Name(Piece piece,string fallback){try{var value=Localization.instance?.Localize(piece.m_name);return string.IsNullOrWhiteSpace(value)||value.StartsWith("[",StringComparison.Ordinal)?fallback.Replace('_',' '):value;}catch{return fallback.Replace('_',' ');}}
  static string Null(string value)=>string.IsNullOrWhiteSpace(value)?null:value;
}

/// <summary>Identity-pinned mailbox for reset, experience selection, and recoverable nearby Charm
/// binding. It cannot install content, run commands, inject input, or name a filesystem path.
/// Selection stays inside the activated pack; binding stays inside the bounded local candidate set.</summary>
sealed class RuntimeRunControlController {
  const int MaxRequestBytes=16*1024; const double PollSeconds=.5;
  readonly string packRoot,requestPath,receiptDirectory;readonly RuntimeExperienceEngine engine;readonly RuntimeReceiptStore runtimeReceipts;readonly Func<bool> privateConfirmed,worldLoaded;readonly Func<string> worldUid;readonly Action<string> log;readonly IRuntimeSpawnResetAdapter adapter;readonly RuntimeBindingCoordinator bindings;double nextPoll;
  public RuntimeRunControlController(string runtimeRoot,RuntimeExperienceEngine runtimeEngine,RuntimeReceiptStore receipts,Func<bool> isPrivateConfirmed,Func<bool> isWorldLoaded,Func<string> currentWorldUid,Action<string> logger=null,IRuntimeSpawnResetAdapter spawnAdapter=null,IRuntimeBindingAdapter bindingAdapter=null){var root=Path.GetFullPath(runtimeRoot??throw new ArgumentNullException(nameof(runtimeRoot)));engine=runtimeEngine??throw new ArgumentNullException(nameof(runtimeEngine));runtimeReceipts=receipts??throw new ArgumentNullException(nameof(receipts));privateConfirmed=isPrivateConfirmed??(()=>false);worldLoaded=isWorldLoaded??(()=>false);worldUid=currentWorldUid??(()=>string.Empty);log=logger??(_=>{});adapter=spawnAdapter??new RuntimeSpawnResetAdapter();bindings=new RuntimeBindingCoordinator(root,bindingAdapter??new RuntimeBindingWorldAdapter(),engine.RunCoordinator().Registry);packRoot=root;requestPath=Path.Combine(root,"requests","run-control.json");receiptDirectory=RuntimeRunControlReceipts.Root(root);}
  public void Poll(double realtimeSeconds){if(realtimeSeconds<nextPoll)return;nextPoll=realtimeSeconds+PollSeconds;if(!File.Exists(requestPath))return;RuntimeRunControlRequest request=null;string error=null;try{var info=new FileInfo(requestPath);if(DateTime.UtcNow-info.LastWriteTimeUtc<TimeSpan.FromMilliseconds(300))return;if(info.Length<=0||info.Length>MaxRequestBytes)error="request_size_invalid";else{request=JsonConvert.DeserializeObject<RuntimeRunControlRequest>(File.ReadAllText(requestPath));RuntimeRunControlRequestPolicy.Validate(request,DateTimeOffset.UtcNow,out error);}}catch(Exception e){error="request_read_failed:"+e.GetType().Name;}Consume();if(error!=null){if(RuntimeRunControlRequestPolicy.CanAddressReceipt(request))Write(request,"rejected",error,null,null);log("[run-control] rejected: "+error);return;}Dispatch(request);}
  void Dispatch(RuntimeRunControlRequest request){
    var actual=worldLoaded()?worldUid():string.Empty;
    if(!string.Equals(request.ExpectedMachine,Environment.MachineName,StringComparison.OrdinalIgnoreCase)){Write(request,"rejected","runtime_machine_mismatch",null,null);return;}
    if(string.IsNullOrWhiteSpace(actual)){Write(request,"rejected","runtime_world_not_loaded",null,null);return;}
    if(actual!=request.ExpectedWorldUid){Write(request,"rejected","runtime_world_mismatch",null,null);return;}
    if(request.Operation=="list_binding_candidates"){ListBindingCandidates(request);return;}
    if(request.Operation=="bind_selected_experience"){BindSelectedExperience(request,actual);return;}
    if(request.Operation=="restore_binding"){RestoreBinding(request,actual);return;}
    if(request.Operation=="select_experience"){SelectExperience(request);return;}
    var run=engine.RunCoordinator().Registry.Find(request.RunId);
    if(run==null||run.Scope.WorldId!=actual){Write(request,"rejected","runtime_run_mismatch",null,null);return;}
    try{
      if(request.Operation=="preview_reset"){var preview=engine.RunCoordinator().Preview(request.RunId,DateTimeOffset.UtcNow,adapter);Write(request,"previewed","reset_preview_ready",preview,null);return;}
      if(!privateConfirmed()){Write(request,"rejected","private_world_confirmation_required",null,null);return;}
      var result=engine.RunCoordinator().Apply(request.RunId,request.PreviewToken,DateTimeOffset.UtcNow,adapter,()=>engine.ClearRunEphemera(run.StateKey));
      Write(request,result.State=="completed"?"completed":"failed",result.Detail??result.State,null,result);return;
    }catch(Exception e){Write(request,"rejected",e.Message,null,null);}
  }
  /// <summary>Bind one of the activated pack's experiences. It addresses the pack, not a run, so
  /// it runs before the run lookup — but behind the same machine, world, and private-world gates,
  /// because it changes what Runtime answers to.</summary>
  void SelectExperience(RuntimeRunControlRequest request){if(!privateConfirmed()){Write(request,"rejected","private_world_confirmation_required",null,null);return;}try{var updated=new QuestPackStore(packRoot).SelectExperience(request.ExperienceId);Write(request,"completed","experience_selected:"+updated.ExperienceId,null,null);}catch(Exception e){Write(request,"rejected",e.Message,null,null);}}
  void ListBindingCandidates(RuntimeRunControlRequest request){try{var candidates=bindings.Candidates();Write(request,"completed","binding_candidates_ready",null,null,candidates,null);}catch(Exception e){Write(request,"rejected",e.Message,null,null);}}
  void BindSelectedExperience(RuntimeRunControlRequest request,string actual){
    if(!privateConfirmed()){Write(request,"rejected","private_world_confirmation_required",null,null);return;}
    RuntimeBindingChange change=null;Selection selection=null;
    try{
      selection=ResolveExperience(request.ExperienceId);
      change=bindings.Bind(request.BindingZdo,actual,selection.Active,selection.Document,DateTimeOffset.UtcNow);
      try{
        new QuestPackStore(packRoot).SelectExperience(request.ExperienceId);
        if(!engine.StartBoundExperience(request.BindingZdo,out var startError))throw new InvalidOperationException(startError??"experience_start_failed");
      }catch(Exception operation){
        var recovery=new System.Collections.Generic.List<string>();
        try{change=bindings.Restore(request.BindingZdo,change.ChangeId,actual,DateTimeOffset.UtcNow);}
        catch(Exception error){recovery.Add("binding:"+error.Message);}
        try{new QuestPackStore(packRoot).RestoreExperienceSelection(selection.Active.ActivationId,selection.PreviousExperienceId);}
        catch(Exception error){recovery.Add("selection:"+error.Message);}
        if(recovery.Count>0)throw new InvalidOperationException(operation.Message+";binding_operation_recovery_failed:"+string.Join(",",recovery),operation);
        throw;
      }
      Write(request,"completed","experience_bound:"+request.ExperienceId,null,null,null,change);
    }catch(Exception e){Write(request,"rejected",e.Message,null,null,null,change);}
  }
  void RestoreBinding(RuntimeRunControlRequest request,string actual){
    if(!privateConfirmed()){Write(request,"rejected","private_world_confirmation_required",null,null);return;}
    try{var change=bindings.Restore(request.BindingZdo,request.BindingChangeId,actual,DateTimeOffset.UtcNow);Write(request,"completed","binding_restored",null,null,null,change);}
    catch(Exception e){Write(request,"rejected",e.Message,null,null);}
  }
  Selection ResolveExperience(string experienceId){
    var store=new QuestPackStore(packRoot);var active=store.ReadActive()??throw new InvalidOperationException("active_set_missing");
    if(active.Source!=Path.GetFileName(active.Source))throw new InvalidOperationException("active_source_invalid");
    var dev=string.Equals(active.SourceChannel,"dev",StringComparison.OrdinalIgnoreCase);
    var package=Path.Combine(packRoot,dev?"inbox-dev":"inbox",active.Source);
    var candidate=dev?store.InspectDev(package):store.Inspect(package);
    if(!candidate.IsValid||candidate.Manifest.PackId!=active.PackId||candidate.Manifest.Version!=active.Version
        ||!string.Equals(candidate.ContentHash,active.ContentHash,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("active_content_mismatch");
    using var zip=System.IO.Compression.ZipFile.OpenRead(package);
    if(!ActiveExperienceResolver.TryResolve(zip,experienceId,out var chosen,out var error))throw new InvalidOperationException(error);
    var compiled=ExperienceCompiler.CompileProductionJson(chosen.Json);
    if(!compiled.IsValid)throw new InvalidOperationException("active_experience_invalid");
    var previousExperienceId=active.ExperienceId;active.ExperienceId=experienceId;
    return new Selection{Active=active,Document=compiled.Document,PreviousExperienceId=previousExperienceId};
  }
  void Write(RuntimeRunControlRequest request,string state,string detail,RuntimeResetPreview preview,RuntimeResetResult result,IReadOnlyList<RuntimeBindingCandidate> candidates=null,RuntimeBindingChange change=null){try{if(!RuntimeRunControlRequestPolicy.CanAddressReceipt(request))throw new InvalidDataException("request_receipt_identity_invalid");var receipt=new RuntimeRunControlReceipt{RequestId=request.RequestId,Operation=request.Operation,State=state,Detail=detail,Machine=Environment.MachineName,WorldUid=worldLoaded()?worldUid():string.Empty,CompletedUtc=DateTimeOffset.UtcNow,Preview=preview,Result=result,BindingCandidates=candidates,BindingChange=change};var scope=RuntimeRunControlReceipts.Scope(request.RunId);var scopeDirectory=RuntimeRunControlReceipts.ScopeDirectory(packRoot,scope);Directory.CreateDirectory(scopeDirectory);string path=RuntimeRunControlReceipts.ReceiptPath(packRoot,scope,request.RequestId);string temp=path+".tmp";File.WriteAllText(temp,JsonConvert.SerializeObject(receipt,Formatting.Indented));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);PruneReceipts(scope);var addressed=result?.NewRunId??request.RunId;var run=string.IsNullOrWhiteSpace(addressed)?null:engine.RunCoordinator().Registry.Find(addressed);ActiveSet active=null;try{active=new QuestPackStore(packRoot).ReadActive();}catch{}runtimeReceipts.Write(new RuntimeReceipt{Operation=request.Operation,Status=state,Error=state is "completed" or "previewed"?null:detail,PackId=active?.PackId,Version=active?.Version,ActivationId=active?.ActivationId,RunId=run?.RunId??addressed,WorldId=run?.Scope?.WorldId??change?.WorldId??(worldLoaded()?worldUid():null),ExperienceId=run?.Scope?.ExperienceId??request.ExperienceId??change?.Applied?.ExperienceId,ContentHash=run?.Scope?.ContentHash??change?.Applied?.ContentHash??active?.ContentHash,BindingZdo=run?.Scope?.BindingZdo??request.BindingZdo,CorrelationId=change?.ChangeId??result?.ResetId??preview?.PreviewToken??request.RequestId,CandidateCount=candidates?.Count??0,Diagnostics=Array.Empty<ContractDiagnostic>()});}catch(Exception e){log("[run-control] receipt failed: "+e.Message);}}
  sealed class Selection{public ActiveSet Active;public ExperienceDocument Document;public string PreviousExperienceId;}
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
        RuntimeRunControlReceipts.ArchiveScopeDirectory(packRoot,scope),
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
