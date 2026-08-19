namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using ComfyQuestContracts;
using Newtonsoft.Json;

sealed class RuntimeExperienceEngine {
  const string Prefix = "comfyQuestRuntime.";
  readonly string root;
  readonly RuntimeReceiptStore receipts;
  readonly ActionExecutionLedger ledger;
  readonly SpawnExecutionStore spawned;
  readonly WorkflowStateStore workflows;
  readonly DurableTimerStore timers;
  readonly Func<bool> privateConfirmed;
  readonly Dictionary<string, DateTimeOffset> recentEventKeys =
      new(StringComparer.Ordinal);
  DateTimeOffset nextTimerPoll;
  Active cachedActive;
  DateTime cachedActiveWriteUtc;
  DateTime cachedPackageWriteUtc;
  long cachedPackageLength;
  WearNTear[] cachedBindings = Array.Empty<WearNTear>();
  string cachedBindingContentHash;
  double nextBindingRefresh;

  public RuntimeExperienceEngine(
      string runtimeRoot,
      RuntimeReceiptStore receiptStore,
      Func<bool> isPrivateConfirmed) {
    root = runtimeRoot;
    receipts = receiptStore;
    ledger = new ActionExecutionLedger(root);
    spawned = new SpawnExecutionStore(root);
    workflows = new WorkflowStateStore(root);
    timers = new DurableTimerStore(root);
    privateConfirmed = isPrivateConfirmed;
  }

  public void OnEasyEvent(RuntimeEvent evt) {
    if (evt != null) OnEvent(evt);
  }

  public void Tick() {
    var now = DateTimeOffset.UtcNow;
    if (now < nextTimerPoll) return;
    nextTimerPoll = now.AddSeconds(1);
    foreach (var timer in timers.Due(now)) {
      OnEvent(new RuntimeEvent {
        Name = ExperienceSchema.TimerElapsedEvent,
        SourceId = timer.Identity.BindingZdo,
        At = now,
        Fields = new Dictionary<string, string> { ["timer_id"] = timer.TimerId },
      });
      timers.Acknowledge(timer.Key);
    }
  }

  public void OnEvent(RuntimeEvent evt) {
    evt = RuntimeEventPolicy.Normalize(evt);
    if (evt == null) return;
    try {
      if (!TryLoad(out var active, out var diagnostic)) {
        Write("transition", diagnostic, null, null, null);
        return;
      }
      // The high-frequency lane stops here: no scene walk, receipt, or workflow write.
      if (!active.Subscriptions.Contains(evt.Name) || IsDuplicate(evt)) return;
      var authority = CharmPolicy.CanMutate(World());
      if (!authority.Allowed) {
        Write("transition", authority.Diagnostic, null, null, null);
        return;
      }

      var foundBinding = false;
      foreach (var wear in Bindings(active)) {
        if (wear == null) continue;
        var view = wear.GetComponent<ZNetView>();
        var zdo = view == null ? null : view.GetZDO();
        if (zdo == null || !view.IsOwner()
            || (!string.IsNullOrWhiteSpace(evt.SourceId)
                && evt.SourceId != zdo.m_uid.ToString())) continue;
        var reference = Read(zdo);
        if (reference == null
            || reference.PackId != active.PackId
            || reference.ExperienceId != active.Document.Id
            || reference.Version != active.Version
            || reference.ContentHash != active.ContentHash) continue;

        foundBinding = true;
        var identity = Identity(zdo, active);
        var decision = workflows.Begin(identity, active.Document, evt);
        if (decision == null) {
          var state = workflows.Get(identity);
          var stage = active.Document.Stages.FirstOrDefault(value => value.Id == state?.StageId);
          var route = stage?.Transitions?.OrderByDescending(value => value.Priority)
              .ThenBy(value => value.Id, StringComparer.Ordinal).FirstOrDefault();
          var progress = TriggerEvaluator.Measure(route?.When, state?.History);
          receipts.Write(EventReceipt(
              "ignored", active, zdo.m_uid.ToString(), evt, state?.StageId,
              state?.StageId, progress));
          continue;
        }

        var currentStage = active.Document.Stages.FirstOrDefault(value => value.Id == decision.StageId);
        if (currentStage == null) continue;
        var currentState = workflows.Get(identity);
        var matchedProgress = TriggerEvaluator.Measure(decision.Transition.When, currentState?.History);
        receipts.Write(EventReceipt(
            "matched", active, zdo.m_uid.ToString(), evt, currentStage.Id,
            decision.Transition.NextStage, matchedProgress));

        var succeeded = true;
        foreach (var action in decision.Transition.Actions ?? new())
          succeeded &= Execute(active, zdo, currentStage.Id, decision.Transition.Id, action);
        if (succeeded && !string.IsNullOrWhiteSpace(decision.Transition.NextStage)) {
          var next = active.Document.Stages.FirstOrDefault(
              value => value.Id == decision.Transition.NextStage);
          foreach (var action in next?.EntryActions ?? new())
            succeeded &= Execute(active, zdo, next.Id, "entry", action);
        }
        if (!succeeded) continue;
        if (workflows.Complete(decision)) {
          receipts.Write(new RuntimeReceipt {
            Operation = "transition",
            Status = string.IsNullOrWhiteSpace(decision.Transition.Outcome)
                ? "advanced" : decision.Transition.Outcome,
            PackId = active.PackId,
            Version = active.Version,
            ContentHash = active.ContentHash,
            BindingZdo = zdo.m_uid.ToString(),
            StageId = currentStage.Id,
            TransitionId = decision.Transition.Id,
            EventName = evt.Name,
            EventTarget = evt.Target,
            ActorRole = CooperativeEventContract.ActorRole(evt),
            CurrentStageId = currentStage.Id,
            NextStageId = decision.Transition.NextStage,
            CurrentCount = matchedProgress.Current,
            RequiredCount = matchedProgress.Required,
            Diagnostics = Array.Empty<ContractDiagnostic>(),
          });
        }
      }

      if (!foundBinding) {
        receipts.Write(EventReceipt("unbound", active, null, evt, null, null,
            new TriggerProgress { Current = 0, Required = 1 }));
      }
    } catch (Exception e) {
      Write("action", "runtime_event_failed", null, null, e.Message);
    }
  }

