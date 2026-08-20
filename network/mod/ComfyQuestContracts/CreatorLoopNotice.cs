namespace ComfyQuestContracts;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

/// <summary>The evidence-row taxonomy from the creator-loop design language. A row's kind
/// is a fact tagged where the line is composed — never re-derived by parsing the rendered
/// string — so a copy change can never silently reclassify a row.</summary>
public enum CreatorEvidenceKind { Plumbing, Story, Cast, Warning }

/// <summary>One evidence row: what happened, and which voice says it. Story advances the
/// telling; Cast is the signature charm moment (the only tinted row); Warning needs a
/// decision and always says what to do next; Plumbing is machinery talking to itself.</summary>
public sealed class CreatorEvidenceLine {
  public CreatorEvidenceKind Kind { get; set; }
  public string Text { get; set; }

  /// <summary>The wire name a receipt carries for a kind. Unknown or null fails closed
  /// to plumbing — the quiet voice, never the loud one.</summary>
  public static string KindName(CreatorEvidenceKind kind) => kind switch {
    CreatorEvidenceKind.Story => "story",
    CreatorEvidenceKind.Cast => "cast",
    CreatorEvidenceKind.Warning => "warning",
    _ => "plumbing",
  };
}

/// <summary>The status card's state grammar. The state is a fact for the renderer's color
/// choice — Ready green for playing/up to date, Amber for the one pending action, Steel for
/// an informational choice — never re-derived by parsing the state line (the DeadlineUrgent
/// rule).</summary>
public enum QuestCardState { Playing, UpToDate, UpdateReady, Choice }

/// <summary>The status card's one state line beside the running quest's title, with the
/// state that colors it.</summary>
public sealed class QuestCardStatus {
  public QuestCardState State { get; set; }
  public string Line { get; set; }
}

/// <summary>One creator-facing sentence about the check/load loop.
///
/// A pure fact type, mirroring TriggerCountdown: the plugin renders, it never composes
/// creator copy inline, so every sentence is provable without the game. The Headline is
/// creator altitude — the quest's title first, version second, and never a pack id, a
/// content hash, an absolute path, or a snake_case diagnostic. Raw identity lives in
/// Detail, which only the drawer's maintainer surface shows.</summary>
public sealed class CreatorLoopNotice {
  public string Headline { get; set; }
  public string Detail { get; set; }

  /// <summary>True when the loop had nothing new to say — already playing, empty inbox,
  /// nothing to load. Session 1 of the Phase 3 exit lap showed these responses merging
  /// invisibly into the HUD line already on screen; the renderer uses this fact to make an
  /// idle repeat re-assert itself. Tagged where the sentence is composed, never inferred
  /// from the rendered copy.</summary>
  public bool Idle { get; set; }

  /// <summary>What a check proved. Never activation language: activating is the load
  /// step's verb, and the validation lap recorded "loaded" here as the ambiguity.</summary>
  public static CreatorLoopNotice Check(
      IReadOnlyList<PackCandidate> candidates, ActiveSet active,
      string loadKey = "F11", string drawerKey = "F9") {
    candidates ??= Array.Empty<PackCandidate>();
    var valid = candidates.Where(value => value.IsValid)
        .OrderByDescending(value => SemanticVersion.Parse(value.Manifest.Version))
        .ThenBy(value => value.Manifest.PackId, StringComparer.Ordinal)
        .ToArray();
    var rejected = candidates.Count - valid.Length;
    var rejectedSuffix = rejected == 0 ? ""
        : " " + RejectedSentence(rejected, candidates.Count, drawerKey);
    if (candidates.Count == 0)
      return new() { Headline = "No new quests in your inbox.", Idle = true };
    if (valid.Length == 0)
      return new() {
        Headline = RejectedSentence(rejected, candidates.Count, drawerKey),
        Detail = CheckDetail(candidates.Count, 0, null),
      };
    var latest = valid[0];
    var detail = CheckDetail(candidates.Count, valid.Length, latest);
    if (Matches(active, latest))
      return new() {
        Headline = Title(latest) + " " + latest.Manifest.Version
            + " is already playing. Nothing new to load." + rejectedSuffix,
        Detail = detail,
        Idle = rejected == 0,
      };
    if (valid.Length == 1)
      return new() {
        Headline = Title(latest) + " " + latest.Manifest.Version
            + " is ready. Press " + loadKey + " to play it." + rejectedSuffix,
        Detail = detail,
      };
    return new() {
      Headline = valid.Length.ToString(CultureInfo.InvariantCulture)
          + " quests are ready. Open " + drawerKey + " to choose." + rejectedSuffix,
      Detail = detail,
    };
  }

  public static CreatorLoopNotice CheckFailed(string error, string drawerKey = "F9") =>
      new() {
        Headline = "Couldn't read your quest inbox. Open " + drawerKey + " for the reason.",
        Detail = error,
      };

  public static CreatorLoopNotice Loaded(
      PackCandidate candidate, ActiveSet active, int orphanedCharms) =>
      new() {
        Headline = "Now playing: " + Title(candidate) + " — " + candidate?.Manifest?.Version
            + OrphanSentence(orphanedCharms),
        Detail = Identity(candidate)
            + (string.IsNullOrWhiteSpace(active?.ActivationId) ? ""
                : " · activation " + Tail(active.ActivationId)),
      };

  public static CreatorLoopNotice AlreadyPlaying(PackCandidate candidate) =>
      new() {
        Headline = Title(candidate) + " " + candidate?.Manifest?.Version
            + " is already playing.",
        Detail = Identity(candidate),
        Idle = true,
      };

