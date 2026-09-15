namespace ComfyQuestRuntime;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using ComfyQuestContracts;
using HarmonyLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>
/// Consumes one bounded Creator Session request and enters one exact local world through
/// Valheim's normal profile/world APIs. This is deliberately not a general launcher or
/// client orchestrator: no server address, console command, input, or arbitrary path can
/// cross this contract.
/// </summary>
sealed class RuntimeWorldEntryController {
  const int MaxRequestBytes = 16 * 1024;
  static readonly HashSet<string> RequestFields = new(StringComparer.Ordinal) {
    "schema", "request_id", "created_utc", "expires_utc", "expected_machine",
    "expected_world_uid", "world_name", "world_display_name", "character_profile",
    "creator_session_id",
  };

  readonly string root;
  readonly string requestPath;
  readonly string consumedRoot;
  readonly string receiptRoot;
  readonly string statusPath;
  readonly Action<string> log;
  readonly RuntimeCreatorSessionAuthority creatorSession = new();
  RuntimeWorldEntryRequest request;

  /// <summary>The Creator Session that completed entry into the currently loaded world.
  /// Merely accepting or rejecting a request must not grant creator authority.</summary>
  public string CreatorSessionId => creatorSession.Current;

  public RuntimeWorldEntryController(string runtimeRoot, Action<string> logger = null) {
    root = Path.GetFullPath(runtimeRoot ?? throw new ArgumentNullException(nameof(runtimeRoot)));
    requestPath = Path.Combine(root, "requests", "world-entry.json");
    consumedRoot = Path.Combine(root, "requests", "world-entry-consumed");
    receiptRoot = Path.Combine(root, "receipts", "world-entry");
    statusPath = Path.Combine(root, "status", "world-entry.json");
    log = logger ?? (_ => { });
  }

  public bool TryStart(MonoBehaviour host) {
    if (host == null || !File.Exists(requestPath)) return false;
    string claimed = null;
    string error = null;
    try {
      Directory.CreateDirectory(consumedRoot);
      claimed = Path.Combine(consumedRoot,
          DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ", CultureInfo.InvariantCulture)
          + "-" + Guid.NewGuid().ToString("N") + ".json");
      File.Move(requestPath, claimed);
      var info = new FileInfo(claimed);
      if (info.Length <= 0 || info.Length > MaxRequestBytes) {
        error = "world_entry_request_size_invalid";
      } else {
        request = ReadStrict(claimed);
        RuntimeWorldEntryRequestPolicy.Validate(
            request, DateTimeOffset.UtcNow, out error);
      }
    } catch (Exception exception) {
      error = "world_entry_request_read_failed:" + exception.GetType().Name;
    }
    if (error != null) {
      Write("rejected", error, null, null);
      return false;
    }
    if (!string.Equals(request.ExpectedMachine, Environment.MachineName,
            StringComparison.OrdinalIgnoreCase)) {
      Write("rejected", "world_entry_machine_mismatch", null, null);
      return false;
    }
    creatorSession.BeginEntry();
    Application.runInBackground = true;
    Write("accepted", "world_entry_request_accepted", null, null);
    host.StartCoroutine(Drive());
    return true;
  }

  static RuntimeWorldEntryRequest ReadStrict(string path) {
    var settings = new JsonLoadSettings {
      CommentHandling = CommentHandling.Ignore,
      DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
      LineInfoHandling = LineInfoHandling.Ignore,
    };
    JToken token = JToken.Parse(File.ReadAllText(path), settings);
    if (token is not JObject value) throw new InvalidDataException("world_entry_object_required");
    if (value.Properties().Any(property => !RequestFields.Contains(property.Name)))
      throw new InvalidDataException("world_entry_unknown_field");
    return value.ToObject<RuntimeWorldEntryRequest>();
  }