  bool IsDuplicate(RuntimeEvent evt) {
    if (string.IsNullOrWhiteSpace(evt.DedupeKey)) return false;
    var now = evt.At == default ? DateTimeOffset.UtcNow : evt.At;
    if (recentEventKeys.TryGetValue(evt.DedupeKey, out var prior)
        && now >= prior && now - prior < TimeSpan.FromSeconds(1)) return true;
    if (recentEventKeys.Count >= 512) {
      foreach (var key in recentEventKeys.OrderBy(value => value.Value)
          .Take(recentEventKeys.Count - 256).Select(value => value.Key).ToArray())
        recentEventKeys.Remove(key);
    }
    recentEventKeys[evt.DedupeKey] = now;
    return false;
  }

  IReadOnlyList<WearNTear> Bindings(Active active) {
    var now = UnityEngine.Time.realtimeSinceStartup;
    if (!string.Equals(cachedBindingContentHash, active.ContentHash, StringComparison.Ordinal)
        || now >= nextBindingRefresh) {
      cachedBindings = WearNTear.GetAllInstances().Where(value => value != null).ToArray();
      cachedBindingContentHash = active.ContentHash;
      nextBindingRefresh = now + 1.0;
    }
    return cachedBindings;
  }

  static RuntimeReceipt EventReceipt(
      string status,
      Active active,
      string bindingZdo,
      RuntimeEvent evt,
      string currentStage,
      string nextStage,
      TriggerProgress progress) => new() {
    Operation = "event",
    Status = status,
    PackId = active.PackId,
    Version = active.Version,
    ContentHash = active.ContentHash,
    BindingZdo = bindingZdo,
    EventName = evt.Name,
    EventTarget = evt.Target,
    ActorRole = CooperativeEventContract.ActorRole(evt),
    CurrentStageId = currentStage,
    NextStageId = nextStage,
    CurrentCount = progress?.Current,
    RequiredCount = progress?.Required,
    Diagnostics = Array.Empty<ContractDiagnostic>(),
  };

