namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using System.IO;
using ComfyQuestContracts;
using Newtonsoft.Json;
using UnityEngine;

sealed class RuntimeCharmBinding {
  const string Prefix="comfyQuestRuntime.";
  const string PendingLamp="comfy-quest-runtime-charm-pending";
  static readonly int EmissionColor=Shader.PropertyToID("_EmissionColor");
  static readonly Color Pending=new(.631f,.361f,1f,1f);
  readonly string root; readonly RuntimeReceiptStore receipts; readonly RuntimeExperienceEngine engine;
  readonly RuntimeBindingCoordinator bindings; AimPreview captured;
  readonly Dictionary<Renderer,MaterialPropertyBlock> marked=new(); GameObject markedHost; string landed;
  public RuntimeCharmBinding(string runtimeRoot,RuntimeReceiptStore receiptStore,RuntimeExperienceEngine experienceEngine){root=runtimeRoot;receipts=receiptStore;engine=experienceEngine??throw new ArgumentNullException(nameof(experienceEngine));bindings=new RuntimeBindingCoordinator(root,new RuntimeBindingWorldAdapter(),engine.RunCoordinator().Registry);}

  public AimPreview Preview(bool privateConfirmed){if(!TryAim(out var view,out var kind,out var description))return new(false,"nothing allowlisted in aim",null,null,CharmTargetKind.Unknown);var zdo=view.GetZDO();var identity=zdo==null?"no-zdo":zdo.m_uid.ToString();var decision=zdo!=null&&!zdo.Persistent?PolicyDecision.Deny("binding_not_persistent"):CharmPolicy.CanInscribe(World(privateConfirmed),new CharmTarget{Kind=kind,HasZdo=zdo!=null,LocallyOwned=IsLocallyOwned(view)});var diagnostic=decision.Diagnostic;if(decision.Allowed&&!BindingAllows(kind,out diagnostic))decision=PolicyDecision.Deny(diagnostic);return new(decision.Allowed,description+" ["+identity+"]"+(decision.Allowed?" · ready":" · "+diagnostic),view,diagnostic,kind);}
  public AimPreview Capture(bool privateConfirmed){captured=Preview(privateConfirmed);landed=null;Mark(captured.Allowed&&captured.View!=null?captured.View.gameObject:null);var zdo=captured.View==null?null:captured.View.GetZDO();receipts.Write(new RuntimeReceipt{Operation="charm_check",Status=captured.Allowed?"ready":"rejected",Error=captured.Allowed?null:captured.Diagnostic,BindingZdo=zdo==null?null:zdo.m_uid.ToString(),EvidenceKind=CreatorEvidenceLine.KindName(captured.Allowed?CreatorEvidenceKind.Plumbing:CreatorEvidenceKind.Warning)});return captured;}
  public bool HasCapture=>captured?.Allowed==true&&captured.View!=null;
  public AimPreview CapturedPreview=>captured;
  public string CapturedSummary=>captured==null?"No target captured.":captured.Summary;
  /// <summary>What the last cast landed on, until the next CHECK. Session 2's strip snapped straight
  /// back to READY after a cast and silently re-armed on the next keypress, so a landed charm and a
  /// fresh capture looked identical.</summary>
  public string Landed=>landed;
  public string CastCaptured(bool privateConfirmed){var aim=captured;captured=null;ClearMark();return aim==null?"Cast failed: CHECK a target first.":InscribeAim(aim,privateConfirmed,"Cast Charm");}
  /// <summary>Drop the capture and everything it lit up — the gesture belongs to the expanded bar.</summary>
  public void Release(){captured=null;ClearMark();}

