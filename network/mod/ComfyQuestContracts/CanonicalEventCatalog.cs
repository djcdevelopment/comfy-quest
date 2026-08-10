namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;

/// <summary>Stable creator-facing identifiers. Presentation names never enter evaluator identity.</summary>
public static class CanonicalEventCatalog {
  static readonly string[] Names = {
    "attack_blocked", "character_healed", "character_staggered", "chat_sent", "container_emptied",
    "damage_dealt", "global_key_removed", "global_key_set", "item_consumed", "item_crafted",
    "item_dropped", "item_equipped", "item_picked_up", "item_unequipped", "kill",
    "max_health_changed", "piece_damaged", "piece_destroyed", "piece_placed", "piece_removed",
    "piece_repaired", "player_died", "player_teleported", "resource_damaged", "resource_picked",
    "sign_written", "skill_raised", "skills_lowered", "stamina_gained", "stamina_spent",
    "station_fuel_added", "station_input_added", "station_output_collected", "station_output_produced"
  };
  static readonly HashSet<string> Set = new(Names, StringComparer.OrdinalIgnoreCase);
  public static IReadOnlyList<string> All => Names;
  public static ISet<string> CreateSet() => new HashSet<string>(Set, StringComparer.OrdinalIgnoreCase);
  public static bool Contains(string name) => !string.IsNullOrWhiteSpace(name) && Set.Contains(name);
}
