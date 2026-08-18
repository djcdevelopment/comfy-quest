namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;

/// <summary>Privacy-minimal event vocabulary for listen-host cooperative quests.</summary>
public static class CooperativeEventContract {
  public const string ChatReceivedEvent = "chat_received";
  public const string PeerRole = "peer";
  public const string ListenHostRole = "listen_host";

  public static bool TryCreateInboundChat(
      long senderId,
      long localCharacterUserId,
      string chatMode,
      string message,
      DateTimeOffset at,
      out RuntimeEvent runtimeEvent) {
    runtimeEvent = null;
    if (senderId == 0L || localCharacterUserId == 0L || string.IsNullOrWhiteSpace(message)) {
      return false;
    }
    string mode = Normalize(chatMode);
    if (mode != "normal" && mode != "shout") {
      return false;
    }
    runtimeEvent = new RuntimeEvent {
      Name = ChatReceivedEvent,
      Target = mode,
      At = at,
      Fields = new Dictionary<string, string> {
        ["actor_role"] = senderId == localCharacterUserId ? ListenHostRole : PeerRole,
      },
    };
    return true;
  }

  public static RuntimeEvent CreateHostPlacement(string prefab, DateTimeOffset at) => new() {
    Name = "piece_placed",
    Target = Normalize(prefab),
    At = at,
    Fields = new Dictionary<string, string> { ["actor_role"] = ListenHostRole },
  };

  public static string ActorRole(RuntimeEvent runtimeEvent) {
    return runtimeEvent?.Fields != null
        && runtimeEvent.Fields.TryGetValue("actor_role", out string role)
      ? role
      : null;
  }

  static string Normalize(string value) {
    string normalized = (value ?? string.Empty).Trim();
    if (normalized.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase)) {
      normalized = normalized.Substring(0, normalized.Length - "(Clone)".Length).Trim();
    }
    return normalized.ToLowerInvariant();
  }
}

/// <summary>Privacy-minimal, one-player signals used by the fast Quest Studio loop.</summary>
public static class EasyEventContract {
  public const string ChatSentEvent = "chat_sent";
  public const string ItemDroppedEvent = "item_dropped";
  public const string ItemPickedUpEvent = "item_picked_up";
  public const string ItemEquippedEvent = "item_equipped";
  public const string ItemConsumedEvent = "item_consumed";
  public const string CharacterHealedEvent = "character_healed";

  public static RuntimeEvent ChatSent(string chatMode, string message, DateTimeOffset at) {
    if (string.IsNullOrWhiteSpace(message)) return null;
    var mode = NormalizeChatMode(chatMode);
    if (mode == null) return null;
    return new RuntimeEvent { Name = ChatSentEvent, Target = mode, At = at };
  }

  public static RuntimeEvent ItemDropped(string prefab, int quantity, DateTimeOffset at) {
    if (quantity <= 0) return null;
    return Inventory(ItemDroppedEvent, prefab, at);
  }

  public static RuntimeEvent ItemPickedUp(string prefab, DateTimeOffset at) =>
    Inventory(ItemPickedUpEvent, prefab, at);

  public static RuntimeEvent ItemEquipped(string prefab, DateTimeOffset at) =>
    Inventory(ItemEquippedEvent, prefab, at);

  public static RuntimeEvent ItemConsumed(string prefab, DateTimeOffset at) =>
    Inventory(ItemConsumedEvent, prefab, at);

  public static RuntimeEvent CharacterHealed(float amount, DateTimeOffset at) => amount > 0f
    ? new RuntimeEvent { Name = CharacterHealedEvent, Target = "you", At = at }
    : null;

  static RuntimeEvent Inventory(string eventName, string prefab, DateTimeOffset at) {
    var target = NormalizePrefab(prefab);
    return target == null ? null : new RuntimeEvent { Name = eventName, Target = target, At = at };
  }

