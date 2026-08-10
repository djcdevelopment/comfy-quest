namespace ComfyQuestContracts;

using System;

public enum CharmTargetKind { Unknown, PlayerBuiltPiece, Sign, ItemStand, DedicatedCharm, Creature, Portal, Terrain, Container, DroppedItem }

public sealed class WorldAuthority {
  public bool IsPrivateWorld { get; set; }
  public bool IsSolo { get; set; }
  public bool IsListenHost { get; set; }
  public bool IsDedicated { get; set; }
  public bool IsPeerClient { get; set; }
}

public sealed class CharmTarget {
  public CharmTargetKind Kind { get; set; }
  public bool HasZdo { get; set; }
  public bool LocallyOwned { get; set; }
}

public sealed class CharmReference {
  public string PackId { get; set; }
  public string ExperienceId { get; set; }
  public string BindingId { get; set; }
  public string Version { get; set; }
  public string ContentHash { get; set; }
}

public sealed class PolicyDecision {
  PolicyDecision(bool allowed,string diagnostic){Allowed=allowed;Diagnostic=diagnostic;}
  public bool Allowed { get; }
  public string Diagnostic { get; }
  public static PolicyDecision Allow() => new(true,null);
  public static PolicyDecision Deny(string diagnostic) => new(false,diagnostic);
}

public static class CharmPolicy {
  public static PolicyDecision CanInscribe(WorldAuthority world, CharmTarget target) {
    var authority=CanMutate(world);if(!authority.Allowed)return authority;
    if(target==null||!target.HasZdo)return PolicyDecision.Deny("charm_zdo_required");
    if(!target.LocallyOwned)return PolicyDecision.Deny("charm_local_ownership_required");
    return target.Kind is CharmTargetKind.PlayerBuiltPiece or CharmTargetKind.Sign or CharmTargetKind.ItemStand or CharmTargetKind.DedicatedCharm
      ? PolicyDecision.Allow() : PolicyDecision.Deny("charm_target_not_allowlisted");
  }
  public static PolicyDecision CanMutate(WorldAuthority world) {
    if(world==null||!world.IsPrivateWorld)return PolicyDecision.Deny("private_world_required");
    if(world.IsDedicated||world.IsPeerClient)return PolicyDecision.Deny("mutation_authority_unavailable");
    return world.IsSolo||world.IsListenHost?PolicyDecision.Allow():PolicyDecision.Deny("mutation_authority_unavailable");
  }
  public static PolicyDecision ValidateReference(CharmReference reference) {
    if(reference==null||!SafeId(reference.PackId)||!SafeId(reference.ExperienceId)||!SafeId(reference.BindingId))return PolicyDecision.Deny("charm_reference_id_invalid");
    if(!SemanticVersion.TryParse(reference.Version,out _))return PolicyDecision.Deny("charm_reference_version_invalid");
    if(reference.ContentHash==null||reference.ContentHash.Length!=64||!IsHex(reference.ContentHash))return PolicyDecision.Deny("charm_reference_hash_invalid");
    return PolicyDecision.Allow();
  }
  static bool SafeId(string value){if(string.IsNullOrWhiteSpace(value)||value.Length>64)return false;foreach(var ch in value)if(!char.IsLetterOrDigit(ch)&&ch!='-'&&ch!='_')return false;return true;}
  static bool IsHex(string value){foreach(var ch in value)if(!((ch>='0'&&ch<='9')||(ch>='a'&&ch<='f')||(ch>='A'&&ch<='F')))return false;return true;}
}