  public static CreatorLoopNotice NothingToLoad(string checkKey = "F10") =>
      new() {
        Headline = "Nothing to load. Press " + checkKey + " to check your inbox.",
        Idle = true,
      };

  public static CreatorLoopNotice LoadFailed(string error, string drawerKey = "F9") =>
      new() {
        Headline = "That quest couldn't be loaded. Open " + drawerKey + " for the reason.",
        Detail = error,
      };

  /// <summary>The authored title of whatever is actually running: the title of the candidate
  /// whose content hash matches the active set. Null when the running revision's pack is no
  /// longer inspectable — there is honestly no title then, and surfaces fall back to pack id.
  /// One shared fact so the drawer and Studio can never disagree about the quest's name.</summary>
  public static string ActiveTitle(IReadOnlyList<PackCandidate> candidates, ActiveSet active) {
    if (active == null || string.IsNullOrWhiteSpace(active.ContentHash)) return null;
    var match = candidates?.FirstOrDefault(value =>
        string.Equals(value?.ContentHash, active.ContentHash, StringComparison.OrdinalIgnoreCase));
    return match?.Titles?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
  }

  /// <summary>The status card's state line, composed from the same candidate ordering the
  /// check uses. UpToDate exists because session 1 of the Phase 3 exit lap read the loop's
  /// idle silence as broken keys: once a check this session has proved the inbox holds
  /// nothing newer, the card says so instead of staying quiet. Null when nothing is
  /// playing — the renderer keeps its own empty state.</summary>
  public static QuestCardStatus Card(
      IReadOnlyList<PackCandidate> candidates, ActiveSet active, bool checkedThisSession) {
    if (active == null) return null;
    var valid = (candidates ?? Array.Empty<PackCandidate>()).Where(value => value.IsValid)
        .OrderByDescending(value => SemanticVersion.Parse(value.Manifest.Version))
        .ThenBy(value => value.Manifest.PackId, StringComparer.Ordinal)
        .ToArray();
    var latest = valid.Length == 0 ? null : valid[0];
    if (latest != null && !Matches(active, latest))
      return valid.Length > 1
          ? new() {
              State = QuestCardState.Choice,
              Line = valid.Length.ToString(CultureInfo.InvariantCulture)
                  + " quests ready — choose",
            }
          : new() {
              State = QuestCardState.UpdateReady,
              Line = latest.Manifest.Version + " is ready — play it",
            };
    return checkedThisSession
        ? new() { State = QuestCardState.UpToDate, Line = "Now playing — up to date" }
        : new() { State = QuestCardState.Playing, Line = "Now playing" };
  }

  /// <summary>Maintainer identity for the card's Details disclosure — pack id, short hash,
  /// local activation time, activation tail. The card's face stays creator altitude; this
  /// line is the proof one click below it.</summary>
  public static string CardDetail(ActiveSet active) =>
      active == null ? null
      : (active.PackId ?? "unknown") + " · " + ShortHash(active.ContentHash)
        + (active.ActivatedUtc == default ? ""
            : " · " + active.ActivatedUtc.ToLocalTime()
                .ToString("HH:mm", CultureInfo.InvariantCulture))
        + (string.IsNullOrWhiteSpace(active.ActivationId) ? ""
            : " · activation " + Tail(active.ActivationId));

  static bool Matches(ActiveSet active, PackCandidate candidate) =>
      active != null && candidate?.Manifest != null
      && string.Equals(active.PackId, candidate.Manifest.PackId, StringComparison.Ordinal)
      && string.Equals(active.Version, candidate.Manifest.Version, StringComparison.Ordinal)
      && string.Equals(active.ContentHash, candidate.ContentHash, StringComparison.OrdinalIgnoreCase);

  /// <summary>The quest's authored title; the pack id is a last resort, not the voice.</summary>
  static string Title(PackCandidate candidate) =>
      candidate?.Titles?.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
      ?? candidate?.Manifest?.PackId ?? "This quest";

  static string RejectedSentence(int rejected, int total, string drawerKey) =>
      (rejected == total
          ? total == 1 ? "That quest can't be loaded."
              : total.ToString(CultureInfo.InvariantCulture) + " quests can't be loaded."
          : rejected.ToString(CultureInfo.InvariantCulture) + " of "
              + total.ToString(CultureInfo.InvariantCulture) + " quests can't be loaded.")
      + " Open " + drawerKey + " for the reason.";

  static string OrphanSentence(int orphanedCharms) =>
      orphanedCharms <= 0 ? ""
      : orphanedCharms == 1
          ? ". 1 charm belongs to an earlier telling. Re-CAST it or roll back."
          : ". " + orphanedCharms.ToString(CultureInfo.InvariantCulture)
              + " charms belong to an earlier telling. Re-CAST them or roll back.";

  static string CheckDetail(int total, int valid, PackCandidate latest) =>
      total.ToString(CultureInfo.InvariantCulture) + " checked · "
      + valid.ToString(CultureInfo.InvariantCulture) + " valid"
      + (latest == null ? "" : " · latest " + Identity(latest));

  static string Identity(PackCandidate candidate) =>
      (candidate?.Manifest?.PackId ?? "unknown") + " " + candidate?.Manifest?.Version
      + " · " + ShortHash(candidate?.ContentHash);

  static string ShortHash(string value) =>
      string.IsNullOrWhiteSpace(value) ? "no hash" : value.Substring(0, Math.Min(8, value.Length));

  static string Tail(string value) =>
      string.IsNullOrWhiteSpace(value) ? "" : value.Substring(Math.Max(0, value.Length - 8));
}
