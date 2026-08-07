namespace ComfyQuestLab;

using System;
using System.Collections.Generic;

/// <summary>The eight categories the event atlas sorts every seam into.
///
/// These are the same buckets as tools/component-packets/samples/valheim-event-atlas.json,
/// and they exist so a quest builder can filter a firehose down to the thing they are
/// trying to learn. Combat alone will bury you inside one fight.</summary>
public static class LabCategory {
  public const string Combat = "combat";
  public const string Harvest = "harvest";
  public const string Inventory = "inventory";
  public const string Building = "building";
  public const string Crafting = "crafting";
  public const string Progression = "progression";
  public const string World = "world";
  public const string Social = "social";

  /// <summary>Display order. Harvest is second on purpose: punching a tree is the first
  /// thing a new builder tries, and it should not be buried at the bottom of a list.</summary>
  public static readonly string[] All = {
    Combat, Harvest, Inventory, Building, Crafting, Progression, World, Social,
  };

  /// <summary>What the lab shows before anyone touches a filter. Combat and harvest are
  /// the two a builder can actually cause on purpose in the first minute; the rest stay
  /// off so the first impression is a readable list rather than a scroll.</summary>
  public static readonly string[] DefaultVisible = { Combat, Harvest };
}

/// <summary>Whether a quest can actually fire on a seam today.
///
/// This is the column the whole lab exists to make visible. The shipping mod produces
/// five event types from three hooks; EventType.cs names 34; and nothing in the game
/// tells a builder which is which. See tools/component-packets/EVENT-ATLAS.md.</summary>
public static class LabUsability {
  /// <summary>The shipping mod hooks it and a quest trigger matches what it produces.
  /// Exactly one seam qualifies today: Character.OnDeath.</summary>
  public const string Today = "today";

  /// <summary>The shipping mod hooks it and emits an event, but no trigger matches that
  /// event. Character.Damage emits first_hit; QuestTriggerEvaluator matches kill only.
  /// Visible and unusable, which is the trap this label exists to name.</summary>
  public const string ProducesEventNoTrigger = "produces-event-no-trigger";

  /// <summary>Patchable, and nothing in a shipping mod hooks it. Most of the atlas.</summary>
  public const string LabCandidate = "lab-candidate";
}

/// <summary>One thing the game did, as the lab saw it.
///
/// Deliberately a flat struct of strings: this is a teaching surface, and every field
/// here is something a builder needs to be able to read off the screen and type into a
/// quest-view.json. If a field cannot survive that round trip it does not belong.</summary>
public struct LabEvent {
  /// <summary>Wall clock, HH:mm:ss. Builders correlate against what they just did.</summary>
  public string At;

  /// <summary>One of <see cref="LabCategory"/>.</summary>
  public string Category;

  /// <summary>The seam that fired, e.g. "TreeBase.Damage". Matches the atlas Id exactly
  /// so a builder can look it up.</summary>
  public string Seam;

  /// <summary>What it happened to, in the vocabulary a quest would use: a creature name,
  /// a prefab name, "tree", "bush".</summary>
  public string Target;

  /// <summary>Free detail the seam thought was worth carrying — weapon skill, amount,
  /// item name. Never required.</summary>
  public string Detail;

  /// <summary>One of <see cref="LabUsability"/>. The honest answer to "can I build a
  /// quest on what I just saw?"</summary>
  public string Usability;

  public LabEvent(string category, string seam, string target, string detail, string usability) {
    At = DateTime.Now.ToString("HH:mm:ss");
    Category = category;
    Seam = seam;
    Target = target ?? string.Empty;
    Detail = detail ?? string.Empty;
    Usability = usability;
  }
}

/// <summary>A bounded, newest-last ring of what the game just did.
///
/// Bounded because combat is a firehose and an unbounded list in a Unity Update loop is
/// a memory leak with extra steps. The shipping mod keeps 12 rows for its own timeline
/// (TelemetryCoordinator.MaxRecentEvents); the lab keeps more because reading back over
/// what just happened IS the feature here, not a debugging aid.</summary>
public sealed class LabEventRing {
  readonly List<LabEvent> _rows = new();
  readonly int _capacity;
  readonly object _gate = new();

  public LabEventRing(int capacity) {
    _capacity = Math.Max(16, capacity);
  }

  public int Count { get { lock (_gate) { return _rows.Count; } } }

  /// <summary>Total ever seen, including rows trimmed off the front. A builder who
  /// wonders why the list is not growing needs to know the difference between "nothing
  /// fired" and "you scrolled past it".</summary>
  public long TotalSeen { get; private set; }

  public void Add(LabEvent row) {
    lock (_gate) {
      _rows.Add(row);
      TotalSeen++;
      if (_rows.Count > _capacity) {
        _rows.RemoveRange(0, _rows.Count - _capacity);
      }
    }
  }

  /// <summary>Newest first, filtered to the given categories, capped at
  /// <paramref name="max"/> rows. A null or empty category set means everything —
  /// the panel passes its filter state straight through.</summary>
  public List<LabEvent> Recent(ICollection<string> categories, string textMatch, int max) {
    var outRows = new List<LabEvent>();
    lock (_gate) {
      for (int i = _rows.Count - 1; i >= 0 && outRows.Count < max; i--) {
        LabEvent row = _rows[i];
        if (categories != null && categories.Count > 0 && !categories.Contains(row.Category)) {
          continue;
        }
        if (!string.IsNullOrEmpty(textMatch) && !Matches(row, textMatch)) {
          continue;
        }
        outRows.Add(row);
      }
    }
    return outRows;
  }

  public void Clear() {
    lock (_gate) { _rows.Clear(); }
  }

  static bool Matches(LabEvent row, string needle) {
    needle = needle.ToLowerInvariant();
    return (row.Seam ?? string.Empty).ToLowerInvariant().Contains(needle)
        || (row.Target ?? string.Empty).ToLowerInvariant().Contains(needle)
        || (row.Detail ?? string.Empty).ToLowerInvariant().Contains(needle);
  }
}
