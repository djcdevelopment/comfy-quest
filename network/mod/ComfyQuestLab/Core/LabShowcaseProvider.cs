namespace ComfyQuestLab;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Fixed preparation for the reviewed Field Lodge development seat. No coordinates,
/// prefab names, commands, damage or quest events are accepted from the mailbox.</summary>
public sealed class LabShowcaseProvider {
  public const string PrepareOperation = "showcase_prepare";
  public const string StatusOperation = "showcase_status";
  public const string ReleaseOperation = "showcase_release";
  public const string TidyOperation = "showcase_tidy";
  public const string Spear = "SpearCarapace";
  static readonly string[] PracticeArmor = { "ArmorCarapaceChest", "ArmorCarapaceLegs", "HelmetCarapace" };
  static readonly string[] PracticeFood = { "DeerStew", "CarrotSoup", "Sausages" };
  public static readonly Vector3 Arrival = new Vector3(415f, 77f, 232f);
  public static readonly Quaternion Facing = Quaternion.identity;
  public const string LodgeBlueprint = "tn0304-6ue2ukrad7ntjfoa7cvdmvwkcvsccusli5wrfvhq2rnkhtm2ehuq";
  public static bool ProtectedPracticeActive { get; private set; }

  string _session;
  string _practiceProfile = "signature-throw";
  string _practiceWeapon = Spear;
  Player _player;
  EnvMan _environment;
  RandEventSystem _events;
  bool _oldGod, _oldGhost, _oldTimeEnabled;
  float _oldTime, _oldEventChance;
  string _oldWeather;
  readonly Dictionary<RandomEvent, bool> _oldEvents = new Dictionary<RandomEvent, bool>();
  float _nextStatus;
  public bool IsRunning { get; private set; }
  public bool LastSucceeded { get; private set; }
  public string LastResult { get; private set; } = "Field Lodge preparation has not run.";
  public string StatusPath => Path.Combine(BepInEx.Paths.ConfigPath,
      "comfy-quest-lab", "status", "showcase.json");
  public string LastTidyPath { get; private set; }

  public static bool IsOperation(string operation) =>
      string.Equals(operation, PrepareOperation, StringComparison.OrdinalIgnoreCase)
      || string.Equals(operation, StatusOperation, StringComparison.OrdinalIgnoreCase)
      || string.Equals(operation, ReleaseOperation, StringComparison.OrdinalIgnoreCase)
      || string.Equals(operation, TidyOperation, StringComparison.OrdinalIgnoreCase);

  public static bool Validate(LabBatchRequest r, out string error) {
    error = null;
    if (string.IsNullOrWhiteSpace(r.creator_session_id)
        || string.IsNullOrWhiteSpace(r.expected_machine)
        || string.IsNullOrWhiteSpace(r.expected_world_uid)) error = "showcase_creator_identity_required";
    else if (!string.IsNullOrWhiteSpace(r.suite) || !string.IsNullOrWhiteSpace(r.profile)
        || !string.IsNullOrWhiteSpace(r.compare_profile) || !string.IsNullOrWhiteSpace(r.selector)
        || !string.IsNullOrWhiteSpace(r.corpus) || r.step != 0 || r.seed != 0
        || r.expected_previous_step != 0) error = "request_argument_not_allowed";
    return error == null;
  }