  static string NormalizeChatMode(string value) {
    var mode = (value ?? string.Empty).Trim().ToLowerInvariant();
    return mode == "shout" ? "shout" : mode == "normal" ? "normal" : null;
  }

  static string NormalizePrefab(string value) {
    var result = (value ?? string.Empty).Trim();
    if (result.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
      result = result.Substring(0, result.Length - "(Clone)".Length).Trim();
    return result.Length == 0 ? null : result;
  }
}

/// <summary>Privacy-minimal events for the searchable Core local-actions cohort.</summary>
public static class CoreActionEventContract {
  public const string ContainerEmptiedEvent = "container_emptied";
  public const string ItemUnequippedEvent = "item_unequipped";
  public const string PieceDestroyedEvent = "piece_destroyed";
  public const string PieceRemovedEvent = "piece_removed";
  public const string PieceRepairedEvent = "piece_repaired";
  public const string PlayerTeleportedEvent = "player_teleported";

  public static RuntimeEvent ContainerEmptied(string prefab, DateTimeOffset at) =>
    WithPrefab(ContainerEmptiedEvent, prefab, at);

  public static RuntimeEvent ItemUnequipped(string prefab, DateTimeOffset at) =>
    WithPrefab(ItemUnequippedEvent, prefab, at);

  public static RuntimeEvent PieceDestroyed(string prefab, DateTimeOffset at) =>
    WithPrefab(PieceDestroyedEvent, prefab, at);

  public static RuntimeEvent PieceRemoved(DateTimeOffset at) => new() {
    Name = PieceRemovedEvent, Target = "piece", At = at,
  };

  public static RuntimeEvent PieceRepaired(string prefab, DateTimeOffset at) =>
    WithPrefab(PieceRepairedEvent, prefab, at);

  public static RuntimeEvent PlayerTeleported(DateTimeOffset at) => new() {
    Name = PlayerTeleportedEvent, At = at,
  };

  static RuntimeEvent WithPrefab(string eventName, string prefab, DateTimeOffset at) {
    string target = NormalizePrefab(prefab);
    return target == null ? null : new RuntimeEvent { Name = eventName, Target = target, At = at };
  }

  static string NormalizePrefab(string value) {
    string result = (value ?? string.Empty).Trim();
    if (result.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
      result = result.Substring(0, result.Length - "(Clone)".Length).Trim();
    return result.Length == 0 ? null : result;
  }
}

/// <summary>Pure result gates shared by Runtime adapters and headless contract tests.</summary>
public static class CoreActionProof {
  public static bool ContainerEmptied(
      bool localPlayer, bool granted, int itemsBefore, int itemsAfter, double elapsedSeconds) =>
    localPlayer && granted && itemsBefore > 0 && itemsAfter == 0
      && elapsedSeconds >= 0.0 && elapsedSeconds <= 10.0;

  public static bool ItemUnequipped(
      bool localPlayer, bool wasEquipped, bool isEquippedAfter) =>
    localPlayer && wasEquipped && !isEquippedAfter;

  public static bool PieceDestroyed(bool localPlayerAttacker) => localPlayerAttacker;

  public static bool PieceRemoved(bool localPlayer, bool succeeded) =>
    localPlayer && succeeded;

  public static bool PieceRepaired(bool localPlayer, bool succeeded) =>
    localPlayer && succeeded;

  public static bool PlayerTeleported(bool localPlayer, bool succeeded) =>
    localPlayer && succeeded;
}

/// <summary>Privacy-minimal events for the production Combat + Harvest cohort.</summary>
public static class CombatHarvestEventContract {
  public const string AttackBlockedEvent = "attack_blocked";
  public const string CharacterStaggeredEvent = "character_staggered";
  public const string DamageDealtEvent = "damage_dealt";
  public const string ResourceDamagedEvent = "resource_damaged";
  public const string ResourcePickedEvent = "resource_picked";

