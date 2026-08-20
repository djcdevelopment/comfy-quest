namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Unity-free decision core for reusing an existing Gallery site.</summary>
public static class LabGalleryOriginContract {
  public sealed class Portal {
    public string BuildId;
    public float X;
    public float Y;
    public float Z;
  }

  public sealed class Decision {
    public bool Succeeded;
    public bool Found;
    public float X;
    public float Y;
    public float Z;
    public string Error;
  }

  public static Decision Decide(
      bool anyGallery,
      bool otherProfile,
      bool requireOnlyRequestedProfile,
      float groundPortalX,
      float groundPortalZ,
      IEnumerable<Portal> candidates) {
    if (!anyGallery) {
      return Good(false, 0f, 0f, 0f);
    }
    if (requireOnlyRequestedProfile && otherProfile) {
      return Bad("marked Gallery objects from another profile share the world");
    }

    List<Portal> rows = (candidates ?? Array.Empty<Portal>()).ToList();
    if (rows.Any(row => row == null || string.IsNullOrWhiteSpace(row.BuildId))) {
      return Bad("a matching ascent portal has no build identity");
    }
    List<IGrouping<string, Portal>> builds = rows
        .GroupBy(row => row.BuildId, StringComparer.OrdinalIgnoreCase)
        .ToList();
    if (builds.Count != 1) {
      return Bad(builds.Count == 0
          ? "marked objects exist but no complete matching ascent pair was found"
          : "more than one matching build exists; select and clear the stale site first");
    }

    List<Portal> pair = builds[0].ToList();
    if (pair.Count != 2) {
      return Bad("the reusable site needs exactly two matching ascent portals");
    }
    Portal ground = pair[0].Y <= pair[1].Y ? pair[0] : pair[1];
    Portal raised = pair[0].Y <= pair[1].Y ? pair[1] : pair[0];
    if (raised.Y - ground.Y < 0.5f) {
      return Bad("the ascent portal pair has no trustworthy lower terrain anchor");
    }
    return Good(true, ground.X - groundPortalX, ground.Y, ground.Z - groundPortalZ);
  }

  static Decision Good(bool found, float x, float y, float z) {
    return new Decision {
      Succeeded = true,
      Found = found,
      X = x,
      Y = y,
      Z = z,
      Error = string.Empty,
    };
  }

  static Decision Bad(string error) {
    return new Decision {
      Succeeded = false,
      Found = false,
      Error = error,
    };
  }
}