  IEnumerator Drive() {
    const float pollSeconds = .5f;
    float menuDeadline = Time.realtimeSinceStartup + 180f;
    while (FejdStartup.instance == null && Time.realtimeSinceStartup < menuDeadline)
      yield return new WaitForSeconds(pollSeconds);
    FejdStartup fejd = FejdStartup.instance;
    if (fejd == null) {
      Write("rejected", "world_entry_main_menu_timeout", null, null);
      yield break;
    }
    while (!PlayFabManager.IsLoggedIn && Time.realtimeSinceStartup < menuDeadline)
      yield return new WaitForSeconds(pollSeconds);
    if (!PlayFabManager.IsLoggedIn) {
      Write("rejected", "world_entry_authentication_timeout", null, null);
      yield break;
    }

    Type startup = typeof(FejdStartup);
    FieldInfo profilesField = AccessTools.Field(startup, "m_profiles");
    FieldInfo profileIndexField = AccessTools.Field(startup, "m_profileIndex");
    MethodInfo showCharacters = AccessTools.Method(startup, "ShowCharacterSelection");
    MethodInfo updateCharacters = AccessTools.Method(startup, "UpdateCharacterList");
    MethodInfo setProfile = AccessTools.Method(startup, "SetSelectedProfile", new[] { typeof(string) });
    MethodInfo startCharacter = AccessTools.Method(startup, "OnCharacterStart");
    MethodInfo loadMainScene = AccessTools.Method(startup, "LoadMainScene");
    if (profilesField == null || profileIndexField == null || showCharacters == null
        || updateCharacters == null || setProfile == null || startCharacter == null
        || loadMainScene == null) {
      Write("rejected", "world_entry_valheim_api_unavailable", null, null);
      yield break;
    }

    try { showCharacters.Invoke(fejd, null); }
    catch (Exception exception) {
      Write("rejected", "world_entry_character_screen_failed:" + InnerType(exception), null, null);
      yield break;
    }
    yield return new WaitForSeconds(1f);

    IList profiles = null;
    while (Time.realtimeSinceStartup < menuDeadline) {
      try { updateCharacters.Invoke(fejd, null); }
      catch (Exception exception) {
        Write("rejected", "world_entry_character_refresh_failed:" + InnerType(exception), null, null);
        yield break;
      }
      yield return new WaitForSeconds(pollSeconds);
      profiles = profilesField.GetValue(fejd) as IList;
      if (profiles != null && profiles.Count > 0) break;
    }
    if (profiles == null || profiles.Count == 0) {
      Write("rejected", "world_entry_character_profiles_unavailable", null, null);
      yield break;
    }

    var matches = new List<(int Index, PlayerProfile Profile)>();
    for (int index = 0; index < profiles.Count; index++) {
      if (profiles[index] is not PlayerProfile profile) continue;
      string filename = null;
      try { filename = profile.GetFilename(); } catch { }
      if (string.Equals(filename, request.CharacterProfile,
              StringComparison.OrdinalIgnoreCase)) matches.Add((index, profile));
    }
    if (matches.Count == 0) {
      Write("rejected", "world_entry_character_profile_not_found", null, null);
      yield break;
    }
    if (matches.Count != 1) {
      Write("rejected", "world_entry_character_profile_ambiguous", null, null);
      yield break;
    }

    var selected = matches[0];
    try {
      profileIndexField.SetValue(fejd, selected.Index);
      setProfile.Invoke(fejd, new object[] { selected.Profile.GetFilename() });
      updateCharacters.Invoke(fejd, null);
    } catch (Exception exception) {
      Write("rejected", "world_entry_character_selection_failed:" + InnerType(exception), null, null);
      yield break;
    }
    yield return new WaitForSeconds(.5f);
    try { startCharacter.Invoke(fejd, null); }
    catch (Exception exception) {
      Write("rejected", "world_entry_character_start_failed:" + InnerType(exception), null, null);
      yield break;
    }
    Write("character_selected", "world_entry_character_selected", null, SafeCharacterName(selected.Profile));
    yield return new WaitForSeconds(.5f);

    World chosen = null;
    try {
      SaveSystem.ForceRefreshCache();
      World[] named = SaveSystem.GetWorldList()
          .Where(world => world != null && string.Equals(world.m_worldName, request.WorldName,
              StringComparison.OrdinalIgnoreCase)).ToArray();
      if (named.Length == 0) {
        Write("rejected", "world_entry_world_file_not_found", null, SafeCharacterName(selected.Profile));
        yield break;
      }
      World[] displayed = named.Where(world => string.Equals(world.m_name,
          request.WorldDisplayName, StringComparison.Ordinal)).ToArray();
      if (displayed.Length == 0) {
        Write("rejected", "world_entry_world_display_name_mismatch", null,
            SafeCharacterName(selected.Profile));
        yield break;
      }
      World[] exact = displayed.Where(world =>
          world.m_uid.ToString(CultureInfo.InvariantCulture) == request.ExpectedWorldUid).ToArray();
      if (exact.Length == 0) {
        Write("rejected", "world_entry_world_uid_mismatch", null, SafeCharacterName(selected.Profile));
        yield break;
      }
      if (exact.Length != 1) {
        Write("rejected", "world_entry_world_ambiguous", null, SafeCharacterName(selected.Profile));
        yield break;
      }
      chosen = exact[0];
      ZNet.SetServer(server: true, openServer: false, publicServer: false,
          serverName: string.Empty, password: string.Empty, world: chosen);
      loadMainScene.Invoke(fejd, null);
    } catch (Exception exception) {
      Write("rejected", "world_entry_world_load_failed:" + InnerType(exception), null,
          SafeCharacterName(selected.Profile));
      yield break;
    }
    Write("loading", "world_entry_scene_requested", null, SafeCharacterName(selected.Profile));

    float worldDeadline = Time.realtimeSinceStartup + 600f;
    while ((ZNet.instance == null || Player.m_localPlayer == null)
        && Time.realtimeSinceStartup < worldDeadline)
      yield return new WaitForSeconds(1f);
    if (ZNet.instance == null || Player.m_localPlayer == null) {
      Write("rejected", "world_entry_player_spawn_timeout", null,
          SafeCharacterName(selected.Profile));
      yield break;
    }
    string actualWorld;
    try { actualWorld = ZNet.instance.GetWorldUID().ToString(CultureInfo.InvariantCulture); }
    catch (Exception exception) {
      Write("rejected", "world_entry_world_identity_unavailable:" + InnerType(exception), null,
          SafeCharacterName(selected.Profile));
      yield break;
    }
    if (actualWorld != request.ExpectedWorldUid) {
      Write("rejected", "world_entry_loaded_world_mismatch", actualWorld,
          SafePlayerName(Player.m_localPlayer));
      yield break;
    }
    creatorSession.ConfirmEntered(request.CreatorSessionId);
    Write("entered", "world_entry_complete", actualWorld, SafePlayerName(Player.m_localPlayer));
  }

