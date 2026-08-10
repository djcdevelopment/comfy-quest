namespace ComfyQuestRuntime;

using System;
using System.Collections.Generic;
using HarmonyLib;
using ComfyQuestContracts;

[HarmonyPatch]
static class RuntimeKillPatches {
  public static RuntimeExperienceEngine Engine;static readonly Dictionary<int,double> hits=new();const double FreshSeconds=15;
  [HarmonyPatch(typeof(Character),"Damage",typeof(HitData)),HarmonyPostfix]static void Damage(Character __instance,HitData __0)=>Record(__instance,__0);
  [HarmonyPatch(typeof(Character),"RPC_Damage",typeof(long),typeof(HitData)),HarmonyPostfix]static void RpcDamage(Character __instance,HitData __1)=>Record(__instance,__1);
  [HarmonyPatch(typeof(Character),"OnDeath"),HarmonyPostfix]static void Death(Character __instance){try{if(__instance==null||__instance.IsPlayer())return;var id=__instance.GetInstanceID();if(!hits.TryGetValue(id,out var at))return;hits.Remove(id);if(UnityEngine.Time.realtimeSinceStartup-at>FreshSeconds)return;Engine?.OnEvent(new RuntimeEvent{Name="kill",Target=__instance.m_name,At=DateTimeOffset.UtcNow});}catch{}}
  [HarmonyPatch(typeof(WearNTear),"ApplyDamage",typeof(float),typeof(HitData)),HarmonyPostfix]static void PieceDamage(WearNTear __instance,float __0,HitData __1,bool __result){try{if(!__result||__0<=0f||__instance==null||__1==null||__1.GetAttacker()!=Player.m_localPlayer)return;var zdo=__instance.GetComponent<ZNetView>()?.GetZDO();if(zdo==null)return;Engine?.OnEvent(new RuntimeEvent{Name="piece_damaged",Target=__instance.name,SourceId=zdo.m_uid.ToString(),At=DateTimeOffset.UtcNow});}catch{}}
  [HarmonyPatch(typeof(Sign),"SetText",typeof(string)),HarmonyPostfix]static void SignWritten(Sign __instance,string __0){try{if(__instance==null||string.IsNullOrWhiteSpace(__0)||Player.m_localPlayer==null)return;var zdo=__instance.GetComponent<ZNetView>()?.GetZDO();if(zdo==null)return;Engine?.OnEvent(new RuntimeEvent{Name="sign_written",Target=__instance.name,SourceId=zdo.m_uid.ToString(),At=DateTimeOffset.UtcNow});}catch{}}
  static void Record(Character victim,HitData hit){try{if(victim==null||hit==null||hit.GetAttacker()!=Player.m_localPlayer)return;if(hits.Count>=256)hits.Clear();hits[victim.GetInstanceID()]=UnityEngine.Time.realtimeSinceStartup;}catch{}}
}
