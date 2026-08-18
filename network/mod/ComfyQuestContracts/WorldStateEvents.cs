namespace ComfyQuestContracts;

using System;

/// <summary>Bounded world-authority events; exact global-key values are public game state.</summary>
public static class WorldStateEventContract {
  public const string GlobalKeyRemovedEvent = "global_key_removed";
  public const string GlobalKeySetEvent = "global_key_set";

  public static RuntimeEvent GlobalKeyRemoved(string key, DateTimeOffset at) =>
      Event(GlobalKeyRemovedEvent, key, at);

  public static RuntimeEvent GlobalKeySet(string key, DateTimeOffset at) =>
      Event(GlobalKeySetEvent, key, at);

  static RuntimeEvent Event(string name, string key, DateTimeOffset at) {
    string target = (key ?? string.Empty).Trim();
    return target.Length == 0 || target.Length > 128 ? null : new RuntimeEvent {
      Name = name, Target = target, At = at,
    };
  }
}

/// <summary>Pure authority and no-op gates for world-state adapters.</summary>
public static class WorldStateProof {
  public static bool GlobalKeySet(bool worldAuthority, bool wasSet, bool isSetAfter) =>
      worldAuthority && !wasSet && isSetAfter;

  public static bool GlobalKeyRemoved(bool worldAuthority, bool wasSet, bool isSetAfter) =>
      worldAuthority && wasSet && !isSetAfter;
}
