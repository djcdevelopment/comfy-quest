namespace ComfyQuestContracts;

using System;
using System.Collections.Generic;

/// <summary>Small reviewed v1 mutation surface. Runtime still verifies the live prefab component.</summary>
public static class MutationRegistry {
  static readonly Dictionary<string,int> GrantItems=new(StringComparer.Ordinal){["Wood"]=50,["Stone"]=50,["Resin"]=50,["Coins"]=100};
  static readonly HashSet<string> Creatures=new(StringComparer.Ordinal){"Greyling","Boar"};
  static readonly HashSet<string> SpawnItems=new(StringComparer.Ordinal){"Wood","Stone","Resin"};
  static readonly HashSet<string> Pieces=new(StringComparer.Ordinal){"sign","wood_floor"};
  public static bool TryGrant(string prefab,int quantity,out string diagnostic){diagnostic=null;if(!GrantItems.TryGetValue(prefab??"",out var max)){diagnostic="grant_item_not_allowlisted";return false;}if(quantity<1||quantity>max){diagnostic="grant_quantity_exceeds_stack_cap";return false;}return true;}
  public static bool CanSpawn(string kind,string prefab){if(kind=="creature")return Creatures.Contains(prefab??"");if(kind=="item")return SpawnItems.Contains(prefab??"");if(kind=="piece")return Pieces.Contains(prefab??"");return false;}
}