  public static RuntimeEvent AttackBlocked(
      string creature, string weaponSkill, bool projectile, DateTimeOffset at) =>
    WithCombat(AttackBlockedEvent, creature, weaponSkill, projectile, at);

  public static RuntimeEvent CharacterStaggered(
      string creature, string weaponSkill, bool projectile, DateTimeOffset at) =>
    WithCombat(CharacterStaggeredEvent, creature, weaponSkill, projectile, at);

  public static RuntimeEvent DamageDealt(
      string creature, string weaponSkill, bool projectile, DateTimeOffset at) =>
    WithCombat(DamageDealtEvent, creature, weaponSkill, projectile, at);

  public static RuntimeEvent ResourceDamaged(
      string prefab, string weaponSkill, bool projectile, DateTimeOffset at) =>
    WithCombat(ResourceDamagedEvent, prefab, weaponSkill, projectile, at);

  public static RuntimeEvent ResourcePicked(string prefab, DateTimeOffset at) {
    string target = NormalizeTarget(prefab);
    return target == null ? null : new RuntimeEvent {
      Name = ResourcePickedEvent, Target = target, At = at,
    };
  }

  static RuntimeEvent WithCombat(
      string eventName, string targetValue, string weaponSkill,
      bool projectile, DateTimeOffset at) {
    string target = NormalizeTarget(targetValue);
    string skill = NormalizeSkill(weaponSkill);
    if (target == null || skill == null) return null;
    return new RuntimeEvent {
      Name = eventName,
      Target = target,
      At = at,
      Fields = new Dictionary<string, string> {
        ["weapon_skill"] = skill,
        ["projectile"] = projectile ? "true" : "false",
      },
    };
  }

  static string NormalizeTarget(string value) {
    string result = (value ?? string.Empty).Trim();
    if (result.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
      result = result.Substring(0, result.Length - "(Clone)".Length).Trim();
    return result.Length == 0 || result.Length > 128 ? null : result;
  }

  static string NormalizeSkill(string value) {
    string result = (value ?? string.Empty).Trim();
    return result.Length == 0 || result.Length > 128 ? null : result;
  }
}

/// <summary>Pure outcome gates shared by Combat/Harvest adapters and headless tests.</summary>
public static class CombatHarvestProof {
  public static bool DamageDealt(
      bool localPlayerAttacker, bool creatureTarget,
      double healthBefore, double healthAfter) =>
    localPlayerAttacker && creatureTarget && Decreased(healthBefore, healthAfter);

  public static bool CharacterStaggered(
      bool attributedToLocalPlayer, bool wasStaggering, bool staggerAccepted) =>
    attributedToLocalPlayer && !wasStaggering && staggerAccepted;

  public static bool AttackBlocked(bool localPlayer, bool succeeded) =>
    localPlayer && succeeded;

  public static bool ResourceDamaged(
      bool localPlayerAttacker, bool authoritativeOwner,
      bool healthChanged) =>
    localPlayerAttacker && authoritativeOwner && healthChanged;

  public static bool ResourcePicked(
      bool localSender, bool authoritativeOwner,
      bool wasPicked, bool isPickedAfter) =>
    localSender && authoritativeOwner && !wasPicked && isPickedAfter;

  static bool Decreased(double before, double after) =>
    !double.IsNaN(before) && !double.IsInfinity(before)
      && !double.IsNaN(after) && !double.IsInfinity(after)
      && before > after;
}

/// <summary>Privacy-minimal events for crafting and host-owned production stations.</summary>
public static class CraftingEventContract {
  public static RuntimeEvent ItemCrafted(string station, DateTimeOffset at) =>
    Event("item_crafted", Normalize(station) ?? "handcrafting", at);

  public static RuntimeEvent StationFuelAdded(
      string station, string item, DateTimeOffset at) =>
    StationEvent("station_fuel_added", station, item, 1, at);