  /// <summary>Show the player what CHECK actually captured, before CAST commits to it. Session 2's
  /// crosshair took a stone through a wall and the READY summary named only a ZDO id, so the real
  /// target was discovered afterwards, from where the glow appeared. Renderer blocks and the child
  /// lamp are owned here and restored on release; the world itself is never written.</summary>
  void Mark(GameObject host){ClearMark();if(host==null)return;markedHost=host;try{var lampObject=new GameObject(PendingLamp){hideFlags=HideFlags.DontSave};lampObject.transform.SetParent(host.transform,false);var lamp=lampObject.AddComponent<Light>();lamp.type=LightType.Point;lamp.shadows=LightShadows.None;lamp.color=Pending;lamp.intensity=1.3f;lamp.range=5f;var emission=new Color(Pending.r*1.35f,Pending.g*1.35f,Pending.b*1.35f,1f);foreach(var renderer in host.GetComponentsInChildren<Renderer>(true)){if(renderer==null||renderer.sharedMaterial==null||!renderer.sharedMaterial.HasProperty(EmissionColor))continue;var prior=new MaterialPropertyBlock();renderer.GetPropertyBlock(prior);marked[renderer]=prior;var block=new MaterialPropertyBlock();renderer.GetPropertyBlock(block);block.SetColor(EmissionColor,emission);renderer.SetPropertyBlock(block);}}catch{}}
  void ClearMark(){foreach(var pair in marked){try{if(pair.Key!=null)pair.Key.SetPropertyBlock(pair.Value);}catch{}}marked.Clear();try{var lamp=markedHost==null?null:markedHost.transform.Find(PendingLamp);if(lamp!=null)UnityEngine.Object.Destroy(lamp.gameObject);}catch{}markedHost=null;}
  public string Inscribe(bool privateConfirmed)=>InscribeAim(Preview(privateConfirmed),privateConfirmed,"Inscribed");
  public IReadOnlyList<RuntimeReceipt> RebindDevActive(ActiveSet set,string correlationId){
    var results=new List<RuntimeReceipt>();
    if(set==null||!string.Equals(set.SourceChannel,"dev",StringComparison.OrdinalIgnoreCase)){
      results.Add(RebindReceipt(set,correlationId,null,null,"rejected","dev_activation_required"));
      return results;
    }
    if(!TryActive(out var active,out var error)){
      results.Add(RebindReceipt(set,correlationId,null,null,"rejected",error));
      return results;
    }
    try{
      foreach(var wear in WearNTear.GetAllInstances()){
        var view=wear?.GetComponent<ZNetView>();
        var zdo=view==null?null:view.GetZDO();
        if(zdo==null||zdo.GetString(Prefix+"packId","")!=active.PackId
            ||zdo.GetString(Prefix+"experienceId","")!=active.ExperienceId
            ||zdo.GetString(Prefix+"bindingId","")!="default")continue;
        var identity=zdo.m_uid.ToString();
        var instanceId=Null(zdo.GetString(Prefix+"bindingInstanceId",""));
        if(!zdo.Persistent){results.Add(RebindReceipt(set,correlationId,identity,instanceId,"rejected","binding_not_persistent"));continue;}
        if(!IsLocallyOwned(view)){results.Add(RebindReceipt(set,correlationId,identity,instanceId,"skipped","remote_owner"));continue;}
        var priorVersion=zdo.GetString(Prefix+"version","");
        var priorHash=zdo.GetString(Prefix+"contentHash","");
        if(priorVersion==active.Version&&string.Equals(priorHash,active.ContentHash,StringComparison.OrdinalIgnoreCase)){
          engine.InvalidateBindingSelection();
          if(engine.StartBoundExperience(identity,out var startError))
            results.Add(RebindReceipt(set,correlationId,identity,instanceId,"skipped","already_current"));
          else if(startError=="binding_start_selection_mismatch")
            results.Add(RebindReceipt(set,correlationId,identity,instanceId,"skipped","superseded_binding"));
          else
            results.Add(RebindReceipt(set,correlationId,identity,instanceId,"rejected",startError??"experience_start_failed"));
          continue;
        }
        try{
          zdo.Set(Prefix+"version",active.Version);
          zdo.Set(Prefix+"contentHash",active.ContentHash);
          engine.InvalidateBindingSelection();
          if(!engine.StartBoundExperience(identity,out var startError))
            throw new InvalidOperationException(startError??"experience_start_failed");
          results.Add(RebindReceipt(set,correlationId,identity,instanceId,"rebound",null));
        }catch(Exception operation){
          try{
            zdo.Set(Prefix+"version",priorVersion);
            zdo.Set(Prefix+"contentHash",priorHash);
            engine.InvalidateBindingSelection();
          }catch{
            results.Add(RebindReceipt(set,correlationId,identity,instanceId,"rejected",
                operation.Message+";binding_rebind_recovery_failed"));
            continue;
          }
          var superseded=operation.Message=="binding_start_selection_mismatch";
          results.Add(RebindReceipt(set,correlationId,identity,instanceId,
              superseded?"skipped":"rejected",superseded?"superseded_binding":operation.Message));
        }
      }
    }catch{
      results.Add(RebindReceipt(set,correlationId,null,null,"rejected","binding_scan_failed"));
    }
    if(results.Count==0)results.Add(RebindReceipt(set,correlationId,null,null,"skipped","no_loaded_binding"));
    return results;
  }
  string InscribeAim(AimPreview aim,bool privateConfirmed,string verb){
    var zdo=aim.View==null?null:aim.View.GetZDO();
    var decision=zdo!=null&&!zdo.Persistent?PolicyDecision.Deny("binding_not_persistent"):CharmPolicy.CanInscribe(World(privateConfirmed),new CharmTarget{Kind=aim.Kind,HasZdo=zdo!=null,LocallyOwned=IsLocallyOwned(aim.View)});
    if(!aim.Allowed||!decision.Allowed){var diagnostic=decision.Diagnostic??aim.Diagnostic??"charm_target_unavailable";WriteFailure(diagnostic,null,zdo?.m_uid.ToString(),CurrentWorldId(),null);return verb+" failed: "+diagnostic;}
    if(!TryActive(out var active,out var error)){WriteFailure(error,null,zdo?.m_uid.ToString(),CurrentWorldId(),null);return verb+" failed: "+error;}
    var reference=new CharmReference{PackId=active.PackId,ExperienceId=active.ExperienceId,BindingId="default",Version=active.Version,ContentHash=active.ContentHash};
    var valid=CharmPolicy.ValidateReference(reference);
    if(!valid.Allowed){WriteFailure(valid.Diagnostic,active,zdo?.m_uid.ToString(),null,null);return verb+" failed: "+valid.Diagnostic;}
    var bindingZdo=zdo.m_uid.ToString();var worldId=CurrentWorldId();
    if(string.IsNullOrWhiteSpace(worldId)){WriteFailure("runtime_world_not_loaded",active,bindingZdo,null,null);return verb+" failed: runtime_world_not_loaded";}
    RuntimeBindingChange change=null;
    try{
      change=bindings.Bind(bindingZdo,worldId,active.Set,active.Document,DateTimeOffset.UtcNow);
      try{
        new QuestPackStore(root).SelectExperience(active.ExperienceId);
        engine.InvalidateBindingSelection();
        if(!engine.StartBoundExperience(bindingZdo,out var startError))throw new InvalidOperationException(startError??"experience_start_failed");
      }catch(Exception operation){
        var recovery=new List<string>();
        try{change=bindings.Restore(bindingZdo,change.ChangeId,worldId,DateTimeOffset.UtcNow);}
        catch(Exception restore){recovery.Add("binding:"+restore.Message);}
        try{new QuestPackStore(root).RestoreExperienceSelection(active.ActivationId,active.PreviousExperienceId);}
        catch(Exception restore){recovery.Add("selection:"+restore.Message);}
        try{engine.InvalidateBindingSelection();}
        catch(Exception restore){recovery.Add("cache:"+restore.Message);}
        if(recovery.Count>0)throw new InvalidOperationException(operation.Message+";binding_operation_recovery_failed:"+string.Join(",",recovery),operation);
        throw;
      }
      receipts.Write(new RuntimeReceipt{Operation="bind",Status="inscribed",PackId=active.PackId,Version=active.Version,ContentHash=active.ContentHash,ActivationId=active.ActivationId,WorldId=worldId,ExperienceId=active.ExperienceId,BindingZdo=bindingZdo,BindingInstanceId=change.Applied.BindingInstanceId,CorrelationId=change.ChangeId,Error=null,EvidenceKind=CreatorEvidenceLine.KindName(CreatorEvidenceKind.Cast)});
      landed="Charm cast on "+aim.Summary;
      return verb+": "+active.ExperienceId+" on "+aim.Summary;
    }catch(Exception e){var diagnostic=string.IsNullOrWhiteSpace(e.Message)?"charm_write_failed":e.Message;WriteFailure(diagnostic,active,bindingZdo,worldId,change);return verb+" failed: "+diagnostic;}
  }
  bool BindingAllows(CharmTargetKind kind,out string error){error=null;if(!TryActive(out var active,out var activeError)){error=activeError;return false;}if(active.TargetKinds==null||active.TargetKinds.Count==0)return true;var canonical=kind==CharmTargetKind.PlayerBuiltPiece?"player_built_piece":kind==CharmTargetKind.Sign?"sign":kind==CharmTargetKind.ItemStand?"item_stand":kind==CharmTargetKind.DedicatedCharm?"dedicated_charm":"unknown";if(active.TargetKinds.Contains(canonical))return true;error="binding_target_incompatible";return false;}

