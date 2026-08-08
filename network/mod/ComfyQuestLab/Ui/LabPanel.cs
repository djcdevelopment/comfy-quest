namespace ComfyQuestLab;

using System;
using System.Collections.Generic;

using ComfyNetworkSense;

using UnityEngine;

/// <summary>The live event console.
///
/// IMGUI, like every other panel in this project — no Jotunn dependency, no uGUI prefab
/// cloning, nothing to keep in sync with a game UI update.
///
/// Deliberately NOT anchored top-left: ComfyNetworkSense's debug HUD owns that corner
/// (HudRenderer draws a GUI.Box at the margin) and two overlapping overlays is how you
/// make a teaching tool look broken.
///
/// The filters are the feature. A single fight emits more rows than this window can
/// hold, and a builder who opens the console mid-combat and sees an unreadable blur
/// learns nothing. So it starts on combat + harvest only, and every category is one
/// click away.</summary>
public sealed class LabPanel {
  const int WindowId = 481922;   // 481620 = retired control surface, 481921 = NetworkSense
  const string FilterControlName = "questlab_filter";
  const float DefaultWidth = 900f;
  const float DefaultHeight = 620f;
  const float MinWidth = 700f;
  const float MinHeight = 440f;
  const float ResizeHandle = 20f;
  const float TimeColumn = 62f;
  const float SchoolColumn = 112f;
  const float EventColumn = 180f;
  const float UseColumn = 116f;
  const float QuestTriggerColumn = 220f;
  const float QuestStateColumn = 104f;
  const float QuestFiresColumn = 54f;
  const float QuestExpandColumn = 32f;
  const float MinPanelScale = 0.65f;
  const float MaxPanelScale = 2f;
  const float PanelScaleStep = 0.1f;

  readonly LabEventRing _ring;
  readonly HashSet<string> _visible = new HashSet<string>(LabCategory.DefaultVisible);

  Rect _window = new Rect(80f, 90f, DefaultWidth, DefaultHeight);
  Vector2 _scroll;
  Vector2 _journalScroll;
  Vector2 _questScroll;
  string _journalCategory = LabCategory.Harvest;   // where a new builder starts
  string _filterText = string.Empty;
  bool _paused;
  bool _showTrueNames;
  bool _showQuestFolder;
  bool _showQuestErrors;
  string _expandedQuestKey;
  bool _resizing;
  Vector2 _resizeStartMouse;
  Vector2 _resizeStartSize;
  float _requestedWidth = -1f;
  float _requestedHeight = -1f;
  float _drawScale = 1f;
  Texture2D _windowBackground;
  Texture2D _gridHeaderBackground;
  Texture2D _gridEvenBackground;
  Texture2D _gridOddBackground;
  Texture2D _resizeBackground;
  GUIStyle _windowStyle;
  GUIStyle _gridHeaderStyle;
  GUIStyle _gridEvenStyle;
  GUIStyle _gridOddStyle;
  GUIStyle _questDetailStyle;
  GUIStyle _resizeStyle;
  Tab _tab = Tab.Console;

  enum Tab { Console, Spellbook, Quests }

  /// <summary>One rune, used as a toggle. The same call renders a spellbook tab and a
  /// console filter, which is the whole reason the runes exist: learning the book
  /// teaches the console for free, because the mark is the same mark.</summary>
  static bool RuneToggle(bool on, string category, string label, float width) {
    Color previous = GUI.color;
    // A rune that is switched off is still legible, just quiet.
    GUI.color = on ? Color.white : new Color(1f, 1f, 1f, 0.68f);
    bool now = GUILayout.Toggle(on, new GUIContent(" " + label, LabRunes.For(category)),
        GUI.skin.button, GUILayout.Width(width), GUILayout.Height(24f));
    GUI.color = previous;
    return now;
  }

  public bool IsOpen { get; private set; }

  public LabPanel(LabEventRing ring) {
    _ring = ring;
  }

  public void Toggle() {
    if (IsOpen) {
      Close();
      return;
    }
    IsOpen = true;
    InputGuard.AcquirePanelInput();
  }

  public void Close() {
    IsOpen = false;
    _resizing = false;
    InputGuard.ReleasePanelInput();
  }

