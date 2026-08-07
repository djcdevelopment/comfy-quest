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
/// QuestViewLoader, QuestTriggerEvaluator are compiled from the same files (see the
/// csproj), so a quest that behaves one way here behaves the same way there. That is
/// the only promise the lab makes, and it is the one that matters.
///
/// Scaffold status: the harvest category is wired end to end as the worked example.
/// The other seven categories in the atlas are not hooked yet.</summary>
[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class ComfyQuestLab : BaseUnityPlugin {
  public const string PluginGuid = "djcdevelopment.valheim.comfyquestlab";
  public const string PluginName = "ComfyQuestLab";
  public const string PluginVersion = "0.1.0";

  // Hand-set at a release cut, exactly like ComfyNetworkSense. "dev" means an uncut
  // local build, which is never a release.
  public const string ReleaseId = "dev";

  public static ComfyQuestLab Instance { get; private set; }

  static ManualLogSource _log;
  Harmony _harmony;
  LabEventRing _ring;
  LabPanel _panel;

  public static LabEventRing Ring { get { return Instance == null ? null : Instance._ring; } }
  public static bool IsPanelOpen { get { return Instance != null && Instance._panel != null && Instance._panel.IsOpen; } }

  void Awake() {
    Instance = this;
    _log = Logger;

    LabConfig.Bind(Config);
    _ring = new LabEventRing(LabConfig.ConsoleRows.Value * 8);
    _panel = new LabPanel(_ring);

    // A dedicated server has no screen and no player to teach. Bail before patching so
    // a server operator who installs this by accident gets a no-op, not a surprise.
    if (IsDedicatedServer()) {
      LogInfo("dedicated server detected — quest lab is client-only, doing nothing.");
      return;
    }

    _harmony = new Harmony(PluginGuid);
    HarvestPatches.Apply(_harmony);
    LabPanelInputPatches.Apply(_harmony);

    RegisterConsoleCommands();

    LogInfo("quest lab " + PluginVersion + " (" + ReleaseId + ") — "
        + LabPatching.AppliedCount + "/" + LabPatching.Outcomes.Count + " seams hooked. "
        + "Type questlab_help in the console (F5).");
  }

  void Update() {
    if (!LabConfig.Enabled.Value || _panel == null) {
      return;
    }

    // Every keystroke is a hotkey unless something says otherwise, and the console has a
    // text field. Without this, typing "bush" into the filter walks the player forward,
    // swings whatever is equipped, and closes the panel on the "s".
    if (InputGuard.ShouldIgnoreKeystrokes()) {
      return;
    }

    if (LabConfig.PanelShortcut.Value.IsDown()) {
      _panel.Toggle();
    }

    if (_panel.IsOpen && Input.GetKeyDown(KeyCode.Escape)) {
      _panel.Close();
    }
  }

  void OnGUI() {
    if (!LabConfig.Enabled.Value || _panel == null) {
      return;
    }
    _panel.Draw();
  }

  void OnDestroy() {
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

      new Terminal.ConsoleCommand("questlab_panel",
          "open or close the lab console: questlab_panel",
          delegate { _panel.Toggle(); Report("quest lab panel " + (_panel.IsOpen ? "open" : "closed")); });

      new Terminal.ConsoleCommand("questlab_seams",
          "which seams this build hooked, and which it could not: questlab_seams",
          delegate { Report(SeamRoster()); });

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
    sb.AppendLine("  questlab_panel   open the live event console (" + LabConfig.PanelShortcut.Value + ")");
    sb.AppendLine("  questlab_seams   which seams are hooked on this game build");
    sb.AppendLine("  questlab_clear   empty the console");
    sb.AppendLine("Hit a tree or a bush with the panel open — harvest is the wired category.");
    return sb.ToString().TrimEnd();
  }

  static string SeamRoster() {
    var sb = new StringBuilder();
    IReadOnlyList<LabPatching.Outcome> outcomes = LabPatching.Outcomes;
    sb.AppendLine("seams hooked " + LabPatching.AppliedCount + "/" + outcomes.Count + ":");
    foreach (LabPatching.Outcome o in outcomes) {
      sb.AppendLine("  " + (o.Applied ? "[x] " : "[ ] ") + o.Label + (o.Applied ? string.Empty : " — " + o.Detail));
    }
    sb.AppendLine("Seven of the eight atlas categories are not wired yet; harvest is the worked example.");
    return sb.ToString().TrimEnd();
  }

  /// <summary>Say something to the player and the log at once. The shipping mod uses
  /// MessageHud the same way; a console command that only writes to a log file is a
  /// command a builder will assume did nothing.</summary>
  static void Report(string message) {
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
  public static ConfigEntry<int> ConsoleRows { get; private set; }
  public static ConfigEntry<bool> VerboseLogging { get; private set; }

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
  }
}