  bool Execute(
      Active active,
      ZDO zdo,
      string stage,
      string transition,
      ExperienceAction action) {
    try {
      var identity = Identity(zdo, active);
      var zdoId = zdo.m_uid.ToString();
      var key = string.Join("|", identity.Key, stage, transition, action.Id);
      if (!ledger.TryClaim(key)) {
        receipts.Write(ActionReceipt(
            "suppressed", "duplicate_suppressed", active, zdoId, stage, transition, action.Id));
        return true;
      }
      switch (action.Type) {
        case "message":
          MessageHud.instance?.ShowMessage(MessageHud.MessageType.Center, Param(action, "text"));
          break;
        case "timer_start":
          timers.Start(identity, Param(action, "timer_id"),
              DateTimeOffset.UtcNow.AddSeconds(IntParam(action, "seconds")));
          break;
        case "timer_cancel":
          timers.Cancel(identity, Param(action, "timer_id"));
          break;
        case "grant_item":
          if (!Grant(action)) throw new InvalidOperationException("grant_failed");
          break;
        case "spawn":
          if (!Spawn(active, zdo, action, key))
            throw new InvalidOperationException("spawn_failed");
          break;
        case "clear_spawned":
          Clear(active, Param(action, "action_id"), identity, stage, transition);
          break;
        default:
          throw new InvalidOperationException("action_not_implemented");
      }
      receipts.Write(ActionReceipt(
          "executed", null, active, zdoId, stage, transition, action.Id));
      return true;
    } catch (Exception e) {
      Write("action", e.Message, active, null, action.Id);
      return false;
    }
  }

  static string Param(ExperienceAction action, string name) =>
      action.Parameters != null && action.Parameters.TryGetValue(name, out var value)
          ? value.ToString() : "";

  static int IntParam(ExperienceAction action, string name) =>
      action.Parameters != null && action.Parameters.TryGetValue(name, out var value)
          ? value.ToObject<int>() : 0;

  static bool Grant(ExperienceAction action) {
    var item = Param(action, "item");
    var count = IntParam(action, "quantity");
    if (!MutationRegistry.TryGrant(item, count, out _)) return false;
    var prefab = ZNetScene.instance?.GetPrefab(item);
    if (prefab == null || prefab.GetComponent<ItemDrop>() == null
        || Player.m_localPlayer == null) return false;
    return Player.m_localPlayer.GetInventory().AddItem(prefab, count);
  }

  bool Spawn(Active active, ZDO binding, ExperienceAction action, string key) {
    var kind = Param(action, "kind");
    var prefabName = Param(action, "prefab");
    var count = IntParam(action, "count");
    var radius = IntParam(action, "radius");
    if (!MutationRegistry.CanSpawn(kind, prefabName)
        || count < 1 || count > 16 || radius < 0 || radius > 30) return false;
    var prefab = ZNetScene.instance?.GetPrefab(prefabName);
    if (prefab == null || !PrefabMatches(prefab, kind)) return false;
    var center = binding.GetPosition();
    for (var i = 0; i < count; i++) {
      var angle = (float)(i * Math.PI * 2 / Math.Max(1, count));
      var distance = Math.Min(radius, 2 + i / 4);
      var position = center + new UnityEngine.Vector3(
          (float)Math.Cos(angle) * distance, 0.5f, (float)Math.Sin(angle) * distance);
      var go = UnityEngine.Object.Instantiate(
          prefab, position, UnityEngine.Quaternion.identity);
      var view = go?.GetComponent<ZNetView>();
      var zdo = view?.GetZDO();
      if (zdo == null) return false;
      zdo.Set(Prefix + "spawnedContentHash", active.ContentHash);
      zdo.Set(Prefix + "spawnedActionId", action.Id);
      zdo.Set(Prefix + "spawnedActionKey", key);
      var piece = go.GetComponent<Piece>();
      if (piece != null && Player.m_localPlayer != null)
        piece.SetCreator(Player.m_localPlayer.GetPlayerID());
      spawned.Record(new SpawnedObject {
        ActionKey = key,
        ContentHash = active.ContentHash,
        ActionId = action.Id,
        UserId = zdo.m_uid.UserID,
        ObjectId = zdo.m_uid.ID,
      });
    }
    return true;
  }

  static bool PrefabMatches(UnityEngine.GameObject prefab, string kind) =>
      kind == "creature"
          ? prefab.GetComponent<Character>() != null && prefab.GetComponent<Player>() == null
          : kind == "item"
              ? prefab.GetComponent<ItemDrop>() != null
              : kind == "piece" && prefab.GetComponent<Piece>() != null;

