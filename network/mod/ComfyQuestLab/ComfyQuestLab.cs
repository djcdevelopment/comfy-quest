namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using System.Text;

using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;

using HarmonyLib;

using UnityEngine;

/// <summary>A private-world lab for learning what Valheim can trigger a quest on.
///
/// Separate from ComfyNetworkSense on purpose. The shipping mod is telemetry and netcode
/// and runs on the live server; this hooks far more of the game, draws a console over
/// the screen, and is meant to be installed on one person's own world while they learn.
/// Keeping them apart means the lab can be adventurous without ever being a risk to a
/// server, and it can be uninstalled the moment somebody is done learning.
///
/// What it shares with the shipping mod is the quest contract itself — TrackedQuest,
/// QuestEvent, QuestViewLoader, QuestEventCatalog, QuestAuthoring, and QuestTriggerEvaluator are compiled from the same files (see the
/// csproj), so a quest that behaves one way here behaves the same way there. That is
/// the only promise the lab makes, and it is the one that matters.
///
/// Status: all eight atlas categories have a canonical event route. Creator rows show stable
/// event names; exact method signatures remain diagnostic provenance. The generated catalog and
/// runtime profile decide what is bindable rather than a hard-coded kill branch.</summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ComfyQuestLab : BaseUnityPlugin {
  public const string PluginGuid = "djcdevelopment.valheim.comfyquestlab";
  public const string PluginName = "ComfyQuestLab";
  public const string PluginVersion = "0.2.0";

  // Hand-set at a release cut, exactly like ComfyNetworkSense. "dev" means an uncut
  // local build, which is never a release.
  public const string ReleaseId = "questlab-v0.2.0-20260809-r22";

  public static ComfyQuestLab Instance { get; private set; }

  static ManualLogSource _log;
  Harmony _harmony;
  LabEventRing _ring;
  LabPanel _panel;
  LabGalleryBuilder _gallery;
  LabBatchController _batch;
  LabBlueprintBuilder _blueprints;

  public static LabEventRing Ring { get { return Instance == null ? null : Instance._ring; } }
  public static LabBatchController Batch { get { return Instance == null ? null : Instance._batch; } }
  public static bool IsPanelOpen { get { return Instance != null && Instance._panel != null && Instance._panel.IsOpen; } }

  void Awake() {
    Instance = this;
    _log = Logger;

    LabConfig.Bind(Config);
    _ring = new LabEventRing(LabConfig.ConsoleRows.Value * 8);
    _panel = new LabPanel(_ring);
    _gallery = new LabGalleryBuilder();
    _batch = new LabBatchController(_gallery);
    _blueprints = new LabBlueprintBuilder();

    // A dedicated server has no screen and no player to teach. Bail before patching so
    // a server operator who installs this by accident gets a no-op, not a surprise.
    if (IsDedicatedServer()) {
      LogInfo("dedicated server detected — quest lab is client-only, doing nothing.");
      return;
    }

    _harmony = new Harmony(PluginGuid);
    CombatPatches.Apply(_harmony);
    HarvestPatches.Apply(_harmony);
    InventoryPatches.Apply(_harmony);
    BuildingPatches.Apply(_harmony);
    CraftingPatches.Apply(_harmony);
    ProgressionPatches.Apply(_harmony);
    WorldPatches.Apply(_harmony);
    SocialPatches.Apply(_harmony);
    DiagnosticPatches.Apply(_harmony);
    LabPanelInputPatches.Apply(_harmony);

    // Not a seam — it holds the gallery up rather than watching anything. See the file.
    GalleryStructurePatches.Apply(_harmony);
    // Also not a seam: hangs a coloured lamp on each monument. Client-side and cosmetic.
    RuneLightPatches.Apply(_harmony);
    // Read-only observability: snapshot prefab renderer state before world pieces stream in.
    LabRenderInspectorPatches.Apply(_harmony);

    RegisterConsoleCommands();

    // Load quests now rather than at first kill, mirroring ComfyNetworkSense's LoadQuestView():
    // a malformed file should be visible when somebody launches, not twenty minutes later when
    // a quest they were counting on quietly does not fire. The world does not exist yet, so the
    // catalog advisories stay silent until the first lab_reload — by design, see LabWorldFacts.
    LabQuestEngine.Reload();

    LogInfo("quest lab " + PluginVersion + " (" + ReleaseId + ") — "
        + LabPatching.AppliedCount + "/" + LabPatching.Outcomes.Count + " seams hooked, "
        + LabQuestEngine.Set.Quests.Count + " quests loaded ("
        + LabQuestEngine.Set.ArmedCount + " armed). "
        + "Type questlab_help in the console (F5).");
  }

  void Update() {
    if (_panel == null) {
      return;
    }
    if (!LabConfig.Enabled.Value) {
      if (_panel.IsOpen) {
        _panel.Close();
      }
      return;
    }

    // Poll before the keyboard guard: a bounded request is filesystem input, not a key, and
    // must continue while the F5 console or a text field has focus.
    if (_batch != null) {
      _batch.Poll(this);
    }

    if (_panel.IsOpen) {
      InputGuard.MaintainPanelInput();
      // Closing is an ownership release, not an ordinary text-field hotkey. Escape and
      // the activation key therefore work even while the match box has IMGUI focus.
      if (Input.GetKeyDown(KeyCode.Escape) || LabConfig.PanelShortcut.Value.IsDown()) {
        _panel.Close();
      }
      return;
    }

    // Every remaining keystroke is a hotkey unless something says otherwise.
    if (!InputGuard.ShouldIgnoreKeystrokes() && LabConfig.PanelShortcut.Value.IsDown()) {
      _panel.Toggle();
    }
  }

  void OnGUI() {
    if (!LabConfig.Enabled.Value || _panel == null) {
      return;
    }
    _panel.Draw();
  }

  void OnDestroy() {
    if (_panel != null) {
      _panel.Dispose();
    } else {
      InputGuard.ReleasePanelInput();
    }
    if (_harmony != null) {
      _harmony.UnpatchSelf();
      _harmony = null;
    }
    Instance = null;
  }

  // ---- what the patches call -------------------------------------------------------

  /// <summary>Record one thing the game did. Safe to call from any postfix.</summary>
  public static void Observe(LabEvent row) {
    ComfyQuestLab self = Instance;
    if (self == null || self._ring == null || !LabConfig.Enabled.Value) {
      return;
    }
    self._ring.Add(row);
    if (LabConfig.VerboseLogging.Value) {
      LogInfo("[lab] " + row.Category + " " + row.Seam + " " + row.Target + " " + row.Detail);
    }
  }

  /// <summary>Did the local player deal this hit?
  ///
  /// A quest can only ever be about what the player did, and a world full of creatures
  /// hitting each other would bury the console within seconds of a fight starting.</summary>
  public static bool IsLocalPlayerAttacker(HitData hit) {
    try {
      if (hit == null || Player.m_localPlayer == null) {
        return false;
      }
      Character attacker = hit.GetAttacker();
      return attacker != null && attacker == Player.m_localPlayer;
    } catch (Exception) {
      return false;
    }
  }

  public static void LogInfo(string message) {
    if (_log != null) {
      _log.LogInfo(message);
    }
  }

  static bool IsDedicatedServer() {
    try {
      return ZNet.instance != null && ZNet.instance.IsDedicated();
    } catch (Exception) {
      // Called from Awake, before ZNet exists on a client. Absence means "not dedicated".
      return false;
    }
  }

  // ---- console ---------------------------------------------------------------------

  // Terminal.ConsoleCommand self-registers in its constructor, so the instances are
  // deliberately discarded. Wrapped as a whole because a failure here should cost the
  // commands, not the mod.
  void RegisterConsoleCommands() {
    try {
      new Terminal.ConsoleCommand("questlab_help",
          "what the quest lab can do right now: questlab_help",
          delegate { Report(Help()); });

      new Terminal.ConsoleCommand("lab_setup",
          "set up the lab practice area and read the welcome note: lab_setup",
          delegate {
            // Seed before the gallery, and synchronously: the starter file is the thing a
            // creator opens next, so it should exist by the time the console stops talking.
            string seeded = LabQuestSeed.EnsureSeeded(LabQuestEngine.QuestDir);
            if (seeded != null) {
              Report(seeded);
              LabQuestEngine.Reload();
            }

            // A setup is a clean, repeatable course rather than "build if absent". The safe
            // lifecycle returns anyone on the old deck to natural terrain, clears only marked
            // Lab objects, then rebuilds fresh targets and interaction-local supplies at the
            // same site. Re-running the first command can neither stack galleries nor inherit
            // a Greyling/tree somebody consumed last time.
            StartCoroutine(_gallery.ResetSite(this, LabGalleryPlan.DefaultProfileId));
            Report("Quest lab reset started: safely clearing marked builds, then raising a fresh "
                + LabGalleryPlan.DefaultProfileId + " course on this site.");

            Report("Press " + LabConfig.PanelShortcut.Value + " to open the lab console. "
                + "Learn the spells at djcdevelopment.github.io/baseline/questlab/");
          });

      // The third of the three one-word commands, and it exists because the other two left a
      // hole: you edit a quest, reload it, and then need something to kill again. Chasing a mob
      // across the map is exactly the minute the practice gallery was built to give back.
      new Terminal.ConsoleCommand("lab_target",
          "put a fresh practice target in front of you: lab_target [school]",
          delegate (Terminal.ConsoleEventArgs args) {
            Report(_gallery.Restock(args.Length >= 2 ? args[1] : null));
          });

      new Terminal.ConsoleCommand("lab_reload",
          "re-read your quest files and say what changed: lab_reload",
          delegate {
            if (_batch != null && _batch.IsLiveCaptureRunning) {
              Report("A live suite is running. Report/export/reset it before reloading quests; "
                  + "reload would discard its zero-cooldown evaluator and action keys.");
              return;
            }
            string summary = LabQuestEngine.Reload();
            Report(summary);
            // One row in the event console so somebody watching it notices; the detail is
            // in the Quests tab, where it stays put instead of scrolling away.
            Observe(new LabEvent(LabCategory.Combat, "quest.reloaded",
                LabQuestEngine.Set.Quests.Count + " quests",
                LabQuestEngine.Set.ArmedCount + " armed", LabUsability.Today));
          });

      new Terminal.ConsoleCommand("questlab_panel",
          "open or close the lab console: questlab_panel",
          delegate { _panel.Toggle(); Report("quest lab panel " + (_panel.IsOpen ? "open" : "closed")); });

      new Terminal.ConsoleCommand("questlab_seams",
          "which seams this build hooked, and which it could not: questlab_seams",
          delegate { Report(SeamRoster()); });

      new Terminal.ConsoleCommand("questlab_profile",
          "show or set integration noise: questlab_profile [core|extended|diagnostic]",
          delegate (Terminal.ConsoleEventArgs args) {
            if (args.Length >= 2 && IsRuntimeProfile(args[1])) {
              LabConfig.EventProfile.Value = args[1].ToLowerInvariant();
            }
            Report("quest lab event profile: " + LabConfig.EventProfile.Value
                + " (core | extended | diagnostic)");
          });

      // The gallery is the one thing here that changes the world, so it only ever moves
      // when somebody types one of these. check first, always.
      new Terminal.ConsoleCommand("questlab_gallery",
          "manage Gallery v2: questlab_gallery "
          + "<profiles|check|build|compare|identify|evidence|clear|rebuild|trees|restore-trees> "
          + "[profile-or-build-id]",
          delegate (Terminal.ConsoleEventArgs args) {
            string verb = args.Length >= 2 ? args[1].ToLowerInvariant() : "check";
            string value = args.Length >= 3 ? args[2] : null;
            if (verb == "build") {
              StartCoroutine(_gallery.Build(this, value));
            } else if (verb == "compare") {
              string right = args.Length >= 4 ? args[3] : "marble-grand";
              StartCoroutine(_gallery.Compare(this, value ?? "marble-wide", right));
            } else if (verb == "clear") {
              StartCoroutine(_gallery.ClearSafely(value));
            } else if (verb == "rebuild") {
              StartCoroutine(_gallery.Rebuild(this, value));
            } else if (verb == "identify") {
              Report(_gallery.Identify());
            } else if (verb == "evidence") {
              Report(LabTruthLens.Capture(value).Summary);
            } else if (verb == "profiles") {
              Report(LabGalleryBuilder.Profiles());
            } else if (verb == "trees") {
              Report(LabTreeRecovery.Status());
            } else if (verb == "restore-trees") {
              string selector = value ?? "all";
              if (_gallery.StandingPieceCount(selector) > 0) {
                Report("tree restore stopped safely: matching gallery pieces are still "
                    + "standing. Use questlab_gallery clear " + selector + " instead; "
                    + "clear restores the matching ledger after the structure is gone.");
              } else {
                Report(LabTreeRecovery.Restore(selector));
              }
            } else {
              Report(_gallery.Check(value));
            }
          });

      new Terminal.ConsoleCommand("questlab_batch",
          "bounded integration suites: questlab_batch "
          + "<suites|prepare|run|reset|report|export> [all-schools|creator-events]",
          delegate (Terminal.ConsoleEventArgs args) {
            string verb = args.Length >= 2 ? args[1].ToLowerInvariant() : "suites";
            string suite = args.Length >= 3 ? args[2] : "all-schools";
            if (verb == "prepare") {
              StartCoroutine(_batch.Prepare(this, suite));
            } else if (verb == "run") {
              Report(_batch.Run(suite));
            } else if (verb == "reset") {
              Report(_batch.Reset());
            } else if (verb == "report") {
              Report(_batch.Report());
            } else if (verb == "export") {
              Report(_batch.Export());
            } else {
              Report(LabBatchContract.SuiteRoster());
            }
          });

      // The other world-changing lane. Same rule as the gallery: it only ever moves
      // when somebody types one of these, and check comes before build, always.
      new Terminal.ConsoleCommand("questlab_blueprint",
          "build a PlanBuild .blueprint file: questlab_blueprint <list|check|build|clear> "
          + "[name] — build <name> sky raises it overhead with a portal pair bound to "
          + "the blueprint's name, ground door at your crosshair",
          delegate (Terminal.ConsoleEventArgs args) {
            string verb = args.Length >= 2 ? args[1].ToLowerInvariant() : "list";
            string name = args.Length >= 3 ? args[2] : null;
            bool sky = args.Length >= 4
                && string.Equals(args[3], "sky", StringComparison.OrdinalIgnoreCase);
            if (verb == "build") {
              StartCoroutine(_blueprints.Build(this, name, sky));
            } else if (verb == "clear") {
              Report(_blueprints.Clear(name));
            } else if (verb == "count") {
              Report(_blueprints.Count(name));
            } else if (verb == "check") {
              Report(_blueprints.Check(name));
            } else {
              Report(_blueprints.List());
            }
          });

      new Terminal.ConsoleCommand("questlab_prefabs",
          "search what this game build actually has: questlab_prefabs <part of a name>, "
          + "questlab_prefabs inspect <exact-name> reads materials/lights, or dump writes "
          + "the whole catalog to JSON",
          delegate (Terminal.ConsoleEventArgs args) {
            string arg = args.Length >= 2 ? args[1] : null;
            if (string.Equals(arg, "dump", StringComparison.OrdinalIgnoreCase)) {
              Report(LabPrefabDump.Write());
            } else if (string.Equals(arg, "inspect", StringComparison.OrdinalIgnoreCase)) {
              Report(LabRenderInspector.Inspect(args.Length >= 3 ? args[2] : null));
            } else {
              Report(LabGalleryBuilder.SearchPrefabs(arg));
            }
          });

      // Lighting is a judgement call about weather nobody can predict, so it is tuned
      // while looking at the thing rather than by rebuilding the gallery.
      new Terminal.ConsoleCommand("questlab_runelight",
          "tune the monument lamps live: questlab_runelight <intensity> [range], "
          + "or questlab_runelight off | on",
          delegate (Terminal.ConsoleEventArgs args) {
            Report(LabRuneLight.Tune(args.Length >= 2 ? args[1] : null,
                                     args.Length >= 3 ? args[2] : null));
          });

      new Terminal.ConsoleCommand("questlab_clear",
          "empty the event console: questlab_clear",
          delegate { _ring.Clear(); Report("console cleared"); });
    } catch (Exception ex) {
      LogInfo("could not register console commands: " + ex);
    }
  }

  static string Help() {
    var sb = new StringBuilder();
    sb.AppendLine("ComfyQuestLab " + PluginVersion + " — learn what the game can trigger a quest on.");
    sb.AppendLine("  lab_setup        set up the practice area (do this first)");
    sb.AppendLine("  lab_reload       re-read your quest files and say what changed");
    sb.AppendLine("  lab_target [school]   put a fresh practice target in front of you");
    sb.AppendLine("  questlab_panel   open the live event console (" + LabConfig.PanelShortcut.Value + ")");
    sb.AppendLine("  questlab_seams   which seams are hooked on this game build");
    sb.AppendLine("  questlab_profile [core|extended|diagnostic]   choose integration noise");
    sb.AppendLine("  questlab_clear   empty the live view");
    sb.AppendLine("  questlab_gallery profiles   list Gallery v2 geometry choices");
    sb.AppendLine("  questlab_gallery check|build|rebuild [profile]   inspect or raise one profile");
    sb.AppendLine("  questlab_gallery compare [left] [right]   raise two profiles side by side");
    sb.AppendLine("  questlab_gallery identify|clear [profile-or-build-id]   inspect or remove marks safely");
    sb.AppendLine("  questlab_gallery evidence [profile-or-build-id]   export read-only truth and named views");
    sb.AppendLine("  questlab_gallery trees|restore-trees [profile-or-build-id]   inspect or recover pruned trees");
    sb.AppendLine("  questlab_batch suites|prepare|run|reset|report|export [suite]   bounded evidence runs");
    sb.AppendLine("  questlab_blueprint list | check <n> | build <n> [sky] | clear [n]   build a .blueprint file");
    sb.AppendLine("  questlab_prefabs <name> | inspect <exact-name> | dump   search or inspect rendered state");
    sb.AppendLine("Try one action at a monument with the panel open; every school has bindable events.");
    return sb.ToString().TrimEnd();
  }

  static string SeamRoster() {
    var sb = new StringBuilder();
    IReadOnlyList<LabPatching.Outcome> outcomes = LabPatching.Outcomes;
    sb.AppendLine("seams hooked " + LabPatching.AppliedCount + "/" + outcomes.Count + ":");
    foreach (LabPatching.Outcome o in outcomes) {
      sb.AppendLine("  " + (o.Applied ? "[x] " : "[ ] ") + o.Label + (o.Applied ? string.Empty : " — " + o.Detail));
    }
    sb.AppendLine("Creator rows use stable canonical events; exact method signatures are diagnostic. "
        + "Active profile: " + LabConfig.EventProfile.Value + ".");
    return sb.ToString().TrimEnd();
  }

  static bool IsRuntimeProfile(string value) {
    return string.Equals(value, LabRuntimeProfile.Core, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, LabRuntimeProfile.Extended, StringComparison.OrdinalIgnoreCase)
        || string.Equals(value, LabRuntimeProfile.Diagnostic, StringComparison.OrdinalIgnoreCase);
  }

  /// <summary>Say something to the player and the log at once. The shipping mod uses
  /// MessageHud the same way; a console command that only writes to a log file is a
  /// command a builder will assume did nothing.</summary>
  public static void Report(string message) {
    LogInfo(message);
    try {
      if (MessageHud.instance != null) {
        MessageHud.instance.ShowMessage(MessageHud.MessageType.TopLeft, message);
      }
    } catch (Exception) {
    }
  }
}

