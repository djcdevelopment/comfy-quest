namespace ComfyQuestRuntime;
using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using ComfyQuestContracts;
using HarmonyLib;

[BepInPlugin("djcdevelopment.valheim.comfyquestruntime", "ComfyQuestRuntime", "0.1.0")]
public sealed class ComfyQuestRuntimePlugin : BaseUnityPlugin {
  QuestPackStore packs;
  RuntimeReceiptStore receipts;
  RuntimeCharmBinding charms;
  RuntimeDevChannelCoordinator devChannel;
  RuntimeCreatorRequestController creatorRequests;
  RuntimeRunControlController runControl;
  RuntimeRunStatusStore runStatus;
  RuntimeWorldEntryController worldEntry;
  RuntimeArcaneSight arcaneSight;
  RuntimeExperienceEngine engine;
  ConfigEntry<KeyboardShortcut> checkHotkey;
  ConfigEntry<KeyboardShortcut> loadHotkey;
  ConfigEntry<KeyboardShortcut> barHotkey;
  ConfigEntry<KeyboardShortcut> castHotkey;
  ConfigEntry<bool> privateWorldConfirmed;
  ConfigEntry<string> studioUrl;
  ConfigEntry<float> alertAnchorX;
  ConfigEntry<float> alertAnchorY;
  ConfigEntry<bool> showCreatorBar;
  string runtimeRoot; bool inboxChecked; int checkedCandidates,checkedValid; bool showMaintenance,showCardDetails,showDetails; double nextDevPoll,nextContentProbe,nextRunStatus; bool welcomed,hasQuestContent; string statusDetail; bool statusIdle; IReadOnlyList<PackCandidate> quietInspected;
  readonly List<CreatorEvidenceLine> outcomes=new(); UnityEngine.Vector2 outcomeScroll,evidenceScroll,detailsScroll;
  bool barExpanded,alertDragging; UnityEngine.Vector2 alertDragOffset; string status="Runtime ready"; PackCandidate[] available=Array.Empty<PackCandidate>(); ActiveSet[] activationHistory=Array.Empty<ActiveSet>(); int selectedVersion,selectedActivation; UnityEngine.Rect details=new(24,140,560,610);
  UnityEngine.Texture2D windowBackground,rowBackground,helpBackground,greenBackground,greenGlowBackground,blueBackground,amberBackground,primaryBackground,castRowBackground,dimBackground,deadlineBackground,deadlineUrgentBackground,circleDoneBackground,circleCurrentBackground,circleWaitingBackground,railDoneBackground,railWaitingBackground; UnityEngine.GUIStyle windowStyle,barPanelStyle,sectionStyle,rowStyle,helpStyle,readyStyle,primaryStyle,blueButtonStyle,amberButtonStyle,dimButtonStyle,stepPendingStyle,rungDoneStyle,rungCurrentStyle,rungWaitingStyle,rungNameDoneStyle,rungNameCurrentStyle,rungNameWaitingStyle,railDoneStyle,railWaitingStyle,stampStyle,castRowStyle,deadlineStyle,deadlineUrgentStyle,storyStyle,castStyle,warnStyle,plumbStyle,questTitleStyle,playingStyle,chipStyle,stateReadyStyle,stateChoiceStyle;
  void Awake() {
    runtimeRoot=Path.Combine(Paths.ConfigPath,"comfy-quest-runtime");
    Directory.CreateDirectory(Path.Combine(runtimeRoot,"inbox"));
    Directory.CreateDirectory(Path.Combine(runtimeRoot,"inbox-dev"));
    packs=new QuestPackStore(runtimeRoot);
    receipts=new RuntimeReceiptStore(runtimeRoot);
    charms=new RuntimeCharmBinding(runtimeRoot,receipts);
    privateWorldConfirmed=Config.Bind("Safety","PrivateWorldConfirmed",false,"Required before Charm inscription or mutation. Enable only for a private solo/listen-host world you control.");
    engine=new RuntimeExperienceEngine(runtimeRoot,receipts,()=>privateWorldConfirmed.Value);
    runStatus=new RuntimeRunStatusStore(runtimeRoot);
    devChannel=new RuntimeDevChannelCoordinator(runtimeRoot,(active,correlation)=>{
      var result=charms.RebindDevActive(active,correlation);
      if(result.Any(value=>value.Status=="rebound"||value.Status=="already_current")){
        engine?.ResolveAlert("charm_unbound");
        engine?.ResolveAlert("binding_version");
      }
      return result;
    });
    arcaneSight=new RuntimeArcaneSight(runtimeRoot);
    worldEntry=new RuntimeWorldEntryController(runtimeRoot,message=>Logger.LogInfo(message));
    creatorRequests=new RuntimeCreatorRequestController(runtimeRoot,devChannel,()=>privateWorldConfirmed.Value,()=>ZNet.instance!=null&&Player.m_localPlayer!=null,CurrentWorldUid,()=>worldEntry.CreatorSessionId,message=>Logger.LogInfo(message),SetCreatorBuildMode,CreatorBuildModeEnabled);
    runControl=new RuntimeRunControlController(runtimeRoot,engine,receipts,()=>privateWorldConfirmed.Value,()=>ZNet.instance!=null&&Player.m_localPlayer!=null,CurrentWorldUid,()=>worldEntry.CreatorSessionId,message=>Logger.LogInfo(message));
    studioUrl=Config.Bind("Studio","Url","http://127.0.0.1:8085/quest-studio","Loopback URL opened by the Runtime creator bar. Only an http:// localhost address is accepted.");
    var legacyAnchor=Config.Bind("Presentation","DeadlineAnchor",.16f,"Legacy vertical alert position; migrated into AlertAnchorY.");
    alertAnchorX=Config.Bind("Presentation","AlertAnchorX",.5f,"Horizontal center of the single alert anchor as a screen fraction (0.05-0.95).");
    alertAnchorY=Config.Bind("Presentation","AlertAnchorY",legacyAnchor.Value,"Top of the single alert anchor as a screen fraction (0.05-0.85).");
    showCreatorBar=Config.Bind("Presentation","ShowCreatorBar",true,"Draw the overhead creator surface. OFF hides every Runtime overlay and changes nothing else -- quests still load, events still fire, hotkeys still work. For unattended screenshot capture, where the bar otherwise burns into every frame. ComfyNetworkSense's showHudOnStart is the same idea.");
    var legacyHotkey=Config.Bind("Runtime","DrawerHotkey",new KeyboardShortcut(UnityEngine.KeyCode.F9),"Legacy creator-surface key; migrated into CreatorBarHotkey.");
    barHotkey=Config.Bind("Runtime","CreatorBarHotkey",legacyHotkey.Value,"Expand or minimize the overhead creator bar.");
    castHotkey=Config.Bind("Runtime","CharmGestureHotkey",new KeyboardShortcut(UnityEngine.KeyCode.BackQuote),"While the creator bar is expanded: first press CHECKS and captures the aimed target; second press CASTS onto that exact target.");
    checkHotkey=Config.Bind("Runtime","CheckHotkey",new KeyboardShortcut(UnityEngine.KeyCode.F10),"Check inbox without activating content.");
    loadHotkey=Config.Bind("Runtime","LoadLatestHotkey",new KeyboardShortcut(UnityEngine.KeyCode.F11),"Activate the highest compatible version.");
    RuntimeEventRouter.Engine=engine;
    devChannel.Heartbeat(DateTimeOffset.UtcNow);
    var harmony=new Harmony("djcdevelopment.valheim.comfyquestruntime");
    harmony.PatchAll(typeof(RuntimeInputPatches));
    RuntimeKillPatches.Apply(harmony); RuntimeCooperativePatches.Apply(harmony);
    RuntimeEasyEventPatches.Apply(harmony); RuntimeProgressionPatches.Apply(harmony);
    RuntimeWorldStatePatches.Apply(harmony); RuntimeCoreActionPatches.Apply(harmony);
    RuntimeHarvestPatches.Apply(harmony);
    foreach(var patch in RuntimePatching.Outcomes) Logger.LogInfo($"Runtime patch {patch.SignatureId}: {(patch.Applied?"ok":patch.Detail)}");
    worldEntry.TryStart(this);
    Logger.LogInfo($"Runtime ready. CreatorBar={barHotkey.Value}, charm={castHotkey.Value}, check={checkHotkey.Value}, load={loadHotkey.Value}, inbox={Path.Combine(runtimeRoot,"inbox")}");
  }
  void Update(){engine?.Tick();PollDevChannel();creatorRequests?.Poll(UnityEngine.Time.realtimeSinceStartup,engine?.CurrentStageId());runControl?.Poll(UnityEngine.Time.realtimeSinceStartup);PublishRunStatus();arcaneSight?.Tick();WelcomeOnce();if(barExpanded)RuntimeInputPatches.Maintain();if(TypingInGame())return;if(barHotkey.Value.IsDown())SetBarExpanded(!barExpanded);if(barExpanded&&UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))SetBarExpanded(false);if(barExpanded&&castHotkey.Value.IsDown())HandleCharmGesture();if(checkHotkey.Value.IsDown()){status=CheckForNew();Report(status,statusIdle);}if(loadHotkey.Value.IsDown()){status=LoadLatest();Report(status,statusIdle);}}
  void OnGUI() {
    // Visibility only. Update() keeps ticking the engine, polling the dev channel
    // and reading hotkeys, so turning this off costs nothing but the pixels.
    if(!showCreatorBar.Value) return;
    if(!HasQuestContent()) return;
    EnsureStyles();
    DrawCreatorBar();
    DrawAlertAnchor();
    if(!barExpanded) return;
    arcaneSight?.Draw(helpStyle);
    if(showDetails) {
      var bar=CreatorBarRect();
      details.width=UnityEngine.Mathf.Max(120f,UnityEngine.Mathf.Min(560f,UnityEngine.Screen.width-16f));
      details.x=UnityEngine.Mathf.Clamp(bar.x,8f,UnityEngine.Mathf.Max(8f,UnityEngine.Screen.width-details.width-8f));
      details.y=UnityEngine.Mathf.Min(bar.yMax+8f,UnityEngine.Mathf.Max(8f,UnityEngine.Screen.height-188f));
      details.height=UnityEngine.Mathf.Max(180f,UnityEngine.Mathf.Min(610f,UnityEngine.Screen.height-details.y-8f));
      details=UnityEngine.GUILayout.Window(94731,details,DrawDetailsPopover,"Creator details",windowStyle);
    }
  }

  // The creator surface is always overhead when quest content exists. F9 changes density,
  // not visibility: 36 px for the hundredth run, 116 px when names and actions are needed.
  // The safe top is below the observed host diagnostic band; the previous y=48 placed the
  // complete compact bar behind that band at the 1026x740 live viewport.
  UnityEngine.Rect CreatorBarRect() {
    var bounds=RuntimeCreatorBarLayout.Place(UnityEngine.Screen.width,UnityEngine.Screen.height,barExpanded);
    return new UnityEngine.Rect(bounds.X,bounds.Y,bounds.Width,bounds.Height);
  }

  sealed class WorkflowSnapshot {
    public ActiveSet Active;
    public PackCandidate Latest;
    public bool Valid;
    public bool Confirmed;
    public int Current;
  }

  WorkflowSnapshot Workflow() {
    var active=ReadActive();
    var valid=inboxChecked&&checkedCandidates>0&&checkedCandidates==checkedValid;
    var latest=available.Length>0?available[0]:null;
    var confirmed=active!=null&&latest!=null&&active.PackId==latest.Manifest.PackId
        &&active.Version==latest.Manifest.Version
        &&string.Equals(active.ContentHash,latest.ContentHash,StringComparison.OrdinalIgnoreCase);
    return new WorkflowSnapshot {
      Active=active, Latest=latest, Valid=valid, Confirmed=confirmed,
      Current=!inboxChecked?0:!valid?1:!confirmed?2:4,
    };
  }

  void DrawCreatorBar() {
    var rect=CreatorBarRect();
    barPanelStyle??=BarStyle(windowBackground);
    UnityEngine.GUI.Box(rect,UnityEngine.GUIContent.none,barPanelStyle);
    UnityEngine.GUILayout.BeginArea(new UnityEngine.Rect(rect.x+7f,rect.y+2f,rect.width-14f,rect.height-4f));
    var workflow=Workflow();
    DrawCompactBar(workflow);
    if(barExpanded) DrawExpandedBar(workflow);
    UnityEngine.GUILayout.EndArea();
  }

  void DrawCompactBar(WorkflowSnapshot workflow) {
    UnityEngine.GUILayout.BeginHorizontal(UnityEngine.GUILayout.Height(32f));
    UnityEngine.GUILayout.Label("COMFY QUEST",sectionStyle,UnityEngine.GUILayout.Width(92f),UnityEngine.GUILayout.Height(30f));
    DrawCompactDots(workflow);
    var active=workflow.Active;
    var title=active==null?"Nothing playing":CreatorLoopNotice.ActiveTitle(TitleSource(),active)??active.PackId;
    UnityEngine.GUILayout.Label(active==null?title:title+"  "+active.Version,playingStyle,UnityEngine.GUILayout.MinWidth(170f),UnityEngine.GUILayout.Height(30f));
    UnityEngine.GUILayout.FlexibleSpace();
    UnityEngine.GUILayout.Label(CharmState(),CharmReady()?readyStyle:stepPendingStyle,UnityEngine.GUILayout.Width(92f),UnityEngine.GUILayout.Height(28f));
    if(UnityEngine.GUILayout.Button((barExpanded?"MINIMIZE ":"EXPAND ")+barHotkey.Value,dimButtonStyle,UnityEngine.GUILayout.Width(132f),UnityEngine.GUILayout.Height(28f))) SetBarExpanded(!barExpanded);
    UnityEngine.GUILayout.EndHorizontal();
  }

  void DrawCompactDots(WorkflowSnapshot workflow) {
    UnityEngine.GUILayout.BeginHorizontal(UnityEngine.GUILayout.Width(116f));
    for(var index=0;index<4;index++) {
      var done=workflow.Current==4||index<workflow.Current;
      var current=workflow.Current==index;
      UnityEngine.GUILayout.Label(done?"✓":"",done?rungDoneStyle:current?rungCurrentStyle:rungWaitingStyle,UnityEngine.GUILayout.Width(22f),UnityEngine.GUILayout.Height(22f));
      if(index<3) UnityEngine.GUILayout.Space(6f);
    }
    UnityEngine.GUILayout.EndHorizontal();
  }

  void DrawExpandedBar(WorkflowSnapshot workflow) {
    UnityEngine.GUILayout.BeginHorizontal(UnityEngine.GUILayout.Height(70f));
    ExpandedRung("LOOK",0,workflow,$"{checkedCandidates} found");
    ExpandedRail(0,workflow);
    ExpandedRung("VALIDATE",1,workflow,$"{checkedValid} valid");
    ExpandedRail(1,workflow);
    ExpandedRung("LOAD",2,workflow,workflow.Active?.Version??"none");
    ExpandedRail(2,workflow);
    ExpandedRung("CONFIRM",3,workflow,workflow.Confirmed?"active":"waiting");
    UnityEngine.GUILayout.Space(8f);
    DrawUpdateAction(workflow);
    UnityEngine.GUILayout.FlexibleSpace();
    if(UnityEngine.GUILayout.Button(devChannel.Armed?"DEV ARMED":"ARM DEV",devChannel.Armed?primaryStyle:amberButtonStyle,UnityEngine.GUILayout.Width(96f),UnityEngine.GUILayout.Height(34f))) {
      if(devChannel.Armed) devChannel.Disarm(DateTimeOffset.UtcNow);
      else if(privateWorldConfirmed.Value) devChannel.Arm(DateTimeOffset.UtcNow);
      else { status="Private world confirmation required."; Report(status); }
    }
    if(UnityEngine.GUILayout.Button("OPEN STUDIO",blueButtonStyle,UnityEngine.GUILayout.Width(108f),UnityEngine.GUILayout.Height(34f))) OpenStudio();
    if(UnityEngine.GUILayout.Button(showDetails?"HIDE DETAILS":"DETAILS",dimButtonStyle,UnityEngine.GUILayout.Width(102f),UnityEngine.GUILayout.Height(34f))) {
      showDetails=!showDetails;
      if(showDetails) RefreshActivationHistory();
    }
    UnityEngine.GUILayout.EndHorizontal();
  }

  void ExpandedRung(string name,int index,WorkflowSnapshot workflow,string detail) {
    var done=workflow.Current==4||index<workflow.Current;
    var current=workflow.Current==index;
    UnityEngine.GUILayout.BeginVertical(UnityEngine.GUILayout.Width(78f));
    UnityEngine.GUILayout.Label(done?"✓":"",done?rungDoneStyle:current?rungCurrentStyle:rungWaitingStyle,UnityEngine.GUILayout.Width(22f),UnityEngine.GUILayout.Height(22f));
    UnityEngine.GUILayout.Label(name+"\n"+detail,done?rungNameDoneStyle:current?rungNameCurrentStyle:rungNameWaitingStyle,UnityEngine.GUILayout.Width(78f),UnityEngine.GUILayout.Height(36f));
    UnityEngine.GUILayout.EndVertical();
  }

  void ExpandedRail(int index,WorkflowSnapshot workflow) {
    var done=workflow.Current==4||index<workflow.Current;
    UnityEngine.GUILayout.BeginVertical(UnityEngine.GUILayout.Width(22f));
    UnityEngine.GUILayout.Space(10f);
    UnityEngine.GUILayout.Label(UnityEngine.GUIContent.none,done?railDoneStyle:railWaitingStyle,UnityEngine.GUILayout.Height(2f),UnityEngine.GUILayout.Width(22f));
    UnityEngine.GUILayout.EndVertical();
  }

  void DrawUpdateAction(WorkflowSnapshot workflow) {
    var label=!inboxChecked?"CHECK "+checkHotkey.Value:!workflow.Valid?"CHECK DIAGNOSTICS":!workflow.Confirmed?"PLAY "+loadHotkey.Value:"UP TO DATE";
    var style=!inboxChecked?blueButtonStyle:!workflow.Valid?amberButtonStyle:!workflow.Confirmed?primaryStyle:dimButtonStyle;
    var prior=UnityEngine.GUI.enabled;
    if(workflow.Confirmed) UnityEngine.GUI.enabled=false;
    if(UnityEngine.GUILayout.Button(label,style,UnityEngine.GUILayout.Width(178f),UnityEngine.GUILayout.Height(34f))) {
      status=!inboxChecked||!workflow.Valid?CheckForNew():LoadLatest();
      Report(status,statusIdle);
    }
    UnityEngine.GUI.enabled=prior;
  }

  string CharmState() {
    var aim=charms?.CapturedPreview;
    if(aim!=null) return aim.Allowed?"READY":"NOT READY";
    return string.IsNullOrWhiteSpace(charms?.Landed)?"CHECK":"LANDED";
  }
  bool CharmReady()=>charms?.CapturedPreview?.Allowed==true||!string.IsNullOrWhiteSpace(charms?.Landed);

  void DrawDetailsPopover(int id) {
    detailsScroll=UnityEngine.GUILayout.BeginScrollView(detailsScroll);
    DrawStatusCard();
    UnityEngine.GUILayout.Space(7);
    UnityEngine.GUILayout.Label("EXPERIENCE",sectionStyle,UnityEngine.GUILayout.Height(22));
    UnityEngine.GUILayout.Label(engine.DescribeProgress(),rowStyle,UnityEngine.GUILayout.Height(34));
    DrawRecentEvidence();
    UnityEngine.GUILayout.Space(7);
    DrawReady(charms.CapturedPreview);
    UnityEngine.GUILayout.Space(7);
    if(UnityEngine.GUILayout.Button((showMaintenance?"- ":"+ ")+"MACHINERY · CAPTURES, ARCANE SIGHT, VERSIONS & ROLLBACK",dimButtonStyle,UnityEngine.GUILayout.Height(27))) {
      showMaintenance=!showMaintenance;
      if(showMaintenance) RefreshActivationHistory();
    }
    if(showMaintenance) {
      DrawOutcomes();
      UnityEngine.GUILayout.Space(7);
      DrawArcaneSight();
      UnityEngine.GUILayout.Space(7);
      DrawMaintenance();
    }
    UnityEngine.GUILayout.EndScrollView();
  }

  // Deadlines and actionable warnings share one movable anchor. The engine supplies a
  // stable warning key and explicitly clears it; this surface never parses prose for state.
  void DrawAlertAnchor() {
    var deadline=engine?.Deadline();
    var warning=string.IsNullOrWhiteSpace(deadline)?engine?.CurrentAlert():null;
    var line=string.IsNullOrWhiteSpace(deadline)?warning?.Text:deadline;
    if(string.IsNullOrWhiteSpace(line)) return;
    var style=!string.IsNullOrWhiteSpace(deadline)&&engine.DeadlineUrgent()?deadlineUrgentStyle:deadlineStyle;
    var scale=UnityEngine.Mathf.Clamp(UnityEngine.Screen.height/900f,1f,2.5f);
    var matrix=UnityEngine.GUI.matrix;
    UnityEngine.GUI.matrix=UnityEngine.Matrix4x4.TRS(UnityEngine.Vector3.zero,UnityEngine.Quaternion.identity,new UnityEngine.Vector3(scale,scale,1f));
    var size=style.CalcSize(new UnityEngine.GUIContent(line));
    var screenWidth=UnityEngine.Screen.width/scale;
    var screenHeight=UnityEngine.Screen.height/scale;
    var x=UnityEngine.Mathf.Clamp(alertAnchorX.Value,.05f,.95f)*screenWidth-size.x/2f;
    var y=UnityEngine.Mathf.Clamp(alertAnchorY.Value,.05f,.85f)*screenHeight;
    var rect=new UnityEngine.Rect(UnityEngine.Mathf.Clamp(x,0f,screenWidth-size.x),UnityEngine.Mathf.Clamp(y,0f,screenHeight-size.y),size.x,size.y);
    UnityEngine.GUI.Label(rect,line,style);
    if(barExpanded) HandleAlertDrag(rect,size,screenWidth,screenHeight);
    UnityEngine.GUI.matrix=matrix;
  }

  void HandleAlertDrag(UnityEngine.Rect rect,UnityEngine.Vector2 size,float screenWidth,float screenHeight) {
    var evt=UnityEngine.Event.current;
    if(evt.type==UnityEngine.EventType.MouseDown&&evt.button==0&&rect.Contains(evt.mousePosition)) {
      alertDragging=true;
      alertDragOffset=evt.mousePosition-rect.position;
      evt.Use();
    } else if(alertDragging&&evt.type==UnityEngine.EventType.MouseDrag) {
      var origin=evt.mousePosition-alertDragOffset;
      alertAnchorX.Value=UnityEngine.Mathf.Clamp((origin.x+size.x/2f)/screenWidth,.05f,.95f);
      alertAnchorY.Value=UnityEngine.Mathf.Clamp(origin.y/screenHeight,.05f,.85f);
      evt.Use();
    } else if(alertDragging&&evt.type==UnityEngine.EventType.MouseUp) {
      alertDragging=false;
      Config.Save();
      evt.Use();
    }
  }
  // The status card answers "which revision is running?" title-first: the quest's authored
  // name is the shared CreatorLoopNotice.ActiveTitle fact, the state line is the shared Card
  // fact — with the idle "up to date" answer first-class, because session 1 of the Phase 3
  // exit lap read its absence as broken keys — and PACK/HASH/ACTIVATED identity waits behind
  // the card's own DETAILS disclosure, per the canvas's status-card states (02).
  void DrawStatusCard(){var active=ReadActive();if(active==null){UnityEngine.GUILayout.Label("Nothing is playing yet.",helpStyle,UnityEngine.GUILayout.Height(24));return;}var title=CreatorLoopNotice.ActiveTitle(TitleSource(),active)??active.PackId;var card=CreatorLoopNotice.Card(TitleSource(),active,inboxChecked);UnityEngine.GUILayout.BeginVertical(rowStyle);UnityEngine.GUILayout.BeginHorizontal();UnityEngine.GUILayout.Label(title,questTitleStyle);UnityEngine.GUILayout.Label(active.Version,chipStyle,UnityEngine.GUILayout.Height(24));UnityEngine.GUILayout.FlexibleSpace();UnityEngine.GUILayout.EndHorizontal();UnityEngine.GUILayout.Label("● "+(card?.Line??"Now playing"),CardStyle(card),UnityEngine.GUILayout.Height(26));if(UnityEngine.GUILayout.Button((showCardDetails?"− ":"+ ")+"DETAILS",dimButtonStyle,UnityEngine.GUILayout.Height(22)))showCardDetails=!showCardDetails;if(showCardDetails)UnityEngine.GUILayout.Label(CreatorLoopNotice.CardDetail(active),helpStyle,UnityEngine.GUILayout.Height(24));UnityEngine.GUILayout.EndVertical();}
  // The state's color rides the QuestCardState fact, never the line's text (the DeadlineUrgent rule).
  UnityEngine.GUIStyle CardStyle(QuestCardStatus card)=>card?.State switch{QuestCardState.UpdateReady=>stateReadyStyle,QuestCardState.Choice=>stateChoiceStyle,_=>playingStyle};
  // Titles come from the same inspection the check step runs; before any check this probes the
  // inbox once, quietly (a display read, not a check operation, so it writes no receipt).
  IReadOnlyList<PackCandidate> TitleSource(){if(available.Length>0)return available;if(quietInspected==null)try{quietInspected=packs.CheckInbox();}catch{quietInspected=Array.Empty<PackCandidate>();}return quietInspected;}
  // Mirrors the Lab InputGuard seams without referencing its assembly: every keystroke is a
  // hotkey unless something says otherwise, and chat, console, and text input say otherwise.
  static bool TypingInGame(){try{if(global::Console.IsVisible())return true;}catch{}try{if(TextInput.IsVisible())return true;}catch{}try{if(Chat.instance!=null&&Chat.instance.HasFocus())return true;}catch{}return false;}
  // One session-scoped line so the expanded bar is discoverable in the world it serves; only when
  // quest content actually exists, so a mod-less install stays silent.
  void WelcomeOnce(){if(welcomed||MessageHud.instance==null||Player.m_localPlayer==null)return;welcomed=true;if(HasQuestContent())Report("Comfy Quest ready. Press "+barHotkey.Value+" to expand the creator bar.");}
  bool HasQuestContent(){var now=UnityEngine.Time.realtimeSinceStartup;if(now<nextContentProbe)return hasQuestContent;nextContentProbe=now+1d;try{if(File.Exists(Path.Combine(runtimeRoot,"active","active-set.json")))return hasQuestContent=true;var inbox=Path.Combine(runtimeRoot,"inbox");return hasQuestContent=Directory.Exists(inbox)&&Directory.GetFiles(inbox,"*.questpack").Length>0;}catch{return hasQuestContent=false;}}
  static string CurrentWorldUid(){try{return ZNet.instance==null?string.Empty:ZNet.instance.GetWorldUID().ToString(System.Globalization.CultureInfo.InvariantCulture);}catch{return string.Empty;}}
  void PublishRunStatus(){if(runStatus==null||UnityEngine.Time.realtimeSinceStartup<nextRunStatus)return;nextRunStatus=UnityEngine.Time.realtimeSinceStartup+1d;try{runStatus.Write(new RuntimeRunStatusDocument{ObservedUtc=DateTimeOffset.UtcNow,Machine=Environment.MachineName,WorldUid=CurrentWorldUid(),Runs=engine?.CurrentRuns()??Array.Empty<RuntimeRunStatusEntry>()});}catch(Exception e){Logger.LogWarning("Run status unavailable: "+e.Message);}}
  static bool CreatorBuildModeEnabled(){try{var player=Player.m_localPlayer;return player!=null&&player.NoCostCheat()&&player.InGodMode();}catch{return false;}}
  static void SetCreatorBuildMode(bool enabled){var player=Player.m_localPlayer??throw new InvalidOperationException("Local player is not available.");Exception failure=null;try{player.SetNoPlacementCost(enabled);}catch(Exception e){failure=e;}try{player.SetGodMode(enabled);}catch(Exception e){if(failure==null)failure=e;}if(failure==null)return;if(enabled){try{player.SetNoPlacementCost(false);}catch{}try{player.SetGodMode(false);}catch{}}throw new InvalidOperationException("Creator build mode could not be changed completely.",failure);}
  void DrawDevChannel(){UnityEngine.GUILayout.Label("DEV CHANNEL",sectionStyle,UnityEngine.GUILayout.Height(22));if(devChannel.Armed){if(UnityEngine.GUILayout.Button("DEV CHANNEL ARMED · DISARM",primaryStyle,UnityEngine.GUILayout.Height(34)))devChannel.Disarm(DateTimeOffset.UtcNow);UnityEngine.GUILayout.Label("Studio revisions are validated and pulled into this private session.",helpStyle,UnityEngine.GUILayout.Height(30));return;}var allowed=privateWorldConfirmed.Value;var prior=UnityEngine.GUI.enabled;UnityEngine.GUI.enabled=allowed;if(UnityEngine.GUILayout.Button(allowed?"ARM DEV CHANNEL":"PRIVATE WORLD CONFIRMATION REQUIRED",allowed?amberButtonStyle:dimButtonStyle,UnityEngine.GUILayout.Height(34))&&allowed)devChannel.Arm(DateTimeOffset.UtcNow);UnityEngine.GUI.enabled=prior;UnityEngine.GUILayout.Label("Arming is session-only. Studio can publish bytes; only the game may activate them.",helpStyle,UnityEngine.GUILayout.Height(30));}
  void PollDevChannel(){if(devChannel==null||UnityEngine.Time.realtimeSinceStartup<nextDevPoll)return;nextDevPoll=UnityEngine.Time.realtimeSinceStartup+.5;try{var result=devChannel.Poll(DateTimeOffset.UtcNow,engine?.CurrentStageId());if(result.Activated){status=result.Message;AddOutcome(CreatorEvidenceKind.Plumbing,"DEV · "+result.Message);}else if(result.Message!=null&&result.Message.StartsWith("Dev revision rejected",StringComparison.Ordinal)){status=result.Message;AddOutcome(CreatorEvidenceKind.Warning,"DEV REJECTED · "+result.Message);}}catch(Exception e){status="Dev channel unavailable: "+e.Message;Logger.LogWarning(status);}}
  void DrawArcaneSight(){if(UnityEngine.GUILayout.Button(arcaneSight.Active?"ARCANE SIGHT - ON":"ARCANE SIGHT - OFF",blueButtonStyle,UnityEngine.GUILayout.Height(27)))arcaneSight.Toggle();UnityEngine.GUILayout.Label(arcaneSight.Describe(),rowStyle,UnityEngine.GUILayout.Height(36));}
  void OpenStudio(){
    if(!Uri.TryCreate(studioUrl.Value,UriKind.Absolute,out var uri)||uri.Scheme!="http"||!(uri.Host=="127.0.0.1"||uri.Host=="localhost"||uri.Host=="::1")){
      status="Studio URL rejected: loopback http required."; Report(status); return;
    }
    var active=ReadActive();
    var query=new List<string>();
    if(!string.IsNullOrWhiteSpace(uri.Query)) query.Add(uri.Query.TrimStart('?'));
    query.Add("stage="+(active==null?"author":"observe"));
    if(active!=null){
      query.Add("pack_id="+Uri.EscapeDataString(active.PackId??""));
      query.Add("version="+Uri.EscapeDataString(active.Version??""));
      query.Add("runtime_stage="+Uri.EscapeDataString(engine?.CurrentStageId()??""));
    }
    var target=new UriBuilder(uri){Query=string.Join("&",query.Where(value=>!string.IsNullOrWhiteSpace(value)))};
    UnityEngine.Application.OpenURL(target.Uri.AbsoluteUri);
  }
  // Three states, not two: CHECK (nothing captured), READY / NOT READY (a capture is standing, and
  // it is lit in the world so the player can see what it is), and LANDED after a cast — session 2's
  // strip snapped back to READY the instant a charm landed and re-armed silently on the next press.
  void DrawReady(RuntimeCharmBinding.AimPreview aim){UnityEngine.GUILayout.Label("CAST A CHARM",sectionStyle,UnityEngine.GUILayout.Height(22));var landed=charms.Landed;var ready=aim!=null&&aim.Allowed;var settled=aim==null&&!string.IsNullOrWhiteSpace(landed);UnityEngine.GUILayout.BeginHorizontal();UnityEngine.GUILayout.Label(ready?"READY":aim!=null?"NOT READY":settled?"LANDED":"CHECK",ready||settled?readyStyle:stepPendingStyle,UnityEngine.GUILayout.Width(120),UnityEngine.GUILayout.Height(54));UnityEngine.GUILayout.Label(aim!=null?aim.Summary:settled?landed:"Keep F9 open. Aim with the fixed center crosshair, then press ` to capture the target.",rowStyle,UnityEngine.GUILayout.Height(54));UnityEngine.GUILayout.EndHorizontal();UnityEngine.GUILayout.Label(ready?"`  ·  CAST CHARM":"`  ·  CHECK TARGET",ready?primaryStyle:blueButtonStyle,UnityEngine.GUILayout.Height(38));}
  void DrawOutcomes(){UnityEngine.GUILayout.Label("CAPTURES & OUTCOMES",sectionStyle,UnityEngine.GUILayout.Height(22));outcomeScroll=UnityEngine.GUILayout.BeginScrollView(outcomeScroll,rowStyle,UnityEngine.GUILayout.Height(74));if(outcomes.Count==0)UnityEngine.GUILayout.Label("No captures yet.",helpStyle);else foreach(var line in outcomes)DrawEvidenceRow(line);UnityEngine.GUILayout.EndScrollView();}
  void DrawRecentEvidence(){UnityEngine.GUILayout.Label("RECENT RUNTIME EVIDENCE",sectionStyle,UnityEngine.GUILayout.Height(22));IReadOnlyList<CreatorEvidenceLine> evidence=engine?.RecentEvidenceLines()??Array.Empty<CreatorEvidenceLine>();evidenceScroll=UnityEngine.GUILayout.BeginScrollView(evidenceScroll,rowStyle,UnityEngine.GUILayout.Height(88));if(evidence.Count==0)UnityEngine.GUILayout.Label("No gameplay evidence yet.",helpStyle);else foreach(var line in evidence)DrawEvidenceRow(line);UnityEngine.GUILayout.EndScrollView();}
  // The design's row taxonomy (canvas 05): a dim time gutter, a mark column, then the line,
  // with only the CAST row tinted — the tint rides the row group so the purple voice stays on
  // the text. The kind is a fact from the emission site, never parsed out of the rendered
  // copy. Marks stay in the default font's safe set.
  void DrawEvidenceRow(CreatorEvidenceLine line){if(line==null)return;UnityEngine.GUILayout.BeginHorizontal(line.Kind==CreatorEvidenceKind.Cast?castRowStyle:UnityEngine.GUIStyle.none);UnityEngine.GUILayout.Label(line.Stamp??"",stampStyle,UnityEngine.GUILayout.Width(58));UnityEngine.GUILayout.Label(Mark(line.Kind),EvidenceStyle(line.Kind),UnityEngine.GUILayout.Width(24));UnityEngine.GUILayout.Label(line.Text,EvidenceStyle(line.Kind));UnityEngine.GUILayout.EndHorizontal();}
  static string Mark(CreatorEvidenceKind kind)=>kind switch{CreatorEvidenceKind.Story=>"◆",CreatorEvidenceKind.Cast=>"◆",CreatorEvidenceKind.Warning=>"▲",_=>"·"};
  UnityEngine.GUIStyle EvidenceStyle(CreatorEvidenceKind kind)=>kind switch{CreatorEvidenceKind.Story=>storyStyle,CreatorEvidenceKind.Cast=>castStyle,CreatorEvidenceKind.Warning=>warnStyle,_=>plumbStyle};
  void HandleCharmGesture(){if(!charms.HasCapture){var aim=charms.Capture(privateWorldConfirmed.Value);AddOutcome(aim.Allowed?CreatorEvidenceKind.Plumbing:CreatorEvidenceKind.Warning,(aim.Allowed?"CHECK READY · ":"CHECK REJECTED · ")+aim.Summary);status=aim.Allowed?"Target captured. Press ` again to Cast Charm.":"Target rejected. Aim elsewhere and CHECK again.";Report(status);}else{status=charms.CastCaptured(privateWorldConfirmed.Value);if(!string.IsNullOrWhiteSpace(charms.Landed)){engine?.ResolveAlert("charm_unbound");engine?.ResolveAlert("binding_version");}AddOutcome(CreatorEvidenceKind.Cast,"CAST · "+status);Report(status);}}
  void AddOutcome(CreatorEvidenceKind kind,string value){outcomes.Add(new CreatorEvidenceLine{Kind=kind,Stamp=DateTime.Now.ToString("HH:mm:ss"),Text=value});if(outcomes.Count>20)outcomes.RemoveAt(0);outcomeScroll.y=float.MaxValue;}
  void DrawUpdateWorkflow(){UnityEngine.GUILayout.Label("CONTENT UPDATE",sectionStyle,UnityEngine.GUILayout.Height(22));var active=ReadActive();var valid=inboxChecked&&checkedCandidates==checkedValid;var latest=available.Length>0?available[0]:null;var confirmed=active!=null&&latest!=null&&active.PackId==latest.Manifest.PackId&&active.Version==latest.Manifest.Version&&string.Equals(active.ContentHash,latest.ContentHash,StringComparison.OrdinalIgnoreCase);UnityEngine.GUILayout.BeginHorizontal();Rung("LOOK",inboxChecked,!inboxChecked,inboxChecked?$"{checkedCandidates} found":"inbox");Rail(inboxChecked);Rung("VALIDATE",valid,inboxChecked&&!valid,inboxChecked?$"{checkedValid} valid":"waiting");Rail(valid);Rung("LOAD",confirmed,valid&&!confirmed,active==null?"none":active.Version);Rail(confirmed);Rung("CONFIRM",confirmed,false,confirmed?"active":"waiting");UnityEngine.GUILayout.EndHorizontal();if(!inboxChecked){if(UnityEngine.GUILayout.Button("Check for updates · "+checkHotkey.Value,blueButtonStyle,UnityEngine.GUILayout.Height(36))){status=CheckForNew();Report(status,statusIdle);}}else if(!valid){if(UnityEngine.GUILayout.Button("Check again · diagnostics present",amberButtonStyle,UnityEngine.GUILayout.Height(36))){status=CheckForNew();Report(status,statusIdle);}}else if(!confirmed){if(UnityEngine.GUILayout.Button("Load validated update · "+loadHotkey.Value,primaryStyle,UnityEngine.GUILayout.Height(36))){status=LoadLatest();Report(status,statusIdle);}}else{var prior=UnityEngine.GUI.enabled;UnityEngine.GUI.enabled=false;UnityEngine.GUILayout.Button("Up to date · "+active.Version,dimButtonStyle,UnityEngine.GUILayout.Height(36));UnityEngine.GUI.enabled=prior;}UnityEngine.GUILayout.Label(status,plumbStyle,UnityEngine.GUILayout.Height(30));if(!string.IsNullOrWhiteSpace(statusDetail))UnityEngine.GUILayout.Label(statusDetail,helpStyle,UnityEngine.GUILayout.Height(24));}
  // Ladder grammar from the canvas (03): circles joined by rails, not filled bars. A done rung
  // is a Ready-green filled circle (its ✓ is decoration — the fill alone carries done if the
  // glyph is missing from the game font), the current rung fills solid amber, waiting stays a
  // hollow ring; a rail takes the color of the rung it leaves. The rung is state; the one
  // button below the ladder is the action.
  void Rung(string name,bool done,bool current,string detail){UnityEngine.GUILayout.BeginVertical(UnityEngine.GUILayout.Width(78));UnityEngine.GUILayout.BeginHorizontal();UnityEngine.GUILayout.FlexibleSpace();UnityEngine.GUILayout.Label(done?"✓":"",done?rungDoneStyle:current?rungCurrentStyle:rungWaitingStyle,UnityEngine.GUILayout.Width(22),UnityEngine.GUILayout.Height(22));UnityEngine.GUILayout.FlexibleSpace();UnityEngine.GUILayout.EndHorizontal();UnityEngine.GUILayout.Label(name+"\n"+detail,done?rungNameDoneStyle:current?rungNameCurrentStyle:rungNameWaitingStyle,UnityEngine.GUILayout.Height(30));UnityEngine.GUILayout.EndVertical();}
  void Rail(bool done){UnityEngine.GUILayout.BeginVertical();UnityEngine.GUILayout.Space(10);UnityEngine.GUILayout.Label(UnityEngine.GUIContent.none,done?railDoneStyle:railWaitingStyle,UnityEngine.GUILayout.Height(2),UnityEngine.GUILayout.ExpandWidth(true));UnityEngine.GUILayout.EndVertical();}
  void DrawMaintenance(){if(available.Length>0){selectedVersion=Math.Max(0,Math.Min(selectedVersion,available.Length-1));var selected=available[selectedVersion];UnityEngine.GUILayout.Label($"CERTIFIED VERSION · {selected.Manifest.PackId}  ·  {selected.Manifest.Version}\n{selected.ContentHash}",rowStyle,UnityEngine.GUILayout.Height(46));UnityEngine.GUILayout.BeginHorizontal();if(UnityEngine.GUILayout.Button("Next certified",dimButtonStyle))selectedVersion=(selectedVersion+1)%available.Length;if(UnityEngine.GUILayout.Button("Load selected",amberButtonStyle)){status=LoadSelected();Report(status);RefreshActivationHistory();}UnityEngine.GUILayout.EndHorizontal();}else UnityEngine.GUILayout.Label("Run Check for updates to list certified production versions.",rowStyle);if(activationHistory.Length==0){UnityEngine.GUILayout.Label("No earlier activation epochs are available.",helpStyle);return;}selectedActivation=Math.Max(0,Math.Min(selectedActivation,activationHistory.Length-1));var prior=activationHistory[selectedActivation];UnityEngine.GUILayout.Label($"EARLIER ACTIVATION · {prior.PackId}  ·  {prior.Version} · {prior.ActivationId.Substring(prior.ActivationId.Length-8)}\n{prior.ContentHash}",rowStyle,UnityEngine.GUILayout.Height(46));UnityEngine.GUILayout.BeginHorizontal();if(UnityEngine.GUILayout.Button("Next earlier",dimButtonStyle))selectedActivation=(selectedActivation+1)%activationHistory.Length;if(UnityEngine.GUILayout.Button("Rollback to this activation",amberButtonStyle)){status=Rollback(prior.ActivationId);Report(status);RefreshActivationHistory();}UnityEngine.GUILayout.EndHorizontal();}
  void RefreshActivationHistory(){try{activationHistory=packs.ActivationHistory().ToArray();selectedActivation=Math.Max(0,Math.Min(selectedActivation,Math.Max(0,activationHistory.Length-1)));}catch(Exception e){activationHistory=Array.Empty<ActiveSet>();status="Activation history unavailable: "+e.Message;}}
  ActiveSet ReadActive(){try{var path=Path.Combine(runtimeRoot,"active","active-set.json");return File.Exists(path)?Newtonsoft.Json.JsonConvert.DeserializeObject<ActiveSet>(File.ReadAllText(path)):null;}catch{return null;}}
  // Tokens from docs/design/creator-loop-tokens.md. The grammar: amber is the one action
  // that changes what's running, steel is safe/repeatable, green is state and never a
  // button, CAST purple appears only for the charm moment.
  void EnsureStyles(){if(windowStyle!=null)return;windowBackground=Solid("runtime-window",new UnityEngine.Color(.020f,.031f,.063f,.985f));rowBackground=Solid("runtime-row",new UnityEngine.Color(.043f,.067f,.125f,.98f));helpBackground=Solid("runtime-help",new UnityEngine.Color(.031f,.051f,.094f,.98f));greenBackground=Solid("runtime-green",new UnityEngine.Color(.071f,.161f,.102f,1f));greenGlowBackground=Solid("runtime-green-glow",new UnityEngine.Color(.494f,.769f,.510f,1f));blueBackground=Solid("runtime-blue",new UnityEngine.Color(.086f,.157f,.235f,1f));amberBackground=Solid("runtime-amber",new UnityEngine.Color(.290f,.200f,.078f,1f));primaryBackground=Solid("runtime-primary",new UnityEngine.Color(.914f,.659f,.247f,1f));castRowBackground=Solid("runtime-cast-row",new UnityEngine.Color(.631f,.361f,1f,.10f));dimBackground=Solid("runtime-dim",new UnityEngine.Color(.114f,.165f,.251f,1f));circleDoneBackground=Ring("runtime-rung-done",new UnityEngine.Color(.071f,.161f,.102f,1f),new UnityEngine.Color(.243f,.420f,.278f,1f));circleCurrentBackground=Ring("runtime-rung-current",new UnityEngine.Color(.914f,.659f,.247f,1f),new UnityEngine.Color(.957f,.753f,.380f,1f));circleWaitingBackground=Ring("runtime-rung-waiting",UnityEngine.Color.clear,new UnityEngine.Color(.165f,.220f,.329f,1f));railDoneBackground=Solid("runtime-rail-done",new UnityEngine.Color(.243f,.420f,.278f,1f));railWaitingBackground=Solid("runtime-rail-waiting",new UnityEngine.Color(.133f,.188f,.290f,1f));windowStyle=new UnityEngine.GUIStyle(UnityEngine.GUI.skin.window){fontSize=15,fontStyle=UnityEngine.FontStyle.Bold,padding=new UnityEngine.RectOffset(12,12,30,12)};Pin(windowStyle,windowBackground,UnityEngine.Color.white);sectionStyle=LabelStyle(null,new UnityEngine.Color(.306f,.345f,.439f,1f),UnityEngine.FontStyle.Bold,11);rowStyle=LabelStyle(rowBackground,new UnityEngine.Color(.914f,.894f,.847f,1f),UnityEngine.FontStyle.Normal,13);rowStyle.wordWrap=true;helpStyle=LabelStyle(helpBackground,new UnityEngine.Color(.604f,.639f,.710f,1f),UnityEngine.FontStyle.Normal,12);helpStyle.wordWrap=true;readyStyle=LabelStyle(greenGlowBackground,new UnityEngine.Color(.043f,.122f,.063f,1f),UnityEngine.FontStyle.Bold,17);readyStyle.alignment=UnityEngine.TextAnchor.MiddleCenter;primaryStyle=ButtonStyle(primaryBackground,new UnityEngine.Color(.098f,.063f,.024f,1f),14);blueButtonStyle=ButtonStyle(blueBackground,new UnityEngine.Color(.663f,.784f,.894f,1f),13);amberButtonStyle=ButtonStyle(amberBackground,new UnityEngine.Color(.957f,.753f,.380f,1f),13);dimButtonStyle=ButtonStyle(dimBackground,new UnityEngine.Color(.604f,.639f,.710f,1f),13);stepPendingStyle=LabelStyle(dimBackground,new UnityEngine.Color(.306f,.345f,.439f,1f),UnityEngine.FontStyle.Bold,12);stepPendingStyle.alignment=UnityEngine.TextAnchor.MiddleCenter;rungDoneStyle=CircleStyle(circleDoneBackground,new UnityEngine.Color(.494f,.769f,.510f,1f));rungCurrentStyle=CircleStyle(circleCurrentBackground,new UnityEngine.Color(.098f,.063f,.024f,1f));rungWaitingStyle=CircleStyle(circleWaitingBackground,new UnityEngine.Color(.365f,.404f,.474f,1f));rungNameDoneStyle=RungNameStyle(new UnityEngine.Color(.435f,.659f,.455f,1f));rungNameCurrentStyle=RungNameStyle(new UnityEngine.Color(.957f,.753f,.380f,1f));rungNameWaitingStyle=RungNameStyle(new UnityEngine.Color(.365f,.404f,.474f,1f));railDoneStyle=BarStyle(railDoneBackground);railWaitingStyle=BarStyle(railWaitingBackground);stampStyle=LabelStyle(null,new UnityEngine.Color(.306f,.345f,.439f,1f),UnityEngine.FontStyle.Normal,11);castRowStyle=BarStyle(castRowBackground);deadlineBackground=Framed("runtime-deadline",new UnityEngine.Color(.031f,.047f,.094f,.94f),new UnityEngine.Color(.914f,.659f,.247f,1f));deadlineUrgentBackground=Framed("runtime-deadline-urgent",new UnityEngine.Color(.482f,.075f,.075f,.96f),new UnityEngine.Color(.988f,.729f,.729f,1f));deadlineStyle=PillStyle(deadlineBackground,new UnityEngine.Color(.976f,.816f,.514f,1f));deadlineUrgentStyle=PillStyle(deadlineUrgentBackground,UnityEngine.Color.white);storyStyle=LabelStyle(null,new UnityEngine.Color(.894f,.871f,.812f,1f),UnityEngine.FontStyle.Bold,13);storyStyle.wordWrap=true;castStyle=LabelStyle(null,new UnityEngine.Color(.812f,.663f,1f,1f),UnityEngine.FontStyle.Bold,13);castStyle.wordWrap=true;warnStyle=LabelStyle(null,new UnityEngine.Color(.910f,.761f,.478f,1f),UnityEngine.FontStyle.Normal,13);warnStyle.wordWrap=true;plumbStyle=LabelStyle(null,new UnityEngine.Color(.420f,.455f,.533f,1f),UnityEngine.FontStyle.Normal,12);plumbStyle.wordWrap=true;questTitleStyle=LabelStyle(null,new UnityEngine.Color(.937f,.914f,.863f,1f),UnityEngine.FontStyle.Bold,22);playingStyle=LabelStyle(null,new UnityEngine.Color(.608f,.831f,.624f,1f),UnityEngine.FontStyle.Bold,13);chipStyle=LabelStyle(dimBackground,new UnityEngine.Color(.725f,.761f,.831f,1f),UnityEngine.FontStyle.Bold,12);chipStyle.alignment=UnityEngine.TextAnchor.MiddleCenter;stateReadyStyle=LabelStyle(null,new UnityEngine.Color(.957f,.753f,.380f,1f),UnityEngine.FontStyle.Bold,13);stateChoiceStyle=LabelStyle(null,new UnityEngine.Color(.663f,.784f,.894f,1f),UnityEngine.FontStyle.Bold,13);}
  static UnityEngine.GUIStyle LabelStyle(UnityEngine.Texture2D background,UnityEngine.Color color,UnityEngine.FontStyle weight,int size){var style=new UnityEngine.GUIStyle(UnityEngine.GUI.skin.label){padding=new UnityEngine.RectOffset(8,8,5,5),margin=new UnityEngine.RectOffset(1,1,1,1),alignment=UnityEngine.TextAnchor.MiddleLeft,fontStyle=weight,fontSize=size};style.normal.background=background;style.normal.textColor=color;return style;}
  static UnityEngine.GUIStyle ButtonStyle(UnityEngine.Texture2D background,UnityEngine.Color color,int size){var style=new UnityEngine.GUIStyle(UnityEngine.GUI.skin.button){fontSize=size,fontStyle=UnityEngine.FontStyle.Bold,padding=new UnityEngine.RectOffset(8,8,6,6)};Pin(style,background,color);return style;}
  static void Pin(UnityEngine.GUIStyle style,UnityEngine.Texture2D background,UnityEngine.Color color){style.normal.background=background;style.hover.background=background;style.active.background=background;style.focused.background=background;style.onNormal.background=background;style.onHover.background=background;style.onActive.background=background;style.onFocused.background=background;style.normal.textColor=color;style.hover.textColor=color;style.active.textColor=color;style.focused.textColor=color;style.onNormal.textColor=color;style.onHover.textColor=color;style.onActive.textColor=color;style.onFocused.textColor=color;}
  static UnityEngine.Texture2D Solid(string name,UnityEngine.Color color){var texture=new UnityEngine.Texture2D(1,1,UnityEngine.TextureFormat.RGBA32,false){name=name,hideFlags=UnityEngine.HideFlags.HideAndDontSave};texture.SetPixel(0,0,color);texture.Apply(false,true);return texture;}
  static UnityEngine.Texture2D Ring(string name,UnityEngine.Color fill,UnityEngine.Color ring){var texture=new UnityEngine.Texture2D(22,22,UnityEngine.TextureFormat.RGBA32,false){name=name,hideFlags=UnityEngine.HideFlags.HideAndDontSave};for(var y=0;y<22;y++)for(var x=0;x<22;x++){var dx=x-10.5f;var dy=y-10.5f;var d=UnityEngine.Mathf.Sqrt(dx*dx+dy*dy);texture.SetPixel(x,y,d>10.5f?UnityEngine.Color.clear:d>8.8f?ring:fill);}texture.Apply(false,true);return texture;}
  // A bordered pill: a nine-sliced texture whose two-pixel edge is the border, so the deadline can
  // never be read as one more line of a host HUD band the way session 2's flat strip was.
  static UnityEngine.Texture2D Framed(string name,UnityEngine.Color fill,UnityEngine.Color border){var texture=new UnityEngine.Texture2D(16,16,UnityEngine.TextureFormat.RGBA32,false){name=name,hideFlags=UnityEngine.HideFlags.HideAndDontSave,filterMode=UnityEngine.FilterMode.Point,wrapMode=UnityEngine.TextureWrapMode.Clamp};for(var y=0;y<16;y++)for(var x=0;x<16;x++)texture.SetPixel(x,y,x<2||y<2||x>13||y>13?border:fill);texture.Apply(false,true);return texture;}
  static UnityEngine.GUIStyle PillStyle(UnityEngine.Texture2D background,UnityEngine.Color color){var style=new UnityEngine.GUIStyle(UnityEngine.GUI.skin.label){padding=new UnityEngine.RectOffset(24,24,10,10),margin=new UnityEngine.RectOffset(0,0,0,0),alignment=UnityEngine.TextAnchor.MiddleCenter,fontStyle=UnityEngine.FontStyle.Bold,fontSize=20,border=new UnityEngine.RectOffset(4,4,4,4)};style.normal.background=background;style.normal.textColor=color;return style;}
  static UnityEngine.GUIStyle CircleStyle(UnityEngine.Texture2D background,UnityEngine.Color glyph){var style=new UnityEngine.GUIStyle(UnityEngine.GUI.skin.label){padding=new UnityEngine.RectOffset(0,0,0,0),margin=new UnityEngine.RectOffset(0,0,0,0),alignment=UnityEngine.TextAnchor.MiddleCenter,fontStyle=UnityEngine.FontStyle.Bold,fontSize=13};style.normal.background=background;style.normal.textColor=glyph;return style;}
  static UnityEngine.GUIStyle RungNameStyle(UnityEngine.Color color){var style=new UnityEngine.GUIStyle(UnityEngine.GUI.skin.label){padding=new UnityEngine.RectOffset(0,0,2,0),margin=new UnityEngine.RectOffset(0,0,0,0),alignment=UnityEngine.TextAnchor.MiddleCenter,fontStyle=UnityEngine.FontStyle.Bold,fontSize=11};style.normal.textColor=color;return style;}
  static UnityEngine.GUIStyle BarStyle(UnityEngine.Texture2D background){var style=new UnityEngine.GUIStyle{padding=new UnityEngine.RectOffset(0,0,0,0),margin=new UnityEngine.RectOffset(1,1,1,1)};style.normal.background=background;return style;}
  void SetBarExpanded(bool expanded){barExpanded=expanded;if(expanded){RuntimeInputPatches.Acquire();arcaneSight?.Enable();}else{showDetails=false;alertDragging=false;RuntimeInputPatches.Release();arcaneSight?.Disable();charms?.Release();}}
  // Creator plumbing speaks TopLeft, matching the Lab's convention; Center belongs to the
  // authored story and the countdown, and the plumbing never competes with it again.
  // An idle response re-asserts through the HUD's own repeat affordance: MessageHud.UpdateMessage
  // merges a repeated TopLeft text into the line already on screen and only renders its counter
  // once the summed amounts exceed one, so amount 0 made session 1's idle F10/F11 presses
  // invisible. Amount 1 makes the second identical press read "… x2" — no new channel.
  // The log line is the emission evidence: session 1 could not distinguish "HUD not live"
  // from "shown and missed", because the null branch was silent.
  void Report(string message,bool reassert=false){var hud=MessageHud.instance;Logger.LogInfo(hud==null?message+" · hud_absent":message);try{if(hud!=null)hud.ShowMessage(MessageHud.MessageType.TopLeft,message,reassert?1:0);}catch(Exception e){Logger.LogWarning("HUD status unavailable: "+e.Message);}}
  void OnDestroy(){try{SetCreatorBuildMode(false);}catch{}try{devChannel?.Disarm(DateTimeOffset.UtcNow);}catch{}RuntimeInputPatches.Release();arcaneSight?.Disable();try{charms?.Release();}catch{}RuntimeCoreActionPatches.Reset();RuntimeHarvestPatches.Reset();RuntimeEventRouter.Reset();RuntimeEventRouter.Engine=null;Destroy(ref windowBackground);Destroy(ref rowBackground);Destroy(ref helpBackground);Destroy(ref greenBackground);Destroy(ref greenGlowBackground);Destroy(ref blueBackground);Destroy(ref amberBackground);Destroy(ref primaryBackground);Destroy(ref castRowBackground);Destroy(ref dimBackground);Destroy(ref deadlineBackground);Destroy(ref deadlineUrgentBackground);Destroy(ref circleDoneBackground);Destroy(ref circleCurrentBackground);Destroy(ref circleWaitingBackground);Destroy(ref railDoneBackground);Destroy(ref railWaitingBackground);}
  static void Destroy(ref UnityEngine.Texture2D texture){if(texture!=null)UnityEngine.Object.Destroy(texture);texture=null;}
  // UI/input and world mutation remain outside the contract. The dev pull loop is inert until the
  // creator explicitly arms it in-game for this process session.
  // Both entrypoints speak through CreatorLoopNotice — the plugin renders, it never composes
  // creator copy inline. F10 states what checking proved; activation language belongs to F11.
  public string CheckForNew(){try{var c=RefreshInbox();var notice=CreatorLoopNotice.Check(c,ReadActive(),loadHotkey.Value.ToString(),barHotkey.Value.ToString());statusDetail=notice.Detail;statusIdle=notice.Idle;return notice.Headline;}catch(Exception e){receipts.Write(new RuntimeReceipt{Operation="check",Status="rejected",Error=e.Message,EvidenceKind=CreatorEvidenceLine.KindName(CreatorEvidenceKind.Warning)});statusDetail=e.Message;statusIdle=false;return CreatorLoopNotice.CheckFailed(e.Message,barHotkey.Value.ToString()).Headline;}}
  IReadOnlyList<PackCandidate> RefreshInbox(){var c=packs.CheckInbox();var valid=c.Count(x=>x.IsValid);inboxChecked=true;checkedCandidates=c.Count;checkedValid=valid;available=c.Where(x=>x.IsValid).OrderByDescending(x=>SemanticVersion.Parse(x.Manifest.Version)).ToArray();selectedVersion=0;receipts.Write(new RuntimeReceipt{Operation="check",Status=c.Count==valid?"accepted":"diagnostics",CandidateCount=c.Count,ValidCount=valid,Diagnostics=c.SelectMany(x=>x.Diagnostics).ToArray()});foreach(var candidate in available)receipts.Write(new RuntimeReceipt{Operation="check",Status="accepted",PackId=candidate.Manifest.PackId,Version=candidate.Manifest.Version,ContentHash=candidate.ContentHash,CandidateCount=c.Count,ValidCount=valid,Diagnostics=Array.Empty<ContractDiagnostic>()});return c;}
  // F11 refreshes the same inbox state F10 populates, so the bar's 1-2-3-4 ladder is a
  // fact about what the keys did rather than a narrative they bypass.
  public string LoadLatest(){try{RefreshInbox();var before=ReadActive();var c=packs.LoadLatest();if(c==null){receipts.Write(new RuntimeReceipt{Operation="load",Status="rejected",Error="no_compatible_pack"});statusDetail="no_compatible_pack";var empty=CreatorLoopNotice.NothingToLoad(checkHotkey.Value.ToString());statusIdle=empty.Idle;return empty.Headline;}var active=ReadActive();if(before!=null&&active!=null&&string.Equals(before.ActivationId,active.ActivationId,StringComparison.Ordinal)){receipts.Write(new RuntimeReceipt{Operation="load",Status="already_active",PackId=c.Manifest.PackId,Version=c.Manifest.Version,ContentHash=c.ContentHash,ActivationId=active.ActivationId,Diagnostics=Array.Empty<ContractDiagnostic>()});var current=CreatorLoopNotice.AlreadyPlaying(c);statusDetail=current.Detail;statusIdle=current.Idle;return current.Headline;}receipts.Write(new RuntimeReceipt{Operation="load",Status="activated",PackId=c.Manifest.PackId,Version=c.Manifest.Version,ContentHash=c.ContentHash,ActivationId=active?.ActivationId,Diagnostics=Array.Empty<ContractDiagnostic>()});var notice=CreatorLoopNotice.Loaded(c,active,engine?.OrphanedBindingsAfterActivation()??0);statusDetail=notice.Detail;statusIdle=notice.Idle;return notice.Headline;}catch(Exception e){receipts.Write(new RuntimeReceipt{Operation="load",Status="rejected",Error=e.Message,EvidenceKind=CreatorEvidenceLine.KindName(CreatorEvidenceKind.Warning)});statusDetail=e.Message;statusIdle=false;return CreatorLoopNotice.LoadFailed(e.Message,barHotkey.Value.ToString()).Headline;}}
  public string LoadSelected(){if(available.Length==0)return "Check for new first.";var selected=available[Math.Max(0,Math.Min(selectedVersion,available.Length-1))];return Activate("load_selected",()=>packs.LoadVersion(selected.Manifest.PackId,selected.Manifest.Version));}
  public string Rollback()=>Activate("rollback",()=>packs.Rollback());
  public string Rollback(string activationId)=>Activate("rollback",()=>packs.Rollback(activationId));
  string Activate(string operation,Func<PackCandidate> choose){try{var c=choose();if(c==null){receipts.Write(new RuntimeReceipt{Operation=operation,Status="rejected",Error="version_unavailable"});return operation=="rollback"?"No older activation available.":"Selected version unavailable.";}var active=packs.ReadActive();var correlation=operation+"-"+Guid.NewGuid().ToString("N");receipts.Write(new RuntimeReceipt{Operation=operation,Status="activated",PackId=c.Manifest.PackId,Version=c.Manifest.Version,ContentHash=c.ContentHash,ActivationId=active?.ActivationId,CorrelationId=correlation,Diagnostics=Array.Empty<ContractDiagnostic>()});if(operation=="rollback"&&string.Equals(active?.SourceChannel,"dev",StringComparison.OrdinalIgnoreCase))foreach(var receipt in charms.RebindDevActive(active,correlation))receipts.Write(receipt);return $"{(operation=="rollback"?"Rolled back to":"Activated")} {c.Manifest.PackId} {c.Manifest.Version}";}catch(Exception e){receipts.Write(new RuntimeReceipt{Operation=operation,Status="rejected",Error=e.Message});return operation+" failed: "+e.Message;}}
}