  void Clear(
      Active active,
      string targetAction,
      WorkflowIdentity identity,
      string stage,
      string transition) {
    var records = spawned.ForOwnerAction(identity.Key, targetAction);
    var removed = new List<SpawnedObject>();
    foreach (var record in records) {
      var zdo = ZDOMan.instance?.GetZDO(new ZDOID(record.UserId, record.ObjectId));
      if (zdo == null) {
        removed.Add(record);
        continue;
      }
      if (record.ContentHash != active.ContentHash
          || zdo.GetString(Prefix + "spawnedContentHash", "") != record.ContentHash
          || zdo.GetString(Prefix + "spawnedActionId", "") != record.ActionId
          || zdo.GetString(Prefix + "spawnedActionKey", "") != record.ActionKey) continue;
      var view = ZNetScene.instance?.FindInstance(zdo);
      if (view != null) {
        view.ClaimOwnership();
        view.Destroy();
      } else {
        zdo.SetOwner(ZDOMan.GetSessionID());
        ZDOMan.instance.DestroyZDO(zdo);
      }
      removed.Add(record);
    }
    spawned.Remove(removed);
  }

  static RuntimeReceipt ActionReceipt(
      string status,
      string error,
      Active active,
      string zdo,
      string stage,
      string transition,
      string action) => new() {
    Operation = "action",
    Status = status,
    Error = error,
    PackId = active.PackId,
    Version = active.Version,
    ContentHash = active.ContentHash,
    BindingZdo = zdo,
    StageId = stage,
    CurrentStageId = stage,
    TransitionId = transition,
    ActionId = action,
    Diagnostics = Array.Empty<ContractDiagnostic>(),
  };

  CharmReference Read(ZDO zdo) {
    var reference = new CharmReference {
      PackId = zdo.GetString(Prefix + "packId", ""),
      ExperienceId = zdo.GetString(Prefix + "experienceId", ""),
      BindingId = zdo.GetString(Prefix + "bindingId", ""),
      Version = zdo.GetString(Prefix + "version", ""),
      ContentHash = zdo.GetString(Prefix + "contentHash", ""),
    };
    return CharmPolicy.ValidateReference(reference).Allowed ? reference : null;
  }

  WorkflowIdentity Identity(ZDO zdo, Active active) => new() {
    WorldId = ZNet.instance.GetWorldUID().ToString(),
    CharacterId = Player.m_localPlayer == null
        ? "0" : Player.m_localPlayer.GetPlayerID().ToString(),
    BindingZdo = zdo.m_uid.ToString(),
    ContentHash = active.ContentHash,
  };

  public string DescribeProgress() {
    try {
      if (!TryLoad(out var active, out _)) return "not ready";
      foreach (var wear in WearNTear.GetAllInstances()) {
        var zdo = wear?.GetComponent<ZNetView>()?.GetZDO();
        if (zdo == null) continue;
        var reference = Read(zdo);
        if (reference == null || reference.ContentHash != active.ContentHash) continue;
        var progress = workflows.Get(Identity(zdo, active));
        if (progress == null) {
          var first = active.Document.Stages.FirstOrDefault(
              value => value.Id == active.Document.EntryStage);
          return active.Document.Title + ": not started - "
              + Describe(first?.Transitions?.FirstOrDefault()?.When);
        }
        if (!string.IsNullOrWhiteSpace(progress.Outcome))
          return active.Document.Title + ": " + progress.Outcome;
        var stage = active.Document.Stages.FirstOrDefault(value => value.Id == progress.StageId);
        var transition = stage?.Transitions?.OrderByDescending(value => value.Priority)
            .ThenBy(value => value.Id, StringComparer.Ordinal).FirstOrDefault();
        var measured = TriggerEvaluator.Measure(transition?.When, progress.History);
        var count = measured.Required > 1
            ? " - " + measured.Current + "/" + measured.Required : "";
        return active.Document.Title + ": " + progress.StageId + " - "
            + Describe(transition?.When) + count;
      }
      return active.Document.Title + ": not started";
    } catch {
      return "unavailable";
    }
  }

  static string Describe(TriggerExpression trigger) {
    var leaf = string.Equals(trigger?.Op, "COUNT", StringComparison.OrdinalIgnoreCase)
        ? trigger?.Children?.FirstOrDefault()
        : trigger;
    if (leaf != null
        && CreatorSignalCatalog.TryDescribe(leaf.Event, leaf.Target, out var signal))
      return signal.Instruction;
    return (leaf?.Event ?? "perform the current beat").Replace('_', ' ');
  }