  /// <summary>Which experience this charm binds to. The pack may hold several; the active set
  /// names the one selected, and an unset selector still means "exactly one, or refuse".</summary>
  bool TryActive(out ActiveReference active,out string error){active=null;error="active_set_missing";var path=Path.Combine(root,"active","active-set.json");try{if(!File.Exists(path))return false;var set=JsonConvert.DeserializeObject<ActiveSet>(File.ReadAllText(path));if(set==null||set.Source!=Path.GetFileName(set.Source)||!set.Source.EndsWith(".questpack",StringComparison.OrdinalIgnoreCase)){error="active_source_invalid";return false;}var dev=string.Equals(set.SourceChannel,"dev",StringComparison.OrdinalIgnoreCase);var package=Path.Combine(root,dev?"inbox-dev":"inbox",set.Source);var store=new QuestPackStore(root);var inspected=dev?store.InspectDev(package):store.Inspect(package);if(!inspected.IsValid||!string.Equals(inspected.ContentHash,set.ContentHash,StringComparison.OrdinalIgnoreCase)||inspected.Manifest.PackId!=set.PackId||inspected.Manifest.Version!=set.Version){error="active_content_mismatch";return false;}using var zip=System.IO.Compression.ZipFile.OpenRead(package);if(!ActiveExperienceResolver.TryResolve(zip,set.ExperienceId,out var chosen,out var selection)){error=selection;return false;}var compiled=ExperienceCompiler.CompileProductionJson(chosen.Json);if(!compiled.IsValid||compiled.Document==null){error="active_experience_invalid";return false;}var doc=compiled.Document;var binding=(doc.Bindings??new System.Collections.Generic.List<ExperienceBinding>()).Find(x=>x.Id=="default");var previous=set.ExperienceId;set.ExperienceId=doc.Id;active=new ActiveReference{Set=set,Document=doc,PreviousExperienceId=previous,PackId=set.PackId,Version=set.Version,ContentHash=set.ContentHash,ActivationId=set.ActivationId,ExperienceId=doc.Id,TargetKinds=binding?.TargetKinds};error=null;return true;}catch{error="active_set_unreadable";return false;}}
  static bool TryAim(out ZNetView view,out CharmTargetKind kind,out string description){view=null;kind=CharmTargetKind.Unknown;description=null;try{GameObject go=Player.m_localPlayer?.GetHoverObject();if(go==null){if(GameCamera.instance==null)return false;var cam=GameCamera.instance.transform;if(!Physics.Raycast(cam.position,cam.forward,out var hit,10f))return false;go=hit.collider==null?null:hit.collider.gameObject;}view=go==null?null:go.GetComponentInParent<ZNetView>();if(view==null||view.GetZDO()==null)return false;if(go.GetComponentInParent<TeleportWorld>()!=null){kind=CharmTargetKind.Portal;description="portal";return true;}if(go.GetComponentInParent<Character>()!=null){kind=CharmTargetKind.Creature;description="creature";return true;}if(go.GetComponentInParent<Container>()!=null){kind=CharmTargetKind.Container;description="container";return true;}var piece=go.GetComponentInParent<Piece>();if(piece==null||piece.GetCreator()==0L)return false;if(go.GetComponentInParent<Sign>()!=null){kind=CharmTargetKind.Sign;description=Named(piece,"sign");return true;}if(go.GetComponentInParent<ItemStand>()!=null){kind=CharmTargetKind.ItemStand;description=Named(piece,"item stand");return true;}kind=CharmTargetKind.PlayerBuiltPiece;description=Named(piece,"player-built piece");return true;}catch{return false;}}
  // The piece's own name, so a capture reads as a thing in the world rather than a kind and a ZDO id.
  static string Named(Piece piece,string fallback){try{var token=piece==null?null:piece.m_name;if(string.IsNullOrWhiteSpace(token))return fallback;var localized=Localization.instance==null?null:Localization.instance.Localize(token);return string.IsNullOrWhiteSpace(localized)||localized.StartsWith("[",StringComparison.Ordinal)?fallback:localized;}catch{return fallback;}}
  static bool IsLocallyOwned(ZNetView view){try{return view!=null&&view.IsOwner()&&view.GetZDO()!=null&&view.GetZDO().GetOwner()==ZDOMan.GetSessionID();}catch{return false;}}
  static WorldAuthority World(bool confirmed){try{var dedicated=ZNet.instance!=null&&ZNet.instance.IsDedicated();var host=ZNet.instance!=null&&ZNet.instance.IsServer()&&!dedicated;return new(){IsPrivateWorld=confirmed,IsListenHost=host,IsSolo=host,IsDedicated=dedicated,IsPeerClient=ZNet.instance!=null&&!ZNet.instance.IsServer()};}catch{return new(){IsPrivateWorld=confirmed};}}
  static string CurrentWorldId(){try{if(ZNet.instance==null)return null;var value=ZNet.instance.GetWorldUID();return value==0L?null:value.ToString(System.Globalization.CultureInfo.InvariantCulture);}catch{return null;}}
  static string Null(string value)=>string.IsNullOrWhiteSpace(value)?null:value;
  static RuntimeReceipt RebindReceipt(ActiveSet set,string correlationId,string zdo,string instanceId,string status,string error)=>new(){Operation="dev_rebind",Status=status,Error=error,PackId=set?.PackId,Version=set?.Version,ContentHash=set?.ContentHash,ActivationId=set?.ActivationId,ExperienceId=set?.ExperienceId,WorldId=CurrentWorldId(),CorrelationId=correlationId,BindingZdo=zdo,BindingInstanceId=instanceId,Diagnostics=Array.Empty<ContractDiagnostic>()};
  void WriteFailure(string error,ActiveReference active=null,string zdo=null,string worldId=null,RuntimeBindingChange change=null)=>receipts.Write(new RuntimeReceipt{Operation="bind",Status="rejected",Error=error,PackId=active?.PackId,Version=active?.Version,ContentHash=active?.ContentHash,ActivationId=active?.ActivationId,ExperienceId=active?.ExperienceId,WorldId=worldId,BindingZdo=zdo,BindingInstanceId=change?.Applied?.BindingInstanceId,CorrelationId=change?.ChangeId,EvidenceKind=CreatorEvidenceLine.KindName(CreatorEvidenceKind.Warning)});
  public sealed class AimPreview {public AimPreview(bool allowed,string summary,ZNetView view,string diagnostic,CharmTargetKind kind){Allowed=allowed;Summary=summary;View=view;Diagnostic=diagnostic;Kind=kind;}public bool Allowed{get;}public string Summary{get;}public ZNetView View{get;}public string Diagnostic{get;}public CharmTargetKind Kind{get;}}
  sealed class ActiveReference {public ActiveSet Set;public ExperienceDocument Document;public string PreviousExperienceId;public string PackId,Version,ContentHash,ActivationId,ExperienceId;public System.Collections.Generic.List<string> TargetKinds;}
}
