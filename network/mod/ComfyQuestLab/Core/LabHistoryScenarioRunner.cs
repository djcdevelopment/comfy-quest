namespace ComfyQuestLab;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using HarmonyLib;
using UnityEngine;

/// <summary>Bounded cumulative mutation lane for Steward validation corpora.
/// The request contains only corpus/step/seed; scenario geometry and prefab choices
/// are source-controlled here so the mailbox cannot become an arbitrary world editor.</summary>
public sealed class LabHistoryScenarioRunner {
  const string Mark = "comfyQuestLabHistory";
  const string CorpusMark = "comfyQuestLabHistoryCorpus";
  const string StepMark = "comfyQuestLabHistoryStep";
  const int MaxPlacedPerStep = 256;
  static readonly AccessTools.FieldRef<ZDOMan, Dictionary<ZDOID, ZDO>> Objects =
      AccessTools.FieldRefAccess<ZDOMan, Dictionary<ZDOID, ZDO>>("m_objectsByID");
  bool _running;

  public bool IsRunning { get { return _running; } }

  static string Root(string corpus) {
    return Path.Combine(BepInEx.Paths.ConfigPath, "comfy-quest-lab",
        Path.Combine("history", corpus));
  }

  public IEnumerator Run(MonoBehaviour host, string corpus, int step, int seed,
      int expectedPreviousStep, Action<string> completed) {
    if (_running) { completed("history_busy"); yield break; }
    if (host == null) { completed("host_missing"); yield break; }
    if (string.IsNullOrWhiteSpace(corpus) || corpus.Length > 80 || step < 1 || step > 5
        || expectedPreviousStep != step - 1) {
      completed("history_arguments_invalid"); yield break;
    }
    _running = true;
    int added = 0, removed = 0, skipped = 0;
    string failure = null;
    string ledger = Path.Combine(Root(corpus), "ledgers");
    try {
      float readyDeadline = Time.realtimeSinceStartup + 180f;
      while ((ZNetScene.instance == null || ZDOMan.instance == null)
          && Time.realtimeSinceStartup < readyDeadline) {
        yield return null;
      }
      if (ZNetScene.instance == null || ZDOMan.instance == null) {
        completed("world_ready_timeout");
        yield break;
      }
      Directory.CreateDirectory(Path.Combine(Root(corpus), "receipts"));
      Directory.CreateDirectory(ledger);
      string ledgerPath = Path.Combine(ledger, "step-" + step.ToString("00", CultureInfo.InvariantCulture) + ".jsonl");
      using (var writer = new StreamWriter(ledgerPath, false, Encoding.UTF8)) {
        Vector3 origin = Player.m_localPlayer == null
            ? Vector3.zero
            : Player.m_localPlayer.transform.position;
        List<Vector3> clusters = Clusters(step, origin);
        int target = step == 1 ? 32 : (step == 2 ? 96 : (step == 3 ? 64 : (step == 4 ? 128 : 256)));
        for (int i = 0; i < target && added < MaxPlacedPerStep; i++) {
          Vector3 c = clusters[i % clusters.Count];
          int ring = i / clusters.Count;
          Vector3 position = c + new Vector3((i % 8) * 2.2f, 0.25f, ring * 2.2f);
          string prefab = (i % 2 == 0) ? "wood_wall" : "wood_floor";
          GameObject go = Place(prefab, position, Quaternion.Euler(0f, (i % 4) * 90f, 0f), corpus, step, seed, i);
          if (go == null) { skipped++; continue; }
          ZNetView view = go.GetComponent<ZNetView>();
          ZDO zdo = view == null ? null : view.GetZDO();
          WriteLedger(writer, "added", zdo, prefab, position, "BUILDING", corpus, step, seed, i);
          added++;
          if (i % 8 == 7) yield return null;
        }
        int removalBudget = step == 3 ? 160 : (step == 5 ? 256 : (step == 4 ? 96 : 0));
        if (removalBudget > 0) {
          foreach (ZDO zdo in SnapshotObjects()) {
            if (removed >= removalBudget || zdo == null || !InEventGeometry(zdo.GetPosition(), origin, step)) continue;
            if (IsHistory(zdo, corpus) || IsNatural(zdo)) {
              if (Destroy(zdo)) {
                WriteLedger(writer, "removed", zdo, PrefabName(zdo), zdo.GetPosition(),
                    IsNatural(zdo) ? "RESOURCE" : "BUILDING", corpus, step, seed, removed);
                removed++;
              }
            }
            if (removed % 8 == 7) yield return null;
          }
        }
      }
      string receipt = "{\"schema\":\"comfy-quest-lab-history-receipt/v1\",\"corpusId\":\""
          + Json(corpus) + "\",\"step\":" + step.ToString(CultureInfo.InvariantCulture)
          + ",\"seed\":" + seed.ToString(CultureInfo.InvariantCulture)
          + ",\"expectedPreviousStep\":" + expectedPreviousStep.ToString(CultureInfo.InvariantCulture)
          + ",\"applied\":{\"added\":" + added.ToString(CultureInfo.InvariantCulture)
          + ",\"removed\":" + removed.ToString(CultureInfo.InvariantCulture)
          + ",\"skipped\":" + skipped.ToString(CultureInfo.InvariantCulture)
          + "},\"ledgerFile\":\"" + Json(Path.Combine(ledger, "step-" + step.ToString("00") + ".jsonl"))
          + "\",\"completedUtc\":\"" + DateTime.UtcNow.ToString("o") + "\"}";
      File.WriteAllText(Path.Combine(Root(corpus), "receipts", "step-" + step.ToString("00") + ".json"), receipt);
      if (ZNet.instance == null) {
        throw new InvalidOperationException("world_save_unavailable");
      }
      // The lab server's supervisor has a bounded shutdown timeout that is shorter than
      // this world's save. Commit synchronously before publishing the completed request.
      ZNet.instance.Save(true, false, false);
      failure = "completed";
    } finally { _running = false; }
    completed(failure);
  }