/// <summary>The [Lab] config section.
///
/// Same conventions as ComfyNetworkSense's PluginConfig: PascalCase section, camelCase
/// keys, full-sentence descriptions, no SettingChanged wiring. Every consumer reads
/// .Value live, so Config.Reload() takes effect on the next frame for free.</summary>
public static class LabConfig {
  public static ConfigEntry<bool> Enabled { get; private set; }
  public static ConfigEntry<KeyboardShortcut> PanelShortcut { get; private set; }
  public static ConfigEntry<float> PanelScale { get; private set; }
  public static ConfigEntry<float> PanelX { get; private set; }
  public static ConfigEntry<float> PanelY { get; private set; }
  public static ConfigEntry<float> PanelWidth { get; private set; }
  public static ConfigEntry<float> PanelHeight { get; private set; }
  public static ConfigEntry<int> ConsoleRows { get; private set; }
  public static ConfigEntry<bool> VerboseLogging { get; private set; }
  public static ConfigEntry<string> EventProfile { get; private set; }
  public static ConfigEntry<bool> ObserveStamina { get; private set; }
  public static ConfigEntry<int> GalleryPiecesPerFrame { get; private set; }
  public static ConfigEntry<int> BlueprintPiecesPerFrame { get; private set; }
  public static ConfigEntry<bool> QuestsEnabled { get; private set; }
  public static ConfigEntry<float> QuestCooldownSeconds { get; private set; }