  public static string Precondition(string session) {
    if (Player.m_localPlayer == null || ZNet.instance == null || ZNetScene.instance == null
        || ZoneSystem.instance == null || EnvMan.instance == null || ObjectDB.instance == null
        || RandEventSystem.instance == null) return "showcase_world_not_loaded";
    if (!ZNet.instance.IsServer() || !ZNet.IsSinglePlayer) return "showcase_private_local_host_required";
    if (ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture)
        != LabSignatureHuntContract.ExpectedWorldUid) return "showcase_reviewed_world_required";
    try {
      var entry = JObject.Parse(File.ReadAllText(Path.Combine(BepInEx.Paths.ConfigPath,
          "comfy-quest-runtime", "status", "world-entry.json")));
      if ((string)entry["state"] != "entered" || (string)entry["creator_session_id"] != session
          || (string)entry["world_uid"] != LabSignatureHuntContract.ExpectedWorldUid)
        return "showcase_active_creator_session_mismatch";
    } catch { return "showcase_active_creator_session_unreadable"; }
    return null;
  }

  public IEnumerator Prepare(string session) {
    LastSucceeded = false;
    string error = Precondition(session);
    if (error != null) { LastResult = error; yield break; }
    if (_session != null && _session != session) {
      LastResult = "showcase_owned_by_another_session"; yield break;
    }
    if (IsRunning) { LastResult = "showcase_busy"; yield break; }
    Player player = Player.m_localPlayer;
    try {
      var profilePath = Path.Combine(BepInEx.Paths.ConfigPath, "comfy-quest-lab", "practice-profile.json");
      _practiceProfile = "signature-throw";
      if (File.Exists(profilePath)) {
        if (new FileInfo(profilePath).Length > 4096) throw new InvalidDataException();
        var profile = JObject.Parse(File.ReadAllText(profilePath));
        if ((string)profile["schema"] != "comfy-questlab-practice-profile/v1"
            || profile.Properties().Any(p => p.Name != "schema" && p.Name != "profile")) throw new InvalidDataException();
        _practiceProfile = (string)profile["profile"];
      }
      if (_practiceProfile != "signature-throw" && _practiceProfile != "integration-melee") throw new InvalidDataException();
      _practiceWeapon = _practiceProfile == "integration-melee" ? "SwordCheat" : Spear;
    } catch { LastResult = "showcase_practice_profile_invalid"; yield break; }
    GameObject prefab = ObjectDB.instance.GetItemPrefab(_practiceWeapon);
    if (prefab == null || prefab.GetComponent<ItemDrop>() == null
        || PracticeArmor.Concat(PracticeFood).Any(name => ObjectDB.instance.GetItemPrefab(name)?.GetComponent<ItemDrop>() == null)
        || !EnvMan.instance.m_environments.Any(e => e.m_name == "Clear")) {
      LastResult = "showcase_reviewed_prefab_or_weather_missing"; yield break;
    }
    var inventory = player.GetInventory();
    if (inventory.GetItem(_practiceWeapon, isPrefabName: true) == null && !inventory.HaveEmptySlot()) {
      LastResult = "showcase_inventory_full_free_one_slot"; yield break;
    }
    // A new prepare request begins a new practice start. The mailbox owns duplicate
    // request suppression; status polling never moves or replenishes the player.
    if (_session == session) {
      Release();
    }
    IsRunning = true;
    LastResult = "Preparing the Field Lodge practice loadout.";
    try {
      _session = session;
      ProtectedPracticeActive = true;
      _player = player; _environment = EnvMan.instance; _events = RandEventSystem.instance;
      _oldGod = player.InGodMode(); _oldGhost = player.InGhostMode();
      _oldTimeEnabled = _environment.m_debugTimeOfDay; _oldTime = _environment.m_debugTime;
      _oldWeather = _environment.m_debugEnv; _oldEventChance = _events.m_eventChance;
      foreach (var e in _events.m_events) _oldEvents[e] = e.m_enabled;
      ApplyConditions();
      float deadline = Time.realtimeSinceStartup + 25f;
      while (player.IsTeleporting() && Time.realtimeSinceStartup < deadline) yield return null;
      bool moving = false;
      while (!moving && Time.realtimeSinceStartup < deadline) {
        moving = player.TeleportTo(Arrival, Facing, true);
        if (!moving) yield return null;
      }
      if (!moving) { LastResult = "showcase_arrival_teleport_refused"; yield break; }
      while (player.IsTeleporting() && Time.realtimeSinceStartup < deadline) yield return null;
      if (player.IsTeleporting() || Vector3.Distance(player.transform.position, Arrival) > 10f) {
        LastResult = "showcase_arrival_not_reached"; yield break;
      }
      // Let terrain, the actual camera and the environment transition settle before evidence.
      yield return new WaitForSeconds(3f);
      foreach (var name in PracticeArmor) {
        var item = inventory.GetItem(name, isPrefabName: true)
            ?? inventory.AddItem(name, 1, 4, 0, 0, "Field Lodge");
        if (item != null) item.m_durability = item.GetMaxDurability();
        if (item == null || (!player.IsItemEquiped(item) && !player.EquipItem(item, false))) {
          LastResult = "showcase_practice_armor_equip_failed"; yield break;
        }
      }
      // Native food and armor make practice playable: god mode still permits stagger.
      // The isolated character backup owns restoration of this fixed starting loadout.
      player.ClearFood();
      foreach (var name in PracticeFood) {
        var foodPrefab = ObjectDB.instance.GetItemPrefab(name);
        var food = foodPrefab.GetComponent<ItemDrop>().m_itemData.Clone();
        food.m_dropPrefab = foodPrefab;
        if (!player.EatFood(food)) {
          LastResult = "showcase_practice_food_failed"; yield break;
        }
      }
      player.Heal(player.GetMaxHealth());
      var spear = inventory.GetItem(_practiceWeapon, isPrefabName: true)
          ?? inventory.AddItem(_practiceWeapon, 1, 4, 0, 0, "Field Lodge");
      if (spear != null) spear.m_durability = spear.GetMaxDurability();
      if (spear == null || (player.GetCurrentWeapon() != spear && !player.EquipItem(spear, false))) {
          LastResult = "showcase_practice_weapon_equip_failed"; yield break;
      }
      yield return new WaitForSeconds(1f);
      LastSucceeded = ConditionsReady() && player.GetCurrentWeapon() == spear && OpenArrival();
      LastResult = LastSucceeded
          ? "Field Lodge arrival ready: daylight, armor, food, protection and equipped " + (_practiceProfile == "integration-melee" ? "godsword." : "Carapace spear.")
          : "showcase_arrival_checks_failed";
      if (LastSucceeded) player.Message(MessageHud.MessageType.Center,
          "FIELD LODGE\n" + (_practiceProfile == "integration-melee" ? "Godsword" : "Spear") + " equipped · Protected practice\nRead the briefing ahead, then start your hunt.");
    } finally {
      IsRunning = false;
      // A failed attempt can be retried, and leaves no indefinite environment override.
      if (!LastSucceeded) Release();
      WriteStatus();
    }
  }

  public IEnumerator Tidy(string session) {
    LastSucceeded = false;
    string error = Precondition(session);
    if (error != null || _session != session || !ProtectedPracticeActive) {
      LastResult = error ?? "showcase_prepare_before_tidy"; yield break;
    }
    long owner = Game.instance.GetPlayerProfile().GetPlayerID();
    var graves = UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None)
        .Where(grave => Vector3.Distance(grave.transform.position, Arrival) < 25f
            && grave.GetComponent<ZNetView>()?.GetZDO()?.GetLong(ZDOVars.s_owner, 0L) == owner).ToArray();
    var drops = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)
        .Where(item => Vector3.Distance(item.transform.position, Arrival) < 25f
            && item.m_itemData?.m_dropPrefab?.name == Spear
            && item.m_itemData.m_crafterName == "Field Lodge").ToArray();
    var views = graves.Select(grave => grave.GetComponent<ZNetView>())
        .Concat(drops.Select(item => item.GetComponent<ZNetView>())).ToArray();
    if (views.Length > 32 || views.Any(view => view == null || !view.IsValid())) {
      LastResult = "showcase_tidy_scope_unavailable"; yield break;
    }
    var rows = new List<object>();
    try {
      foreach (var grave in graves) {
        var package = new ZPackage();
        grave.GetComponent<Container>().GetInventory().Save(package);
        rows.Add(new { kind = "owned_grave", zdo = grave.GetComponent<ZNetView>().GetZDO().m_uid.ToString(),
            owner, position = new { x = grave.transform.position.x, y = grave.transform.position.y, z = grave.transform.position.z },
            inventory_base64 = package.GetBase64() });
      }
      foreach (var drop in drops) {
        var inventory = new Inventory("Practice archive", null, 1, 1);
        inventory.AddItem(drop.m_itemData.Clone());
        var package = new ZPackage(); inventory.Save(package);
        rows.Add(new { kind = "practice_spear", zdo = drop.GetComponent<ZNetView>().GetZDO().m_uid.ToString(),
            owner, position = new { x = drop.transform.position.x, y = drop.transform.position.y, z = drop.transform.position.z },
            inventory_base64 = package.GetBase64() });
      }
      LastTidyPath = Path.Combine(BepInEx.Paths.ConfigPath, "comfy-quest-lab", "receipts", "tidy",
          DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ") + ".json");
      Directory.CreateDirectory(Path.GetDirectoryName(LastTidyPath));
      File.WriteAllText(LastTidyPath, JsonConvert.SerializeObject(new {
          schema = "comfy-questlab-practice-tidy/v1", creator_session_id = session,
          world_uid = LabSignatureHuntContract.ExpectedWorldUid, player_id = owner,
          boundary = "Own graves and Field Lodge spears within 25 metres of practice arrival; inventory archived before removal.",
          objects = rows }, Formatting.Indented));
    } catch (Exception e) {
      LastResult = "showcase_tidy_archive_failed: " + e.Message; yield break;
    }
    IsRunning = true;
    try {
      foreach (var view in views) { view.ClaimOwnership(); view.Destroy(); }
      yield return null; yield return null;
      LastSucceeded = views.All(view => view == null || !view.IsValid());
      LastResult = LastSucceeded ? "Practice arrival cleared: " + graves.Length + " own graves and "
          + drops.Length + " practice spears archived and removed." : "showcase_tidy_removal_incomplete";
    } finally { IsRunning = false; WriteStatus(); }
  }

  void ApplyConditions() {
    if (_player == null || _environment == null || _events == null) return;
    _player.SetGodMode(true);
    _player.Heal(_player.GetMaxHealth(), false);
    foreach (var item in _player.GetInventory().GetAllItems())
      if (item.m_dropPrefab != null && (item.m_dropPrefab.name == Spear || item.m_dropPrefab.name == "SwordCheat"
          || PracticeArmor.Contains(item.m_dropPrefab.name)))
        item.m_durability = item.GetMaxDurability();
    _player.SetGhostMode(false); // Targets must react normally to actual play.
    _environment.m_debugTimeOfDay = true; _environment.m_debugTime = .5f;
    _environment.m_debugEnv = "Clear";
    _events.m_eventChance = -1f;
    foreach (var e in _events.m_events) e.m_enabled = false;
    if (_events.GetCurrentRandomEvent() != null) _events.ResetRandomEvent();
  }

  bool ConditionsReady() => _session != null && _player != null && _player.InGodMode()
      && !_player.InGhostMode() && _environment != null
      && Mathf.Abs(_environment.GetDayFraction() - .5f) < .02f
      && _environment.GetCurrentEnvironment()?.m_name == "Clear"
      && _events != null && _events.m_eventChance < 0f
      && _events.GetCurrentRandomEvent() == null && _events.m_events.All(e => !e.m_enabled);

  bool OpenArrival() {
    if (_player == null) return false;
    var p = _player.transform.position;
    // Refuse a roof/cage and a solid obstacle immediately in front. Never clear world pieces.
    int mask = LayerMask.GetMask("piece", "static_solid", "Default", "terrain");
    return !Physics.Raycast(p + Vector3.up * .4f, Vector3.up, 4f, mask)
        && !Physics.Raycast(p + Vector3.up, Facing * Vector3.forward, 2.5f, mask);
  }

  public void Tick() {
    if (_session == null) return;
    if (_player == null || Player.m_localPlayer != _player || ZNet.instance == null
        || ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture)
            != LabSignatureHuntContract.ExpectedWorldUid) { Release(); return; }
    if (Time.realtimeSinceStartup >= _nextStatus) {
      _nextStatus = Time.realtimeSinceStartup + .1f;
      ApplyConditions();
      WriteStatus();
    }
  }

  public void Release() {
    ProtectedPracticeActive = false;
    if (_player != null) { _player.SetGodMode(_oldGod); _player.SetGhostMode(_oldGhost); }
    if (_environment != null) {
      _environment.m_debugTimeOfDay = _oldTimeEnabled;
      _environment.m_debugTime = _oldTime; _environment.m_debugEnv = _oldWeather;
    }
    if (_events != null) {
      _events.m_eventChance = _oldEventChance;
      foreach (var e in _oldEvents) e.Key.m_enabled = e.Value;
    }
    _oldEvents.Clear(); _player = null; _environment = null; _events = null; _session = null;
  }

  public string Status() { WriteStatus(); return _session == null
      ? "Field Lodge test conditions are not active." : LastResult; }

  void WriteStatus() {
    var player = Player.m_localPlayer;
    var pos = player == null ? Vector3.zero : player.transform.position;
    var weapon = player?.GetCurrentWeapon();
    var camera = Camera.main;
    var actors = player == null ? new object[0] : Character.GetAllCharacters()
        .Where(c => c != null && c != player && Vector3.Distance(c.transform.position, pos) < 100f)
        .Select(c => {
          var point = c.GetCenterPoint();
          var screen = camera == null ? Vector3.zero : camera.WorldToScreenPoint(point);
          var zdo = c.GetComponent<ZNetView>()?.GetZDO();
          return (object)new { name = c.m_name, health = c.GetHealth(),
              spawn_action_key = zdo?.GetString("comfyQuestRuntime.spawnedActionKey", ""),
              spawn_content_hash = zdo?.GetString("comfyQuestRuntime.spawnedContentHash", ""),
              position = new { x = point.x, y = point.y, z = point.z },
              screen = new { x = screen.x, y = Screen.height - screen.y, depth = screen.z } };
        }).ToArray();
    var status = new { schema = "comfy-questlab-showcase-readiness/v1", revision = 1,
        observed_utc = DateTimeOffset.UtcNow.ToString("o"), machine = Environment.MachineName,
        creator_session_id = _session, world_uid = ZNet.instance?.GetWorldUID().ToString(CultureInfo.InvariantCulture),
        state = IsRunning ? "preparing" : ConditionsReady() ? "conditions_ready" : "not_ready",
        proof_level = "live-starting-conditions",
        disclaimer = "Observed test conditions only; campaign completion and the full Derek checklist require separate evidence.",
        detail = LastResult, protected_player = player != null && player.InGodMode(),
        practice_profile = _practiceProfile, expected_weapon = _practiceWeapon,
        arrival_graves = UnityEngine.Object.FindObjectsByType<TombStone>(FindObjectsSortMode.None)
            .Count(grave => Vector3.Distance(grave.transform.position, Arrival) < 25f),
        lodge_pieces = UnityEngine.Object.FindObjectsByType<WearNTear>(FindObjectsSortMode.None)
            .Count(piece => LabMarks.BlueprintName(piece.GetComponent<ZNetView>()?.GetZDO()) == LodgeBlueprint
                && Vector3.Distance(piece.transform.position, Arrival) < 80f),
        fixture_damage_blocked = GalleryStructurePatches.PracticeDamageBlocked,
        daylight = _environment != null && Mathf.Abs(_environment.GetDayFraction() - .5f) < .02f,
        weather = _environment?.GetCurrentEnvironment()?.m_name,
        raids_suppressed = _events != null && _events.m_eventChance < 0f && _events.m_events.All(e => !e.m_enabled),
        equipped_prefab = weapon?.m_dropPrefab?.name, open_arrival = OpenArrival(),
        inventory_visible = InventoryGui.IsVisible(), hover_object = player?.GetHoverObject()?.name,
        health = player?.GetHealth(), stamina = player?.GetStamina(), armor = player?.GetBodyArmor(),
        staggering = player != null && player.IsStaggering(), attacking = player != null && player.InAttack(),
        foods = player?.GetFoods().Select(f => f.m_name).ToArray(),
        ground_spears = UnityEngine.Object.FindObjectsByType<ItemDrop>(FindObjectsSortMode.None)
            .Where(item => item.m_itemData?.m_dropPrefab?.name == Spear && Vector3.Distance(item.transform.position, pos) < 80f)
            .Select(item => new { x = item.transform.position.x, y = item.transform.position.y, z = item.transform.position.z }).ToArray(),
        player = new { x = pos.x, y = pos.y, z = pos.z },
        screen = new { width = Screen.width, height = Screen.height },
        camera = camera == null ? null : new { yaw = camera.transform.eulerAngles.y,
            pitch = camera.transform.eulerAngles.x, fov = camera.fieldOfView,
            position = new { x = camera.transform.position.x, y = camera.transform.position.y, z = camera.transform.position.z } },
        signs = UnityEngine.Object.FindObjectsByType<Sign>(FindObjectsSortMode.None).Where(s => Vector3.Distance(s.transform.position, pos) < 60f)
            .Select(s => new { text = s.GetText(), x = s.transform.position.x,
                y = s.transform.position.y, z = s.transform.position.z }).ToArray(), actors };
    try {
      Directory.CreateDirectory(Path.GetDirectoryName(StatusPath));
      string temporary = StatusPath + ".tmp";
      File.WriteAllText(temporary, JsonConvert.SerializeObject(status, Formatting.Indented));
      if (File.Exists(StatusPath)) File.Replace(temporary, StatusPath, null);
      else File.Move(temporary, StatusPath);
    } catch (Exception e) { ComfyQuestLab.LogInfo("[showcase] status write failed: " + e.Message); }
  }
}