  public static RuntimeEvent StationInputAdded(
      string station, string item, DateTimeOffset at) =>
    StationEvent("station_input_added", station, item, 1, at);

  public static RuntimeEvent StationOutputCollected(
      string station, int quantity, DateTimeOffset at) =>
    StationEvent("station_output_collected", station, "cooking output", quantity, at);

  public static RuntimeEvent StationOutputProduced(
      string station, string outputItem, int quantity, DateTimeOffset at) =>
    StationEvent("station_output_produced", station, outputItem, quantity, at);

  static RuntimeEvent StationEvent(
      string name, string stationValue, string itemValue, int quantity, DateTimeOffset at) {
    string station = Normalize(stationValue);
    string item = Normalize(itemValue);
    if (station == null || item == null || quantity <= 0) return null;
    return new RuntimeEvent {
      Name = name, Target = item, At = at,
      Fields = new Dictionary<string, string> {
        ["station"] = station,
        ["item"] = item,
        ["quantity"] = quantity.ToString(System.Globalization.CultureInfo.InvariantCulture),
      },
    };
  }

  static RuntimeEvent Event(string name, string target, DateTimeOffset at) =>
    target == null ? null : new RuntimeEvent { Name = name, Target = target, At = at };

  static string Normalize(string value) {
    string result = (value ?? string.Empty).Trim();
    if (result.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
      result = result.Substring(0, result.Length - "(Clone)".Length).Trim();
    return result.Length == 0 || result.Length > 128 ? null : result;
  }
}

public static class CraftingProof {
  public static bool ItemCrafted(
      bool localPlayer, bool newCraft, int outputBefore, int outputAfter) =>
    localPlayer && newCraft && outputAfter > outputBefore;

  public static bool StationValueIncreased(
      bool localActor, bool authoritativeOwner, double before, double after) =>
    localActor && authoritativeOwner && Finite(before) && Finite(after) && after > before;

  public static bool StationSlotAdded(
      bool localActor, bool authoritativeOwner, int before, int after) =>
    localActor && authoritativeOwner && before >= 0 && after > before;

  public static bool StationOutputCollected(
      bool localActor, bool authoritativeOwner, int doneBefore, int doneAfter, int quantity) =>
    localActor && authoritativeOwner && quantity > 0 && doneBefore > doneAfter;

  public static bool StationOutputProduced(
      bool authoritativeOwner, bool conversionExists, int quantity) =>
    authoritativeOwner && conversionExists && quantity > 0;

  static bool Finite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
}

/// <summary>
/// Coalesces the nested Shout callbacks without persisting sender IDs, names, or message text.
/// A repeated witness is a new message; its alternate witness joins the same short-lived action.
/// </summary>
public sealed class CooperativeChatDedupe {
  sealed class State {
    public double At;
    public readonly HashSet<string> Witnesses = new(StringComparer.Ordinal);
  }

  readonly double windowSeconds;
  readonly Dictionary<string, State> states = new(StringComparer.Ordinal);

  public CooperativeChatDedupe(double correlationSeconds = 0.75) {
    windowSeconds = Math.Max(0.01, correlationSeconds);
  }

  public bool ShouldEmit(
      long senderId,
      string chatMode,
      string message,
      string witness,
      double now) {
    // GetHashCode is deliberately process-local: this key is never serialized or receipted.
    string key = senderId + "\n" + (chatMode ?? string.Empty) + "\n"
        + (message ?? string.Empty).GetHashCode();
    string route = witness ?? string.Empty;
    if (states.TryGetValue(key, out State state)
        && now >= state.At
        && now - state.At <= windowSeconds
        && !state.Witnesses.Contains(route)) {
      state.At = now;
      state.Witnesses.Add(route);
      return false;
    }
    if (states.Count >= 256) {
      states.Clear();
    }
    var next = new State { At = now };
    next.Witnesses.Add(route);
    states[key] = next;
    return true;
  }

  public void Clear() => states.Clear();
}