  public static void Bind(ConfigFile config) {
    Enabled =
        config.Bind(
            "Lab",
            "enabled",
            true,
            "Master switch for the quest lab. OFF = no console, no hotkeys, and every "
            + "observation is dropped; the Harmony patches remain applied but do nothing. "
            + "Hot-reloadable.");

    PanelShortcut =
        config.Bind(
            "Lab",
            "panelShortcut",
            new KeyboardShortcut(KeyCode.F6),
            "Opens and closes the live event console. F6 by default because "
            + "ComfyControlSurface used F7 and the camera kit warns against reusing it. "
            + "Set to None to use questlab_panel from the console instead.");

    PanelScale =
        config.Bind(
            "Lab",
            "panelScale",
            1f,
            new ConfigDescription(
                "Quest Lab panel zoom. Use the - and + controls inside the panel to tune "
                + "windowed, 1080p, and 4K layouts; the choice is saved here. Hot-reloadable.",
                new AcceptableValueRange<float>(0.65f, 2f)));

    PanelX = config.Bind(
        "Lab", "panelX", 80f,
        "Saved horizontal position of the Quest Lab window in unscaled screen coordinates. "
        + "Updated when the panel closes; out-of-screen values are clamped safely.");
    PanelY = config.Bind(
        "Lab", "panelY", 90f,
        "Saved vertical position of the Quest Lab window in unscaled screen coordinates. "
        + "Updated when the panel closes; out-of-screen values are clamped safely.");
    PanelWidth = config.Bind(
        "Lab", "panelWidth", 900f,
        new ConfigDescription(
            "Saved Quest Lab window width. Drag the lower-right handle; the value is saved "
            + "when the panel closes.",
            new AcceptableValueRange<float>(700f, 2400f)));
    PanelHeight = config.Bind(
        "Lab", "panelHeight", 620f,
        new ConfigDescription(
            "Saved Quest Lab window height. Drag the lower-right handle; the value is saved "
            + "when the panel closes.",
            new AcceptableValueRange<float>(440f, 1800f)));

    ConsoleRows =
        config.Bind(
            "Lab",
            "consoleRows",
            18,
            "How many event rows the console shows at once. The in-memory ring keeps "
            + "eight times this many so you can scroll back over a fight you just had.");

    VerboseLogging =
        config.Bind(
            "Lab",
            "verboseLogging",
            false,
            "Default OFF. ON = every observed event is also written to the BepInEx log. "
            + "Useful when you want a transcript to paste into a thread; noisy in combat.");

    EventProfile =
        config.Bind(
            "Lab",
            "eventProfile",
            LabRuntimeProfile.Extended,
            new ConfigDescription(
                "Runtime integration profile. core = intentional low-noise actions; extended "
                + "also enables stable high-frequency actions such as damage; diagnostic also "
                + "shows corroborating and low-level mutation seams but never makes them quest "
                + "triggers. Hot-reloadable. Stamina keeps its separate observeStamina gate.",
                new AcceptableValueList<string>(
                    LabRuntimeProfile.Core,
                    LabRuntimeProfile.Extended,
                    LabRuntimeProfile.Diagnostic)));

    ObserveStamina =
        config.Bind(
            "Lab",
            "observeStamina",
            false,
            "Default OFF, and read once at startup rather than live. Player.UseStamina "
            + "fires on nearly every action including running, so it drowns the console. "
            + "Turn it on to see the shape of a stamina event, then turn it off again. "
            + "Changing this needs a game restart, because it decides whether the patch "
            + "is applied at all.");

    GalleryPiecesPerFrame =
        config.Bind(
            "Lab",
            "galleryPiecesPerFrame",
            24,
            "How many Gallery v2 objects are placed per frame. 24 keeps the default "
            + "1,350-piece compact marble build responsive while finishing in a few seconds. "
            + "Lower it on slower clients or raise it for faster local comparisons. "
            + "Hot-reloadable; clamped to 1-200.");

    BlueprintPiecesPerFrame =
        config.Bind(
            "Lab",
            "blueprintPiecesPerFrame",
            12,
            "How many blueprint pieces are placed per frame during questlab_blueprint "
            + "build. 12 is the conservative rate the original gallery proved out. Raise it "
            + "to build faster at the cost of frame hitches; a 2,000-piece blueprint "
            + "at 12/frame takes a few seconds. Hot-reloadable; clamped to 1-200.");

    QuestsEnabled =
        config.Bind(
            "Quests",
            "questsEnabled",
            true,
            "Whether the lab evaluates your quest files against safe creator events. OFF = files "
            + "are still loaded and still shown in the Quests tab, but nothing fires. Turn it "
            + "off to prove that a quest firing is what you think it is.");

    QuestCooldownSeconds =
        config.Bind(
            "Quests",
            "questCooldownSeconds",
            60f,
            new ConfigDescription(
                "How long one quest waits before it can fire again. 60 matches the shipping "
                + "mod's constructor default, so what you see here is what a player would see. "
                + "Drop it to 0 while authoring — retesting an edit should not cost a minute.",
                new AcceptableValueRange<float>(0f, 3600f)));

    // Retune the live evaluator rather than only the next reload, for the same reason the
    // rune lamps do it: a knob about timing is one you turn while watching the thing.
    QuestCooldownSeconds.SettingChanged += (s, e) => LabQuestEngine.RetuneCooldown();
  }
}