  public void Dispose() {
    Close();
    DestroyTexture(ref _windowBackground);
    DestroyTexture(ref _gridHeaderBackground);
    DestroyTexture(ref _gridEvenBackground);
    DestroyTexture(ref _gridOddBackground);
    DestroyTexture(ref _resizeBackground);
  }

  public void Draw() {
    if (!IsOpen) {
      InputGuard.TypingInLab = false;
      return;
    }
    InputGuard.MaintainPanelInput();
    EnsureStyles();

    _drawScale = CurrentPanelScale();
    Matrix4x4 oldMatrix = GUI.matrix;
    GUIStyle label = GUI.skin.label;
    int oldFontSize = label.fontSize;
    bool oldWordWrap = label.wordWrap;
    Color oldLabelColor = label.normal.textColor;
    Color oldContentColor = GUI.contentColor;
    try {
      GUI.matrix = Matrix4x4.Scale(new Vector3(_drawScale, _drawScale, 1f)) * oldMatrix;
      label.fontSize = 14;
      label.wordWrap = true;
      label.normal.textColor = new Color(0.94f, 0.96f, 1f, 1f);
      GUI.contentColor = Color.white;
      _window = GUILayout.Window(WindowId, ClampWindow(_window, _drawScale), DrawWindow, "Quest Lab",
          _windowStyle, GUILayout.MinWidth(MinWidth), GUILayout.MinHeight(MinHeight));
      if (_requestedWidth > 0f && _requestedHeight > 0f) {
        _window.width = _requestedWidth;
        _window.height = _requestedHeight;
      }
      _window = ClampWindow(_window, _drawScale);
    } finally {
      label.fontSize = oldFontSize;
      label.wordWrap = oldWordWrap;
      label.normal.textColor = oldLabelColor;
      GUI.contentColor = oldContentColor;
      GUI.matrix = oldMatrix;
    }
  }

