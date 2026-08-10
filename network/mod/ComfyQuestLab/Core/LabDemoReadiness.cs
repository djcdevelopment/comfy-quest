namespace ComfyQuestLab;

using System;
using System.Collections.Generic;

/// <summary>Plain runtime facts used to assess whether a creator demo can start.
/// No rendered-frame claims belong here: visual acceptance remains human evidence.</summary>
public sealed class LabReadinessInput {
  public int HooksApplied;
  public int HooksExpected;
  public int CatalogEventCount;
  public int ManifestEventCount;
  public int QuestsLoaded;
  public int QuestsArmed;
  public int QuestLoadErrors;
  public int ArmedSchoolCount;
  public bool QuestsEnabled;
  public bool InputOwned;
  public string RuntimeProfile;
  public string ReleaseId;
  public bool HasLiveSuite;
  public int LiveSuiteRequired;
  public int LiveSuiteWitnessed;
  public int LiveSuiteCompleted;
  public string LiveSuiteVerdict;
}

/// <summary>Color-independent, creator-facing readiness result.</summary>
public sealed class LabReadinessSnapshot {
  public const string NeedsAttention = "needs-attention";
  public const string ReadyForLap = "ready-for-live-lap";
  public const string LiveLapProved = "live-lap-proved";

  public string Status;
  public string StatusLabel;
  public string Summary;
  public readonly List<string> Issues = new List<string>();
}

public static class LabDemoReadiness {
  public const int RequiredSchools = 8;

  public static LabReadinessSnapshot Assess(LabReadinessInput input) {
    input ??= new LabReadinessInput();
    var result = new LabReadinessSnapshot();

    if (input.HooksExpected <= 0) {
      result.Issues.Add("no integration hook outcomes are available");
    } else if (input.HooksApplied < input.HooksExpected) {
      result.Issues.Add((input.HooksExpected - input.HooksApplied)
          + " integration hook(s) unavailable");
    } else if (input.HooksApplied > input.HooksExpected) {
      result.Issues.Add("integration hook count is inconsistent: " + input.HooksApplied
          + " applied / " + input.HooksExpected + " expected");
    }
    if (input.CatalogEventCount <= 0 || input.ManifestEventCount <= 0) {
      result.Issues.Add("creator event catalog is unavailable");
    } else if (input.CatalogEventCount != input.ManifestEventCount) {
      result.Issues.Add("creator event drift: evaluator " + input.CatalogEventCount
          + " / manifest " + input.ManifestEventCount);
    }
    if (!input.QuestsEnabled) {
      result.Issues.Add("quest evaluation is disabled");
    }
    if (input.QuestLoadErrors > 0) {
      result.Issues.Add(input.QuestLoadErrors + " quest file load error(s)");
    }
    if (input.QuestsArmed <= 0) {
      result.Issues.Add("no bindable quests are armed");
    }
    if (input.ArmedSchoolCount < RequiredSchools) {
      result.Issues.Add("bindable example quests cover "
          + Math.Max(0, input.ArmedSchoolCount) + "/" + RequiredSchools + " schools");
    }
    if (!input.InputOwned) {
      result.Issues.Add("interactive panel input is not owned");
    }
    if (input.HasLiveSuite
        && string.Equals(input.LiveSuiteVerdict, "fail_double_completion",
            StringComparison.OrdinalIgnoreCase)) {
      result.Issues.Add("live suite detected a duplicate quest completion");
    }

    bool livePass = input.HasLiveSuite
        && string.Equals(input.LiveSuiteVerdict, "pass", StringComparison.OrdinalIgnoreCase)
        && input.LiveSuiteRequired > 0
        && input.LiveSuiteWitnessed == input.LiveSuiteRequired
        && input.LiveSuiteCompleted == input.LiveSuiteRequired;
    if (result.Issues.Count > 0) {
      result.Status = LabReadinessSnapshot.NeedsAttention;
      result.StatusLabel = "[CHECK] NEEDS ATTENTION";
    } else if (livePass) {
      result.Status = LabReadinessSnapshot.LiveLapProved;
      result.StatusLabel = "[PROVED] LIVE LAP WITNESSED";
    } else {
      result.Status = LabReadinessSnapshot.ReadyForLap;
      result.StatusLabel = "[READY] READY FOR LIVE LAP";
    }

    string live = input.HasLiveSuite
        ? Math.Max(0, input.LiveSuiteWitnessed) + "/" + Math.Max(0, input.LiveSuiteRequired)
            + " witnessed, " + Math.Max(0, input.LiveSuiteCompleted) + "/"
            + Math.Max(0, input.LiveSuiteRequired) + " completed"
        : "not run in this process";
    string release = string.IsNullOrWhiteSpace(input.ReleaseId) ? "unknown release" : input.ReleaseId;
    string profile = string.IsNullOrWhiteSpace(input.RuntimeProfile)
        ? "unknown profile"
        : input.RuntimeProfile;
    result.Summary = result.StatusLabel + "  /  " + release + "  /  " + profile + "  /  "
        + Math.Max(0, input.HooksApplied) + "/" + Math.Max(0, input.HooksExpected)
        + " hooks  /  " + Math.Max(0, input.CatalogEventCount) + " creator events  /  "
        + Math.Max(0, input.QuestsArmed) + "/" + Math.Max(0, input.QuestsLoaded)
        + " quests armed  /  "
        + Math.Max(0, input.ArmedSchoolCount) + "/" + RequiredSchools
        + " example schools  /  live: " + live;
    return result;
  }

  public static int DistinctKnownSchools(IEnumerable<string> schools) {
    var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    if (schools == null) {
      return 0;
    }
    foreach (string school in schools) {
      foreach (string expected in LabCategory.All) {
        if (string.Equals(school, expected, StringComparison.OrdinalIgnoreCase)) {
          known.Add(expected);
          break;
        }
      }
    }
    return known.Count;
  }
}
