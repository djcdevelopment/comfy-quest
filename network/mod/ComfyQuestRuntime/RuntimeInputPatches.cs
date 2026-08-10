namespace ComfyQuestRuntime;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch]
static class RuntimeInputPatches {
  static bool owns; static CursorLockMode priorLock; static bool priorVisible;
  public static void Acquire(){if(!owns){priorLock=Cursor.lockState;priorVisible=Cursor.visible;owns=true;try{ZInput.ResetAllButtonStates();}catch{}}Maintain();}
  public static void Maintain(){if(!owns)return;Cursor.lockState=CursorLockMode.None;Cursor.visible=true;}
  public static void Release(){if(!owns)return;owns=false;try{ZInput.ResetAllButtonStates();}catch{}Cursor.lockState=priorLock;Cursor.visible=priorVisible;}
  [HarmonyPatch(typeof(GameCamera),"UpdateMouseCapture"),HarmonyPostfix] static void MouseCapture()=>Maintain();
  [HarmonyPatch(typeof(Character),"TakeInput"),HarmonyPostfix] static void TakeInput(Character __instance,ref bool __result){if(owns&&__instance==Player.m_localPlayer)__result=false;}
}
