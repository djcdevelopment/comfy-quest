namespace ComfyQuestLab;

using System.Collections.Generic;
using System.Reflection;

using HarmonyLib;
using UnityEngine;

/// <summary>
/// Explicit coverage for every practical atlas witness that is useful for diagnosis but is not
/// safe enough to bind to a quest. The diagnostic runtime profile makes these visible; the
/// generated capability catalog makes it impossible for this generic postfix to evaluate them.
/// Chat arguments are intentionally never inspected or logged.
/// </summary>
public static class DiagnosticPatches {
  public static void Apply(Harmony harmony) {
    TryAtlasPatch(harmony, typeof(WearNTear), "Remove", new[] { typeof(bool) },
        "WearNTear.Remove(bool)");
    TryAtlasPatch(harmony, typeof(WearNTear), "Repair", System.Type.EmptyTypes,
        "WearNTear.Repair()");

    TryAtlasPatch(harmony, typeof(Character), "AddStaggerDamage",
        new[] { typeof(float), typeof(Vector3), typeof(HitData) },
        "Character.AddStaggerDamage(float, Vector3, HitData)");
    TryAtlasPatch(harmony, typeof(Character), "BlockAttack",
        new[] { typeof(HitData), typeof(Character) },
        "Character.BlockAttack(HitData, Character)");
    TryAtlasPatch(harmony, typeof(Character), "SetHealth", new[] { typeof(float) },
        "Character.SetHealth(float)");

    TryAtlasPatch(harmony, typeof(InventoryGui), "OnCraftPressed", System.Type.EmptyTypes,
        "InventoryGui.OnCraftPressed()");

    TryAtlasPatch(harmony, typeof(Pickable), "RPC_SetPicked", new[] { typeof(long), typeof(bool) },
        "Pickable.RPC_SetPicked(long, bool)");
    TryAtlasPatch(harmony, typeof(Pickable), "SetPicked", new[] { typeof(bool) },
        "Pickable.SetPicked(bool)");

    TryAtlasPatch(harmony, typeof(Container), "RPC_RequestTakeAll",
        new[] { typeof(long), typeof(long) },
        "Container.RPC_RequestTakeAll(long, long)");
    TryAtlasPatch(harmony, typeof(Inventory), "AddItem",
        new[] { typeof(GameObject), typeof(int) },
        "Inventory.AddItem(GameObject, int)");
    TryAtlasPatch(harmony, typeof(Inventory), "AddItem",
        new[] { typeof(ItemDrop.ItemData) },
        "Inventory.AddItem(ItemData)");
    TryAtlasPatch(harmony, typeof(Inventory), "AddItem",
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(int), typeof(int) },
        "Inventory.AddItem(ItemData, int, int, int)");
    TryAtlasPatch(harmony, typeof(Inventory), "AddItem",
        new[] { typeof(ItemDrop.ItemData), typeof(Vector2i) },
        "Inventory.AddItem(ItemData, Vector2i)");
    TryAtlasPatch(harmony, typeof(Inventory), "AddItem",
        new[] {
          typeof(string), typeof(int), typeof(float), typeof(Vector2i), typeof(bool),
          typeof(int), typeof(int), typeof(long), typeof(string),
          typeof(Dictionary<string, string>), typeof(int), typeof(bool),
        },
        "Inventory.AddItem(string, int, float, Vector2i, bool, int, int, long, string, Dictionary`2, int, bool)");
    TryAtlasPatch(harmony, typeof(Inventory), "AddItem",
        new[] {
          typeof(string), typeof(int), typeof(int), typeof(int), typeof(long),
          typeof(string), typeof(bool),
        },
        "Inventory.AddItem(string, int, int, int, long, string, bool)");
    TryAtlasPatch(harmony, typeof(Inventory), "AddItem",
        new[] {
          typeof(string), typeof(int), typeof(int), typeof(int), typeof(long),
          typeof(string), typeof(Vector2i), typeof(bool),
        },
        "Inventory.AddItem(string, int, int, int, long, string, Vector2i, bool)");
    TryAtlasPatch(harmony, typeof(Inventory), "RemoveItem", new[] { typeof(int) },
        "Inventory.RemoveItem(int)");
    TryAtlasPatch(harmony, typeof(Inventory), "RemoveItem", new[] { typeof(ItemDrop.ItemData) },
        "Inventory.RemoveItem(ItemData)");
    TryAtlasPatch(harmony, typeof(Inventory), "RemoveItem",
        new[] { typeof(ItemDrop.ItemData), typeof(int) },
        "Inventory.RemoveItem(ItemData, int)");
    TryAtlasPatch(harmony, typeof(Inventory), "RemoveItem",
        new[] { typeof(string), typeof(int), typeof(int), typeof(bool) },
        "Inventory.RemoveItem(string, int, int, bool)");
    TryAtlasPatch(harmony, typeof(ItemDrop), "DropItem",
        new[] { typeof(ItemDrop.ItemData), typeof(int), typeof(Vector3), typeof(Quaternion) },
        "ItemDrop.DropItem(ItemData, int, Vector3, Quaternion)");
    TryAtlasPatch(harmony, typeof(ItemDrop), "Pickup", new[] { typeof(Humanoid) },
        "ItemDrop.Pickup(Humanoid)");
    TryAtlasPatch(harmony, typeof(ItemDrop), "RPC_RequestOwn", new[] { typeof(long) },
        "ItemDrop.RPC_RequestOwn(long)");

    TryAtlasPatch(harmony, typeof(Skills), "OnDeath", System.Type.EmptyTypes,
        "Skills.OnDeath()");

    TryAtlasPatch(harmony, typeof(Chat), "OnNewChatMessage",
        new[] {
          typeof(GameObject), typeof(long), typeof(Vector3), typeof(Talker.Type),
          typeof(UserInfo), typeof(string),
        },
        "Chat.OnNewChatMessage(GameObject, long, Vector3, Type, UserInfo, string)");
    TryAtlasPatch(harmony, typeof(Chat), "RPC_ChatMessage",
        new[] { typeof(long), typeof(Vector3), typeof(int), typeof(UserInfo), typeof(string) },
        "Chat.RPC_ChatMessage(long, Vector3, int, UserInfo, string)");
    TryAtlasPatch(harmony, typeof(Talker), "Say", new[] { typeof(Talker.Type), typeof(string) },
        "Talker.Say(Type, string)");
  }

  static void TryAtlasPatch(
      Harmony harmony,
      System.Type declaringType,
      string methodName,
      System.Type[] argumentTypes,
      string signatureId) {
    LabPatching.TryPatch(
        harmony, declaringType, methodName, argumentTypes,
        nameof(DiagnosticPostfix), signatureId);
  }

  static void DiagnosticPostfix(MethodBase __originalMethod, object __instance) {
    string signatureId = LabPatching.SignatureFor(__originalMethod);
    UnityEngine.Object subject = __instance as UnityEngine.Object;
    string target = subject == null
        ? (__originalMethod == null ? "unknown" : __originalMethod.DeclaringType.Name)
        : LabObserve.Clean(subject.name);
    LabObserve.Seam(
        signatureId, target, "diagnostic witness; never bindable", subject,
        evaluate: false);
  }
}