  static List<Vector3> Clusters(int step, Vector3 o) {
    var c = new List<Vector3> { o + new Vector3(0f, 0f, 0f) };
    if (step >= 2) { c.Add(o + new Vector3(320f, 0f, 160f)); c.Add(o + new Vector3(-320f, 0f, 160f)); c.Add(o + new Vector3(0f, 0f, -320f)); }
    if (step >= 5) { c.Add(o + new Vector3(640f, 0f, 320f)); c.Add(o + new Vector3(-640f, 0f, 320f)); }
    return c;
  }

  static bool InEventGeometry(Vector3 p, Vector3 o, int step) {
    if (step == 4) return Vector2.Distance(new Vector2(p.x, p.z), new Vector2(o.x, o.z)) <= 55f;
    if (step == 3 || step == 5) return Math.Abs((p.x - o.x) - (p.z - o.z)) < 28f && Math.Abs(p.x - o.x) < 520f;
    return false;
  }

  static bool IsNatural(ZDO zdo) {
    string n = PrefabName(zdo).ToLowerInvariant();
    return n.Contains("birch") || n.Contains("oak") || n.Contains("rock") || n.Contains("ore") || n.Contains("stone");
  }

  static bool IsHistory(ZDO zdo, string corpus) {
    return zdo.GetString(Mark, string.Empty).Length > 0
        && string.Equals(zdo.GetString(CorpusMark, string.Empty), corpus, StringComparison.Ordinal);
  }

  static string PrefabName(ZDO zdo) {
    try { return ZNetScene.instance.GetPrefab(zdo.GetPrefab()).name; } catch { return zdo.GetPrefab().ToString(CultureInfo.InvariantCulture); }
  }

  static IEnumerable<ZDO> SnapshotObjects() {
    if (ZDOMan.instance == null || Objects == null) yield break;
    foreach (ZDO zdo in new List<ZDO>(Objects(ZDOMan.instance).Values)) yield return zdo;
  }

  static GameObject Place(string prefabName, Vector3 p, Quaternion r, string corpus, int step, int seed, int ordinal) {
    GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
    if (prefab == null) return null;
    GameObject go = UnityEngine.Object.Instantiate(prefab, p, r);
    ZNetView view = go.GetComponent<ZNetView>();
    ZDO zdo = view == null ? null : view.GetZDO();
    if (zdo == null) return go;
    zdo.Set(Mark, "1"); zdo.Set(CorpusMark, corpus); zdo.Set(StepMark, step.ToString(CultureInfo.InvariantCulture));
    Piece piece = go.GetComponent<Piece>();
    if (piece != null && Player.m_localPlayer != null) piece.SetCreator(Player.m_localPlayer.GetPlayerID());
    return go;
  }

  static bool Destroy(ZDO zdo) {
    if (zdo == null || ZDOMan.instance == null) return false;
    ZNetView view = ZNetScene.instance.FindInstance(zdo);
    if (view != null) { view.ClaimOwnership(); view.Destroy(); return true; }
    zdo.SetOwner(ZDOMan.GetSessionID()); ZDOMan.instance.DestroyZDO(zdo); return true;
  }

  static void WriteLedger(StreamWriter w, string action, ZDO zdo, string prefab, Vector3 p,
      string category, string corpus, int step, int seed, int ordinal) {
    if (zdo == null) return;
    w.WriteLine("{\"action\":\"" + action + "\",\"corpusId\":\"" + Json(corpus)
        + "\",\"step\":" + step + ",\"seed\":" + seed + ",\"ordinal\":" + ordinal
        + ",\"prefab\":\"" + Json(prefab) + "\",\"category\":\"" + category
        + "\",\"x\":" + p.x.ToString("R", CultureInfo.InvariantCulture)
        + ",\"y\":" + p.y.ToString("R", CultureInfo.InvariantCulture)
        + ",\"z\":" + p.z.ToString("R", CultureInfo.InvariantCulture)
        + ",\"zdoid\":\"" + Json(zdo.m_uid.ToString()) + "\"}");
  }

  static string Json(string value) {
    return (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"");
  }
}