  void DrawWindow(int id) {
    if (DrawTabs()) {
      return;
    }
    GUILayout.Space(4f);

    if (_tab == Tab.Console) {
      DrawConsole();
    } else if (_tab == Tab.Spellbook) {
      DrawSpellbook();
    } else {
      DrawQuestDashboard();
    }

    DrawResizeHandle();
    GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(0f, _window.width - ResizeHandle), 24f));
  }

  bool DrawTabs() {
    GUILayout.BeginHorizontal();
    if (GUILayout.Toggle(_tab == Tab.Console, "What just happened", GUI.skin.button)) {
      _tab = Tab.Console;
    }
    if (GUILayout.Toggle(_tab == Tab.Spellbook, "Spellbook", GUI.skin.button)) {
      _tab = Tab.Spellbook;
    }
    if (GUILayout.Toggle(_tab == Tab.Quests, "Quests", GUI.skin.button)) {
      _tab = Tab.Quests;
    }
    GUILayout.FlexibleSpace();
    GUILayout.Label(_ring.Count + " held · " + _ring.TotalSeen + " seen · mouse active");
    if (GUILayout.Button("-", GUILayout.Width(28f))) {
      SetPanelScale(_drawScale - PanelScaleStep);
    }
    GUILayout.Label(Mathf.RoundToInt(_drawScale * 100f) + "%", GUILayout.Width(44f));
    if (GUILayout.Button("+", GUILayout.Width(28f))) {
      SetPanelScale(_drawScale + PanelScaleStep);
    }
    if (GUILayout.Button("Close", GUILayout.Width(58f))) {
      GUILayout.EndHorizontal();
      Close();
      return true;
    }
    GUILayout.EndHorizontal();
    return false;
  }

  void DrawConsole() {
    // --- filters: the same runes as the spellbook, doing the filtering -------------
    for (int row = 0; row < 2; row++) {
      GUILayout.BeginHorizontal();
      for (int i = row * 4; i < (row + 1) * 4 && i < LabCategory.All.Length; i++) {
        string category = LabCategory.All[i];
        bool on = _visible.Contains(category);
        if (RuneToggle(on, category, LabJournal.For(category).Title, 138f) != on) {
          if (on) {
            _visible.Remove(category);
          } else {
            _visible.Add(category);
          }
        }
      }
      GUILayout.EndHorizontal();
    }

    GUILayout.BeginHorizontal();
    GUILayout.Label("match", GUILayout.Width(42f));

    // Naming the control is the only way to know IMGUI focus, and knowing it is what
    // stops every keystroke in this box from also being a game hotkey.
    GUI.SetNextControlName(FilterControlName);
    _filterText = GUILayout.TextField(_filterText ?? string.Empty, GUILayout.MinWidth(160f));
    InputGuard.TypingInLab = GUI.GetNameOfFocusedControl() == FilterControlName;

    if (GUILayout.Button(_paused ? "Resume" : "Pause", GUILayout.Width(70f))) {
      _paused = !_paused;
    }
    if (GUILayout.Button("Clear", GUILayout.Width(56f))) {
      _ring.Clear();
    }
    GUILayout.EndHorizontal();

    GUILayout.Space(3f);
    GUILayout.Label("BINDABLE enters the shared quest evaluator · DIAGNOSTIC is observation only");

    // --- spreadsheet ---------------------------------------------------------------
    DrawGridHeader();
    _scroll = GUILayout.BeginScrollView(_scroll);
    List<LabEvent> rows = _paused
        ? new List<LabEvent>()
        : _ring.Recent(_visible, _filterText, LabConfig.ConsoleRows.Value);

    if (_paused) {
      GUILayout.Label("Paused. Nothing is being dropped — resume to see what arrived.");
    } else if (rows.Count == 0) {
      GUILayout.Label(EmptyMessage());
    } else {
      for (int i = 0; i < rows.Count; i++) {
        DrawGridRow(rows[i], i);
      }
    }
    GUILayout.EndScrollView();
  }

  void DrawGridHeader() {
    GUILayout.BeginHorizontal();
    GridCell(new GUIContent("TIME"), _gridHeaderStyle, TimeColumn);
    GridCell(new GUIContent("SCHOOL"), _gridHeaderStyle, SchoolColumn);
    GridCell(new GUIContent("CREATOR EVENT"), _gridHeaderStyle, EventColumn);
    GridCell(new GUIContent("TARGET / DETAIL"), _gridHeaderStyle, -1f);
    GridCell(new GUIContent("QUEST USE"), _gridHeaderStyle, UseColumn);
    GUILayout.EndHorizontal();
  }

  void DrawGridRow(LabEvent row, int index) {
    GUIStyle style = index % 2 == 0 ? _gridEvenStyle : _gridOddStyle;
    string creatorName = string.IsNullOrWhiteSpace(row.EventName) ? row.Seam : row.EventName;
    string targetDetail = row.Target ?? string.Empty;
    if (!string.IsNullOrWhiteSpace(row.Detail)) {
      targetDetail += (targetDetail.Length == 0 ? string.Empty : " · ") + row.Detail;
    }

    GUILayout.BeginHorizontal();
    GridCell(new GUIContent(row.At ?? string.Empty), style, TimeColumn);

    Color prior = style.normal.textColor;
    Color priorHover = style.hover.textColor;
    Color schoolColor = LabRunes.ColorFor(row.Category);
    style.normal.textColor = schoolColor;
    style.hover.textColor = schoolColor;
    GridCell(new GUIContent(LabJournal.For(row.Category).Title,
        LabRunes.For(row.Category), row.Category), style, SchoolColumn);
    style.normal.textColor = prior;
    style.hover.textColor = priorHover;

    GridCell(new GUIContent(LabSpellNames.For(creatorName), row.Seam), style, EventColumn);
    GridCell(new GUIContent(targetDetail, targetDetail), style, -1f);

    Color usabilityColor = UsabilityColor(row.Usability);
    style.normal.textColor = usabilityColor;
    style.hover.textColor = usabilityColor;
    GridCell(new GUIContent(UsabilityLabel(row.Usability), UsabilityLine(row.Usability)),
        style, UseColumn);
    style.normal.textColor = prior;
    style.hover.textColor = priorHover;
    GUILayout.EndHorizontal();
  }

  static void GridCell(GUIContent content, GUIStyle style, float width) {
    if (width > 0f) {
      GUILayout.Label(content, style, GUILayout.Width(width), GUILayout.Height(28f));
    } else {
      GUILayout.Label(content, style, GUILayout.MinWidth(120f), GUILayout.ExpandWidth(true),
          GUILayout.Height(28f));
    }
  }

  static string UsabilityLabel(string usability) {
    switch (usability) {
      case LabUsability.Today:
        return "BINDABLE";
      case LabUsability.DiagnosticOnly:
        return "DIAGNOSTIC";
      default:
        return "NO TRIGGER";
    }
  }

  static Color UsabilityColor(string usability) {
    switch (usability) {
      case LabUsability.Today:
        return new Color(0.55f, 1f, 0.64f, 1f);
      case LabUsability.DiagnosticOnly:
        return new Color(1f, 0.78f, 0.38f, 1f);
      default:
        return new Color(0.72f, 0.76f, 0.82f, 1f);
    }
  }

  string EmptyMessage() {
    if (_visible.Count == 0) {
      return "No categories selected — turn one on above.";
    }
    if (!string.IsNullOrEmpty(_filterText)) {
      return "Nothing matches \"" + _filterText + "\" in the categories you have on.";
    }
    if (_ring.TotalSeen == 0) {
      return "Nothing yet. Punch a tree, or open the Spellbook and pick a rune to see what to try.";
    }
    return "Nothing under these runes. " + _ring.TotalSeen
        + " have fired overall — light another rune above.";
  }

  /// <summary>The honest answer to "can I build a quest on what I just saw?", spelled
  /// out rather than abbreviated. A builder should never have to look this up.</summary>
  static string UsabilityLine(string usability) {
    switch (usability) {
      case LabUsability.Today:
        return "-> a quest can be bound to this today";
      case LabUsability.ProducesEventNoTrigger:
        return "-> the world speaks, but no quest is listening yet";
      case LabUsability.DiagnosticOnly:
        return "-> diagnostic witness; intentionally not a quest trigger";
      default:
        return "-> nothing binds a quest to this yet";
    }
  }

  /// <summary>Creator-facing quest dashboard. Scan-critical facts remain in the grid;
  /// evaluator prose and source provenance stay one click behind each row.</summary>
  void DrawQuestDashboard() {
    LabQuestSet set = LabQuestEngine.Set;

    GUILayout.BeginHorizontal();
    GUILayout.Label(set.Quests.Count + " quests  /  " + set.ArmedCount + " armed");
    GUILayout.FlexibleSpace();
    _showQuestFolder = GUILayout.Toggle(_showQuestFolder, "Folder", GUI.skin.button,
        GUILayout.Width(68f));
    if (GUILayout.Button("Reload", GUILayout.Width(70f))) {
      ComfyQuestLab.Report(LabQuestEngine.Reload());
    }
    GUILayout.EndHorizontal();

    if (_showQuestFolder) {
      GUILayout.Label(LabQuestEngine.QuestDir);
    }

    string lastEvent = LabQuestEngine.LastEventLine;
    GUILayout.Label("matcher  /  "
        + (string.IsNullOrEmpty(lastEvent) ? "none yet" : lastEvent));

    if (!LabConfig.QuestsEnabled.Value) {
      Color previous = GUI.contentColor;
      GUI.contentColor = new Color(1f, 0.62f, 0.46f, 1f);
      GUILayout.Label("QUESTS OFF  /  files are loaded, but nothing will fire");
      GUI.contentColor = previous;
    }

    GUILayout.Space(4f);
    DrawQuestGridHeader();
    _questScroll = GUILayout.BeginScrollView(_questScroll);

    if (set.Quests.Count == 0 && set.Errors.Count == 0) {
      GUILayout.Label("No quests yet. Run lab_setup, or add a quest-view.json and reload.");
    }

    for (int i = 0; i < set.Quests.Count; i++) {
      DrawQuestGridRow(set.Quests[i], i);
    }

    if (set.Errors.Count > 0) {
      GUILayout.Space(8f);
      Color previous = GUI.contentColor;
      GUI.contentColor = new Color(1f, 0.62f, 0.46f, 1f);
      _showQuestErrors = GUILayout.Toggle(_showQuestErrors,
          (_showQuestErrors ? "-" : "+") + "  " + set.Errors.Count + " LOAD ERROR"
              + (set.Errors.Count == 1 ? string.Empty : "S"),
          GUI.skin.button);
      GUI.contentColor = previous;
      if (_showQuestErrors) {
        foreach (LabQuestFileError error in set.Errors) {
          GUILayout.BeginVertical(_questDetailStyle);
          GUILayout.Label(error.SourceFile);
          GUILayout.Label("contract  /  " + error.ContractMessage);
          GUILayout.Label("fix  /  " + error.Remedy);
          GUILayout.EndVertical();
        }
      }
    }

    GUILayout.EndScrollView();
  }

  void DrawQuestGridHeader() {
    GUILayout.BeginHorizontal();
    GridCell(new GUIContent("SCHOOL"), _gridHeaderStyle, SchoolColumn);
    GridCell(new GUIContent("QUEST"), _gridHeaderStyle, -1f);
    GridCell(new GUIContent("EVENT -> TARGET"), _gridHeaderStyle, QuestTriggerColumn);
    GridCell(new GUIContent("STATE"), _gridHeaderStyle, QuestStateColumn);
    GridCell(new GUIContent("FIRES"), _gridHeaderStyle, QuestFiresColumn);
    GridCell(new GUIContent(string.Empty, "expand details"), _gridHeaderStyle,
        QuestExpandColumn);
    GUILayout.EndHorizontal();
  }

  void DrawQuestGridRow(LabQuest quest, int index) {
    GUIStyle style = index % 2 == 0 ? _gridEvenStyle : _gridOddStyle;
    string key = QuestKey(quest);
    bool expanded = string.Equals(_expandedQuestKey, key, StringComparison.Ordinal);
    string category = QuestSchool(quest);
    string questName = quest.Quest == null || string.IsNullOrWhiteSpace(quest.Quest.Name)
        ? "unnamed quest"
        : quest.Quest.Name;
    string eventName = quest.Quest == null || string.IsNullOrWhiteSpace(quest.Quest.TriggerEvent)
        ? "manual"
        : quest.Quest.TriggerEvent;
    string target = quest.Quest == null || string.IsNullOrWhiteSpace(quest.Quest.TriggerTarget)
        ? "any"
        : quest.Quest.TriggerTarget;

    GUILayout.BeginHorizontal();
    Color prior = style.normal.textColor;
    Color priorHover = style.hover.textColor;
    Color schoolColor = LabRunes.ColorFor(category);
    style.normal.textColor = schoolColor;
    style.hover.textColor = schoolColor;
    GridCell(new GUIContent(LabJournal.For(category).Title, LabRunes.For(category), category),
        style, SchoolColumn);
    style.normal.textColor = prior;
    style.hover.textColor = priorHover;

    GridCell(new GUIContent(questName, quest.QuestId + "  /  " + quest.SourceFile),
        style, -1f);
    GridCell(new GUIContent(eventName + " -> " + target, "creator trigger"),
        style, QuestTriggerColumn);

    Color stateColor = QuestStateColor(quest.Armed);
    style.normal.textColor = stateColor;
    style.hover.textColor = stateColor;
    GridCell(new GUIContent(QuestStateLabel(quest.Armed), quest.ArmedLine()),
        style, QuestStateColumn);
    style.normal.textColor = prior;
    style.hover.textColor = priorHover;

    double cooldown = quest.IsArmed ? LabQuestEngine.CooldownRemaining(quest.QuestId) : 0.0;
    string fireTip = cooldown > 0.0
        ? "re-arms in " + Mathf.CeilToInt((float) cooldown) + "s"
        : "since the last reload";
    GridCell(new GUIContent(quest.IsArmed ? quest.Fires.ToString() : "-", fireTip),
        style, QuestFiresColumn);
    if (GUILayout.Button(expanded ? "-" : "+", GUILayout.Width(QuestExpandColumn),
        GUILayout.Height(28f))) {
      _expandedQuestKey = expanded ? null : key;
    }
    GUILayout.EndHorizontal();

    if (expanded) {
      DrawQuestDetails(quest, eventName, target, cooldown);
    }
  }

  void DrawQuestDetails(LabQuest quest, string eventName, string target, double cooldown) {
    GUILayout.BeginVertical(_questDetailStyle);
    GUILayout.Label("quest_id  /  " + (quest.QuestId ?? "none")
        + "    file  /  " + (quest.SourceFile ?? "unknown"));
    GUILayout.Label("trigger  /  " + eventName + " -> " + target);

    Color previous = GUI.contentColor;
    GUI.contentColor = QuestStateColor(quest.Armed);
    GUILayout.Label("verdict  /  " + quest.ArmedLine());
    GUI.contentColor = previous;

    if (quest.IsArmed) {
      GUILayout.Label("fires  /  " + quest.Fires
          + (cooldown > 0.0
              ? "    re-arms in " + Mathf.CeilToInt((float) cooldown) + "s"
              : "    ready"));
    }
    foreach (string note in quest.Advisories) {
      GUI.contentColor = new Color(1f, 0.78f, 0.38f, 1f);
      GUILayout.Label("note  /  " + note);
      GUI.contentColor = previous;
    }
    GUILayout.EndVertical();
  }

  static string QuestKey(LabQuest quest) {
    return (quest.SourceFile ?? string.Empty) + "\n" + (quest.QuestId ?? string.Empty);
  }

  static string QuestSchool(LabQuest quest) {
    if (quest == null || quest.Quest == null) {
      return LabCategory.Combat;
    }

    QuestEventCatalog.Definition definition;
    if (QuestEventCatalog.TryGet(quest.Quest.TriggerEvent, out definition)) {
      return definition.Category;
    }

    // Schema-1's "hit" alias spans combat and harvest. The legacy starter quest names
    // tree_or_bush, so preserve the school a creator expects instead of choosing whichever
    // canonical alias happens to appear first in a dictionary.
    string target = quest.Quest.TriggerTarget ?? string.Empty;
    if (target.IndexOf("tree", StringComparison.OrdinalIgnoreCase) >= 0
        || target.IndexOf("bush", StringComparison.OrdinalIgnoreCase) >= 0) {
      return LabCategory.Harvest;
    }

    foreach (string eventName in QuestEventCatalog.AllEventNames) {
      if (QuestEventCatalog.TriggerMatches(quest.Quest.TriggerEvent, eventName)
          && QuestEventCatalog.TryGet(eventName, out definition)) {
        return definition.Category;
      }
    }
    return LabCategory.Combat;
  }

  static string QuestStateLabel(string armed) {
    switch (armed) {
      case LabArmed.Yes: return "ARMED";
      case LabArmed.NoTrigger: return "MANUAL";
      case LabArmed.AutoChecked: return "EXTERNAL";
      case LabArmed.Irl: return "IRL";
      case LabArmed.UnsupportedEvent: return "UNBOUND";
      default: return "CHECK";
    }
  }

  static Color QuestStateColor(string armed) {
    switch (armed) {
      case LabArmed.Yes: return new Color(0.55f, 1f, 0.64f, 1f);
      case LabArmed.NoTrigger:
      case LabArmed.AutoChecked: return new Color(1f, 0.78f, 0.38f, 1f);
      case LabArmed.Irl: return new Color(0.72f, 0.76f, 0.82f, 1f);
      case LabArmed.UnsupportedEvent: return new Color(1f, 0.48f, 0.42f, 1f);
      default: return new Color(1f, 0.66f, 0.42f, 1f);
    }
  }

  /// <summary>The spellbook. Eight runes, each a school of things the world will answer
  /// to — what it covers, something to go and try, and the trap.
  ///
  /// Turning to a page also lights that rune in the console, because the next thing
  /// anyone does after reading "punch a tree" is punch a tree, and finding the row
  /// filtered out would be a silly way to lose them.</summary>
  void DrawSpellbook() {
    for (int row = 0; row < 2; row++) {
      GUILayout.BeginHorizontal();
      for (int i = row * 4; i < (row + 1) * 4 && i < LabCategory.All.Length; i++) {
        string category = LabCategory.All[i];
        bool selected = _journalCategory == category;
        if (RuneToggle(selected, category, LabJournal.For(category).Title, 138f) && !selected) {
          _journalCategory = category;
          _visible.Add(category);
        }
      }
      GUILayout.EndHorizontal();
    }
    GUILayout.Space(6f);

    LabJournal.Page current = LabJournal.For(_journalCategory);
    _journalScroll = GUILayout.BeginScrollView(_journalScroll);

    Color previous = GUI.color;
    GUI.color = LabRunes.ColorFor(current.Category);
    GUILayout.Label(current.Title);
    GUI.color = previous;
    GUILayout.Space(4f);
    foreach (string line in current.What) {
      GUILayout.Label(line);
    }

    GUILayout.Space(8f);
    GUILayout.Label("Go and try");
    foreach (string line in current.Try) {
      GUILayout.Label("  " + line);
    }

    GUILayout.Space(8f);
    GUILayout.Label("Worth knowing");
    foreach (string line in current.Watch) {
      GUILayout.Label("  " + line);
    }

    GUILayout.Space(8f);
    GUILayout.BeginHorizontal();
    GUILayout.Label("What the world answers to");
    GUILayout.FlexibleSpace();
    // You do not need a thing's true name to see it happen — only to command it. So the
    // method names are here, and they are not in your way until you ask.
    _showTrueNames = GUILayout.Toggle(_showTrueNames, "true names", GUI.skin.button,
        GUILayout.Width(96f));
    GUILayout.EndHorizontal();

    foreach (LabJournal.Spell spell in current.Spells) {
      Color before = GUI.color;
      if (!spell.Bound) {
        GUI.color = new Color(1f, 1f, 1f, 0.68f);
      }
      GUILayout.Label("  " + (spell.Bound ? "*" : "-") + "  " + spell.Name);
      GUILayout.Label("        " + spell.Verdict
          + (spell.Bound ? string.Empty : "  ·  intentionally not integrated"));
      if (_showTrueNames) {
        GUILayout.Label("        true name: " + spell.TrueName);
      }
      GUI.color = before;
    }

    GUILayout.Space(8f);
    GUILayout.Label("Integrated " + LabPatching.AppliedCount + " of "
        + LabPatching.Outcomes.Count + " seams this build reached for.");
    foreach (LabPatching.Outcome outcome in LabPatching.Outcomes) {
      if (!outcome.Applied) {
        GUILayout.Label("  unavailable: " + outcome.Label + " — " + outcome.Detail);
      }
    }

    GUILayout.EndScrollView();
  }

  void EnsureStyles() {
    if (_windowStyle != null) {
      return;
    }

    _windowBackground = SolidTexture("questlab-window", new Color(0.02f, 0.03f, 0.05f, 0.97f));
    _gridHeaderBackground = SolidTexture(
        "questlab-grid-header", new Color(0.10f, 0.16f, 0.24f, 1f));
    _gridEvenBackground = SolidTexture(
        "questlab-grid-even", new Color(0.035f, 0.05f, 0.075f, 0.97f));
    _gridOddBackground = SolidTexture(
        "questlab-grid-odd", new Color(0.065f, 0.085f, 0.12f, 0.97f));
    _resizeBackground = SolidTexture(
        "questlab-resize", new Color(0.22f, 0.34f, 0.48f, 1f));

    _windowStyle = new GUIStyle(GUI.skin.window);
    _windowStyle.normal.background = _windowBackground;
    _windowStyle.onNormal.background = _windowBackground;
    _windowStyle.normal.textColor = Color.white;
    _windowStyle.onNormal.textColor = Color.white;
    _windowStyle.border = new RectOffset(1, 1, 1, 1);
    _windowStyle.padding = new RectOffset(12, 12, 30, 12);
    _windowStyle.fontSize = 15;
    _windowStyle.fontStyle = FontStyle.Bold;

    _gridHeaderStyle = GridStyle(_gridHeaderBackground, Color.white, FontStyle.Bold);
    _gridEvenStyle = GridStyle(
        _gridEvenBackground, new Color(0.92f, 0.95f, 1f, 1f), FontStyle.Normal);
    _gridOddStyle = GridStyle(
        _gridOddBackground, new Color(0.92f, 0.95f, 1f, 1f), FontStyle.Normal);

    _questDetailStyle = GridStyle(
        _gridOddBackground, new Color(0.86f, 0.90f, 0.96f, 1f), FontStyle.Normal);
    _questDetailStyle.padding = new RectOffset(18, 10, 8, 8);
    _questDetailStyle.wordWrap = true;
    _questDetailStyle.clipping = TextClipping.Overflow;

    _resizeStyle = new GUIStyle(GUI.skin.box);
    _resizeStyle.normal.background = _resizeBackground;
    _resizeStyle.normal.textColor = Color.white;
    _resizeStyle.alignment = TextAnchor.MiddleCenter;
    _resizeStyle.fontSize = 15;
    _resizeStyle.fontStyle = FontStyle.Bold;
  }

  static GUIStyle GridStyle(Texture2D background, Color text, FontStyle weight) {
    var style = new GUIStyle(GUI.skin.label);
    style.normal.background = background;
    style.normal.textColor = text;
    style.hover.background = background;
    style.hover.textColor = text;
    style.padding = new RectOffset(6, 6, 4, 4);
    style.margin = new RectOffset(1, 1, 1, 1);
    style.alignment = TextAnchor.MiddleLeft;
    style.imagePosition = ImagePosition.ImageLeft;
    style.fontSize = 13;
    style.fontStyle = weight;
    style.wordWrap = false;
    style.clipping = TextClipping.Clip;
    return style;
  }

  static Texture2D SolidTexture(string name, Color color) {
    var texture = new Texture2D(1, 1, TextureFormat.ARGB32, false);
    texture.name = name;
    texture.hideFlags = HideFlags.HideAndDontSave;
    texture.SetPixel(0, 0, color);
    texture.Apply();
    return texture;
  }

  static void DestroyTexture(ref Texture2D texture) {
    if (texture != null) {
      UnityEngine.Object.Destroy(texture);
      texture = null;
    }
  }

  void DrawResizeHandle() {
    var handle = new Rect(
        Mathf.Max(0f, _window.width - ResizeHandle - 3f),
        Mathf.Max(0f, _window.height - ResizeHandle - 3f),
        ResizeHandle,
        ResizeHandle);
    GUI.Box(handle, "↘", _resizeStyle);

    Event current = Event.current;
    if (current.type == EventType.MouseDown && current.button == 0
        && handle.Contains(current.mousePosition)) {
      _resizing = true;
      _resizeStartMouse = GUIUtility.GUIToScreenPoint(current.mousePosition);
      _resizeStartSize = new Vector2(_window.width, _window.height);
      current.Use();
      return;
    }

    if (_resizing && current.type == EventType.MouseDrag) {
      Vector2 mouse = GUIUtility.GUIToScreenPoint(current.mousePosition);
      Vector2 delta = (mouse - _resizeStartMouse) / _drawScale;
      _requestedWidth = _resizeStartSize.x + delta.x;
      _requestedHeight = _resizeStartSize.y + delta.y;
      current.Use();
    }
    if (_resizing && current.rawType == EventType.MouseUp) {
      _resizing = false;
    }
  }

  static float CurrentPanelScale() {
    try {
      return Mathf.Clamp(LabConfig.PanelScale.Value, MinPanelScale, MaxPanelScale);
    } catch (Exception) {
      return 1f;
    }
  }

  void SetPanelScale(float scale) {
    scale = Mathf.Clamp(Mathf.Round(scale * 10f) / 10f, MinPanelScale, MaxPanelScale);
    try {
      LabConfig.PanelScale.Value = scale;
    } catch (Exception) {
    }
    _window = ClampWindow(_window, scale);
  }

  static Rect ClampWindow(Rect rect, float scale) {
    float logicalWidth = Screen.width / Mathf.Max(MinPanelScale, scale);
    float logicalHeight = Screen.height / Mathf.Max(MinPanelScale, scale);
    float maxWidth = Mathf.Max(360f, logicalWidth - 24f);
    float maxHeight = Mathf.Max(280f, logicalHeight - 24f);
    rect.width = Mathf.Clamp(rect.width, Mathf.Min(MinWidth, maxWidth), maxWidth);
    rect.height = Mathf.Clamp(rect.height, Mathf.Min(MinHeight, maxHeight), maxHeight);
    rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, logicalWidth - rect.width));
    rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, logicalHeight - rect.height));
    return rect;
  }
}