  bool TryLoad(out Active active, out string error) {
    active = null;
    error = "active_set_missing";
    try {
      var activePath = Path.Combine(root, "active", "active-set.json");
      if (!File.Exists(activePath)) { InvalidateActive(); return false; }
      var activeWrite = File.GetLastWriteTimeUtc(activePath);
      if (cachedActive != null && activeWrite == cachedActiveWriteUtc
          && File.Exists(cachedActive.PackagePath)) {
        var currentPackage = new FileInfo(cachedActive.PackagePath);
        if (currentPackage.LastWriteTimeUtc == cachedPackageWriteUtc
            && currentPackage.Length == cachedPackageLength) {
          active = cachedActive;
          error = null;
          return true;
        }
      }
      var set = JsonConvert.DeserializeObject<ActiveSet>(File.ReadAllText(activePath));
      if (set == null || set.Source != Path.GetFileName(set.Source)) {
        InvalidateActive();
        error = "active_source_invalid";
        return false;
      }
      var package = Path.Combine(root, "inbox", set.Source);
      var inspected = new QuestPackStore(root).Inspect(package);
      if (!inspected.IsValid
          || inspected.ContentHash != set.ContentHash
          || inspected.Manifest.PackId != set.PackId
          || inspected.Manifest.Version != set.Version) {
        InvalidateActive();
        error = "active_content_mismatch";
        return false;
      }
      using var zip = ZipFile.OpenRead(package);
      var entries = zip.Entries.Where(value =>
          value.FullName.StartsWith("experiences/", StringComparison.Ordinal)
          && value.FullName.EndsWith(".json", StringComparison.Ordinal)).ToArray();
      if (entries.Length != 1) {
        InvalidateActive();
        error = "active_experience_ambiguous";
        return false;
      }
      using var reader = new StreamReader(entries[0].Open());
      var compiled = ExperienceCompiler.CompileProductionJson(reader.ReadToEnd());
      if (!compiled.IsValid) {
        InvalidateActive();
        error = "active_experience_invalid";
        return false;
      }
      var packageInfo = new FileInfo(package);
      cachedActive = new Active {
        PackId = set.PackId,
        Version = set.Version,
        ContentHash = set.ContentHash,
        ActivationId = set.ActivationId,
        Document = compiled.Document,
        Subscriptions = RuntimeSubscriptionIndex.Create(compiled.Document),
        PackagePath = package,
      };
      cachedActiveWriteUtc = activeWrite;
      cachedPackageWriteUtc = packageInfo.LastWriteTimeUtc;
      cachedPackageLength = packageInfo.Length;
      active = cachedActive;
      error = null;
      return true;
    } catch {
      InvalidateActive();
      error = "active_set_unreadable";
      return false;
    }
  }

  void InvalidateActive() {
    cachedActive = null;
    cachedBindings = Array.Empty<WearNTear>();
    cachedBindingContentHash = null;
    recentEventKeys.Clear();
  }

  WorldAuthority World() {
    try {
      var dedicated = ZNet.instance != null && ZNet.instance.IsDedicated();
      var host = ZNet.instance != null && ZNet.instance.IsServer() && !dedicated;
      return new WorldAuthority {
        IsPrivateWorld = privateConfirmed(),
        IsSolo = host,
        IsListenHost = host,
        IsDedicated = dedicated,
        IsPeerClient = ZNet.instance != null && !ZNet.instance.IsServer(),
      };
    } catch {
      return new WorldAuthority { IsPrivateWorld = privateConfirmed() };
    }
  }

  void Write(string operation, string error, Active active, string status, string detail) =>
      receipts.Write(new RuntimeReceipt {
        Operation = operation,
        Status = status ?? "rejected",
        Error = detail == null ? error : error + ":" + detail,
        PackId = active?.PackId,
        Version = active?.Version,
        ContentHash = active?.ContentHash,
        Diagnostics = Array.Empty<ContractDiagnostic>(),
      });

  sealed class ActiveSet {
    [JsonProperty("pack_id")] public string PackId { get; set; }
    [JsonProperty("version")] public string Version { get; set; }
    [JsonProperty("content_hash")] public string ContentHash { get; set; }
    [JsonProperty("activation_id")] public string ActivationId { get; set; }
    [JsonProperty("source")] public string Source { get; set; }
  }

  sealed class Active {
    public string PackId;
    public string Version;
    public string ContentHash;
    public string ActivationId;
    public ExperienceDocument Document;
    public RuntimeSubscriptionIndex Subscriptions;
    public string PackagePath;
  }
}