  void Write(string state, string detail, string worldUid, string characterName) {
    var receipt = new RuntimeWorldEntryReceipt {
      RequestId = request?.RequestId,
      CreatorSessionId = request?.CreatorSessionId,
      State = state,
      Detail = detail,
      Machine = Environment.MachineName,
      ExpectedWorldUid = request?.ExpectedWorldUid,
      WorldUid = worldUid,
      WorldName = request?.WorldName,
      WorldDisplayName = request?.WorldDisplayName,
      CharacterProfile = request?.CharacterProfile,
      CharacterName = characterName,
      CompletedUtc = DateTimeOffset.UtcNow,
    };
    try {
      WriteAtomic(statusPath, receipt);
      if (RuntimeWorldEntryRequestPolicy.CanAddressReceipt(request)) {
        Directory.CreateDirectory(receiptRoot);
        WriteAtomic(Path.Combine(receiptRoot, request.RequestId + ".json"), receipt);
      }
      log("[world-entry] " + state + ": " + detail);
    } catch (Exception exception) {
      log("[world-entry] receipt failed: " + exception.Message);
    }
  }

  static void WriteAtomic(string path, RuntimeWorldEntryReceipt receipt) {
    Directory.CreateDirectory(Path.GetDirectoryName(path));
    string temporary = path + ".tmp";
    File.WriteAllText(temporary, JsonConvert.SerializeObject(receipt, Formatting.Indented));
    if (File.Exists(path)) File.Replace(temporary, path, null);
    else File.Move(temporary, path);
  }

  static string InnerType(Exception exception) =>
      (exception.InnerException ?? exception).GetType().Name;

  static string SafeCharacterName(PlayerProfile profile) {
    try { return profile?.GetName(); } catch { return null; }
  }

  static string SafePlayerName(Player player) {
    try { return player?.GetPlayerName(); } catch { return null; }
  }
}
