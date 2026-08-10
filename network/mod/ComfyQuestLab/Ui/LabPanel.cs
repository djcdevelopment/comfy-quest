namespace ComfyQuestLab;

using System;
using System.Collections.Generic;
using System.IO;

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
  const float DefaultWidth = LabPanelLayout.DefaultWidth;
  const float DefaultHeight = LabPanelLayout.DefaultHeight;
  const float MinWidth = LabPanelLayout.PreferredMinimumWidth;
  const float MinHeight = LabPanelLayout.PreferredMinimumHeight;
  const float ResizeHandle = 20f;
  const float TimeColumn = 62f;
  const float SchoolColumn = 112f;
  const float EventColumn = 180f;
  const float UseColumn = 116f;
  const float QuestTriggerColumn = 220f;
  const float QuestStateColumn = 104f;
  const float QuestFiresColumn = 54f;
  const float QuestExpandColumn = 44f;
  const float SpellUseColumn = 190f;
  const float SpellTrueNameColumn = 220f;
  const float MinPanelScale = LabPanelLayout.MinimumScale;
  const float MaxPanelScale = LabPanelLayout.MaximumScale;
  const float ScenarioEventColumn = 210f;
  const float ScenarioProfileColumn = 82f;
  const float ScenarioSelectColumn = 70f;
  const float PanelScaleStep = 0.1f;
  // Deliberately not configurable: this is a local handoff, never a remote-control or
  // arbitrary-URL surface. The companion binds this exact IPv4 loopback address only.
  const string SheetsExporterUrl = "http://127.0.0.1:47631/";

  readonly LabEventRing _ring;
  readonly HashSet<string> _visible = new HashSet<string>(LabCategory.DefaultVisible);
  readonly List<LabEvent> _pausedRows = new List<LabEvent>();

  Rect _window = new Rect(80f, 90f, DefaultWidth, DefaultHeight);
  Vector2 _scroll;
  Vector2 _journalScroll;
  Vector2 _questScroll;
  Vector2 _readinessScroll;
  Vector2 _scenarioScroll;
  string _journalCategory = LabCategory.Harvest;   // where a new builder starts
  string _scenarioCategory = LabCategory.Harvest;
  string _selectedScenarioId = "scenario-resource_damaged";
  string _filterText = string.Empty;
  string _notice = string.Empty;
  float _noticeUntil;
  bool _paused;
  bool _showTrueNames;
  bool _showQuestFolder;
  bool _showQuestErrors;
  bool _showKeyboardHelp;
  bool _focusFilter;
  string _expandedQuestKey;
  bool _resizing;
  Vector2 _resizeStartMouse;
  Vector2 _resizeStartSize;
  float _requestedWidth = -1f;
  float _requestedHeight = -1f;
  float _drawScale = 1f;
  bool _saveLayoutAfterDraw;
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
  GUIStyle _statusStyle;
  GUIStyle _sectionHeaderStyle;
  GUIStyle _helpStyle;
  GUIStyle _resizeStyle;
  Tab _tab = Tab.Console;

  enum Tab { Console, Spellbook, Scenarios, Quests, Readiness }

  /// <summary>One rune, used as a toggle. The same call renders a spellbook tab and a
  /// console filter, which is the whole reason the runes exist: learning the book
  /// teaches the console for free, because the mark is the same mark.</summary>
  static bool RuneToggle(bool on, string category, string label, float width) {
    Color previous = GUI.color;
    // A rune that is switched off is still legible, just quiet.
    GUI.color = on ? Color.white : new Color(1f, 1f, 1f, 0.68f);
    bool now = GUILayout.Toggle(on, new GUIContent(" " + label, LabRunes.For(category),
        label + " school"),
        GUI.skin.button, GUILayout.Width(width), GUILayout.Height(24f));
    GUI.color = previous;
    return now;
  }

  public bool IsOpen { get; private set; }

  public LabPanel(LabEventRing ring) {
    _ring = ring;
    _window = SavedWindow();
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
    SaveWindow();
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
      Rect clamped = ClampWindow(_window, _drawScale);
      LabPanelBounds layout = LayoutBounds(clamped, _drawScale);
      _window = GUILayout.Window(WindowId, clamped, DrawWindow, "Quest Lab",
          _windowStyle,
          GUILayout.MinWidth(layout.MinimumWidth),
          GUILayout.MinHeight(layout.MinimumHeight));
      if (_requestedWidth > 0f && _requestedHeight > 0f) {
        _window.width = _requestedWidth;
        _window.height = _requestedHeight;
      }
      _window = ClampWindow(_window, _drawScale);
      if (_saveLayoutAfterDraw) {
        SaveWindow();
        _saveLayoutAfterDraw = false;
      }
    } finally {
      label.fontSize = oldFontSize;
      label.wordWrap = oldWordWrap;
      label.normal.textColor = oldLabelColor;
      GUI.contentColor = oldContentColor;
      GUI.matrix = oldMatrix;
    }
  }

  void DrawWindow(int id) {
    HandleKeyboardNavigation();
    if (DrawTabs()) {
      return;
    }
    if (_showKeyboardHelp) {
      DrawKeyboardHelp();
    }
    GUILayout.Space(4f);

    if (_tab == Tab.Console) {
      DrawConsole();
    } else if (_tab == Tab.Spellbook) {
      DrawSpellbook();
    } else if (_tab == Tab.Scenarios) {
      DrawScenarioCockpit();
    } else if (_tab == Tab.Quests) {
      DrawQuestDashboard();
    } else {
      DrawReadiness();
    }

    DrawHelpBar();
    DrawResizeHandle();
    GUI.DragWindow(new Rect(0f, 0f, Mathf.Max(0f, _window.width - ResizeHandle), 24f));
  }

  bool DrawTabs() {
    GUILayout.BeginHorizontal();
    if (GUILayout.Toggle(_tab == Tab.Console,
        new GUIContent("What just happened",
            "live creator events and quest usability; Ctrl+1"),
        GUI.skin.button)) {
      _tab = Tab.Console;
    }
    if (GUILayout.Toggle(_tab == Tab.Spellbook,
        new GUIContent("Spellbook", "browse the eight event schools; Ctrl+2"),
        GUI.skin.button)) {
      _tab = Tab.Spellbook;
    }
    if (GUILayout.Toggle(_tab == Tab.Scenarios,
        new GUIContent("Scenarios", "prepare and rehearse one canonical event; Ctrl+3"),
        GUI.skin.button)) {
      _tab = Tab.Scenarios;
    }
    if (GUILayout.Toggle(_tab == Tab.Quests,
        new GUIContent("Quests", "loaded quest files and evaluator state; Ctrl+4"),
        GUI.skin.button)) {
      _tab = Tab.Quests;
    }
    if (GUILayout.Toggle(_tab == Tab.Readiness,
        new GUIContent("Ready?", "runtime-backed demo readiness; Ctrl+5"), GUI.skin.button)) {
      _tab = Tab.Readiness;
    }
    GUILayout.FlexibleSpace();
    GUILayout.Label(new GUIContent("[" + InputGuard.OwnershipMode.ToUpperInvariant() + "]",
        "input ownership: interactive means pointer owned and player actions blocked"),
        GUILayout.Width(108f));
    GUILayout.Label(new GUIContent(_ring.Count + " held · " + _ring.TotalSeen + " seen",
        "events currently retained / events observed this session"));
    if (GUILayout.Button(new GUIContent("-", "zoom out"), GUILayout.Width(28f))) {
      SetPanelScale(_drawScale - PanelScaleStep);
    }
    if (GUILayout.Button(new GUIContent(Mathf.RoundToInt(_drawScale * 100f) + "%",
        "reset zoom to 100%"), GUILayout.Width(48f))) {
      SetPanelScale(1f);
    }
    if (GUILayout.Button(new GUIContent("+", "zoom in"), GUILayout.Width(28f))) {
      SetPanelScale(_drawScale + PanelScaleStep);
    }
    if (GUILayout.Button(new GUIContent("Keys", "show keyboard shortcuts and state legend"),
        GUILayout.Width(44f))) {
      _showKeyboardHelp = !_showKeyboardHelp;
    }
    if (GUILayout.Button(new GUIContent("Close", "F6 or Escape"), GUILayout.Width(58f))) {
      GUILayout.EndHorizontal();
      Close();
      return true;
    }
    GUILayout.EndHorizontal();
    return false;
  }

  void HandleKeyboardNavigation() {
    Event current = Event.current;
    if (current == null || current.type != EventType.KeyDown) {
      return;
    }
    if (current.keyCode == KeyCode.F1) {
      _showKeyboardHelp = !_showKeyboardHelp;
      current.Use();
      return;
    }
    if (!current.control) {
      return;
    }

    if (current.keyCode == KeyCode.Alpha1) {
      SelectTab(Tab.Console);
    } else if (current.keyCode == KeyCode.Alpha2) {
      SelectTab(Tab.Spellbook);
    } else if (current.keyCode == KeyCode.Alpha3) {
      SelectTab(Tab.Scenarios);
    } else if (current.keyCode == KeyCode.Alpha4) {
      SelectTab(Tab.Quests);
    } else if (current.keyCode == KeyCode.Alpha5) {
      SelectTab(Tab.Readiness);
    } else if (current.keyCode == KeyCode.Tab) {
      int count = Enum.GetValues(typeof(Tab)).Length;
      int delta = current.shift ? -1 : 1;
      SelectTab((Tab) (((int) _tab + delta + count) % count));
    } else if (current.keyCode == KeyCode.F) {
      SelectTab(Tab.Console);
      _focusFilter = true;
    } else if (current.keyCode == KeyCode.Alpha0) {
      SetPanelScale(1f);
      ShowNotice("ZOOM RESET  /  100%");
    } else {
      return;
    }
    current.Use();
  }

  void SelectTab(Tab tab) {
    _tab = tab;
    GUI.FocusControl(null);
    InputGuard.TypingInLab = false;
  }

  void DrawKeyboardHelp() {
    GUILayout.BeginVertical(_questDetailStyle);
    GUILayout.BeginHorizontal();
    GUILayout.Label("KEYBOARD  /  Ctrl+1..5 tabs  /  Ctrl+Tab cycle  /  Ctrl+F find  /  "
        + "Ctrl+0 zoom  /  F1 help  /  F6 or Esc close");
    if (GUILayout.Button(new GUIContent("Reset layout",
        "restore 100% zoom and a visible default window"), GUILayout.Width(96f))) {
      ResetPanelLayout();
    }
    if (GUILayout.Button("Hide", GUILayout.Width(46f))) {
      _showKeyboardHelp = false;
    }
    GUILayout.EndHorizontal();
    GUILayout.Label("STATE WORDS  /  [OK] usable  /  [INFO] observation only  /  "
        + "[CHECK] action needed  /  [PROVED] machine receipt passed");
    GUILayout.EndVertical();
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
    GUILayout.Label("find", GUILayout.Width(34f));

    // Naming the control is the only way to know IMGUI focus, and knowing it is what
    // stops every keystroke in this box from also being a game hotkey.
    GUI.SetNextControlName(FilterControlName);
    _filterText = GUILayout.TextField(_filterText ?? string.Empty, GUILayout.MinWidth(160f));
    if (_focusFilter) {
      GUI.FocusControl(FilterControlName);
      _focusFilter = false;
    }
    InputGuard.TypingInLab = GUI.GetNameOfFocusedControl() == FilterControlName;

    bool controlsEnabled = GUI.enabled;
    GUI.enabled = controlsEnabled && !string.IsNullOrEmpty(_filterText);
    if (GUILayout.Button(new GUIContent("×", "clear the search"), GUILayout.Width(28f))) {
      _filterText = string.Empty;
      GUI.FocusControl(null);
      InputGuard.TypingInLab = false;
    }
    GUI.enabled = controlsEnabled;

    if (GUILayout.Button(new GUIContent("All", "show all eight event schools"),
        GUILayout.Width(42f))) {
      SetVisibleSchools(LabCategory.All);
    }
    if (GUILayout.Button(new GUIContent("Default", "return to the quiet Combat + Harvest view"),
        GUILayout.Width(62f))) {
      SetVisibleSchools(LabCategory.DefaultVisible);
    }

    if (GUILayout.Button(new GUIContent(_paused ? "Resume" : "Pause",
        _paused ? "return to the live event stream" : "freeze the rows currently visible"),
        GUILayout.Width(70f))) {
      if (_paused) {
        _paused = false;
        _pausedRows.Clear();
      } else {
        _pausedRows.Clear();
        // Capture every retained row, then keep school/search filters live over that frozen
        // moment. A creator can narrow a paused fight without new events moving underneath it.
        _pausedRows.AddRange(_ring.Recent(null, string.Empty, Math.Max(1, _ring.Count)));
        _paused = true;
      }
    }
    if (GUILayout.Button(new GUIContent("Clear log", "discard retained event rows"),
        GUILayout.Width(72f))) {
      _ring.Clear();
      _pausedRows.Clear();
    }
    if (GUILayout.Button(new GUIContent("Exports",
        "open the optional local CSV / Google Sheets companion"), GUILayout.Width(66f))) {
      OpenSheetsExporter();
    }
    GUILayout.EndHorizontal();

    List<LabEvent> rows = VisibleRows();
    GUILayout.Space(3f);
    if (_paused) {
      Color previous = GUI.contentColor;
      GUI.contentColor = new Color(1f, 0.78f, 0.38f, 1f);
      GUILayout.Label("PAUSED  /  " + rows.Count
          + " visible row" + (rows.Count == 1 ? string.Empty : "s") + " frozen");
      GUI.contentColor = previous;
    } else {
      GUILayout.Label(_visible.Count + "/" + LabCategory.All.Length
          + " schools  ·  BINDABLE enters the quest evaluator  ·  DIAGNOSTIC is observation only");
    }

    // --- spreadsheet ---------------------------------------------------------------
    DrawGridHeader();
    _scroll = GUILayout.BeginScrollView(_scroll);

    if (rows.Count == 0) {
      if (_paused) {
        GUILayout.Label("Paused with no visible rows. Resume when you are ready.");
      } else {
        GUILayout.Label(EmptyMessage());
      }
    } else {
      for (int i = 0; i < rows.Count; i++) {
        DrawGridRow(rows[i], i);
      }
    }
    GUILayout.EndScrollView();
  }

  void SetVisibleSchools(IEnumerable<string> schools) {
    _visible.Clear();
    foreach (string school in schools) {
      _visible.Add(school);
    }
  }

  List<LabEvent> VisibleRows() {
    if (_visible.Count == 0) {
      return new List<LabEvent>();
    }
    if (!_paused) {
      return _ring.Recent(_visible, _filterText, LabConfig.ConsoleRows.Value);
    }

    var rows = new List<LabEvent>();
    int max = Math.Max(1, LabConfig.ConsoleRows.Value);
    foreach (LabEvent row in _pausedRows) {
      if (_visible.Contains(row.Category) && RowMatches(row, _filterText)) {
        rows.Add(row);
        if (rows.Count >= max) {
          break;
        }
      }
    }
    return rows;
  }

  static bool RowMatches(LabEvent row, string needle) {
    if (string.IsNullOrEmpty(needle)) {
      return true;
    }
    return Contains(row.Seam, needle)
        || Contains(row.EventName, needle)
        || Contains(row.Target, needle)
        || Contains(row.Detail, needle);
  }

  static bool Contains(string value, string needle) {
    return (value ?? string.Empty).IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
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
        LabRunes.For(row.Category), LabJournal.For(row.Category).Title + " school"),
        style, SchoolColumn);
    style.normal.textColor = prior;
    style.hover.textColor = priorHover;

    string eventTip = (row.Usability == LabUsability.Today
            ? "click to copy trigger.event: "
            : "creator event: ")
        + creatorName + "\nsource seam: " + row.Seam;
    if (row.Usability == LabUsability.Today && !string.IsNullOrWhiteSpace(row.EventName)) {
      style.normal.textColor = new Color(0.72f, 0.88f, 1f, 1f);
      style.hover.textColor = Color.white;
      if (GridButton(new GUIContent(LabSpellNames.For(creatorName), eventTip),
          style, EventColumn)) {
        CopyToClipboard("trigger.event", creatorName);
      }
      style.normal.textColor = prior;
      style.hover.textColor = priorHover;
    } else {
      GridCell(new GUIContent(LabSpellNames.For(creatorName), eventTip), style, EventColumn);
    }
    GridCell(new GUIContent(targetDetail,
        string.IsNullOrWhiteSpace(targetDetail) ? "no target detail" : targetDetail),
        style, -1f);

    Color usabilityColor = UsabilityColor(row.Usability);
    style.normal.textColor = usabilityColor;
    style.hover.textColor = usabilityColor;
    GridCell(new GUIContent(UsabilityCue(row.Usability), UsabilityLine(row.Usability)),
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

  static bool GridButton(GUIContent content, GUIStyle style, float width) {
    if (width > 0f) {
      return GUILayout.Button(content, style, GUILayout.Width(width), GUILayout.Height(28f));
    }
    return GUILayout.Button(content, style, GUILayout.MinWidth(120f),
        GUILayout.ExpandWidth(true), GUILayout.Height(28f));
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

  static string UsabilityCue(string usability) {
    string cue = usability == LabUsability.Today
        ? "OK"
        : (usability == LabUsability.DiagnosticOnly ? "INFO" : "NO");
    return StatusCue(cue, UsabilityLabel(usability));
  }

  static string StatusCue(string cue, string label) {
    return "[" + cue + "] " + label;
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
    _showQuestFolder = GUILayout.Toggle(_showQuestFolder,
        new GUIContent("Path", "show the quest directory; click the path to copy it"),
        GUI.skin.button, GUILayout.Width(54f));
    if (GUILayout.Button(new GUIContent("Open folder", "open the local quest directory"),
        GUILayout.Width(92f))) {
      OpenQuestFolder();
    }
    if (GUILayout.Button(new GUIContent("Reload", "re-read every quest file and reset cooldowns"),
        GUILayout.Width(70f))) {
      ComfyQuestLab.Report(LabQuestEngine.Reload());
    }
    GUILayout.EndHorizontal();

    if (_showQuestFolder) {
      if (GUILayout.Button(new GUIContent(LabQuestEngine.QuestDir,
          "click to copy this directory"), _questDetailStyle)) {
        CopyToClipboard("quest folder", LabQuestEngine.QuestDir);
      }
    }

    string lastEvent = LabQuestEngine.LastEventLine;
    DrawMatcherStatus(lastEvent);

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
          new GUIContent(
              (_showQuestErrors ? "▲" : "▼") + "  " + set.Errors.Count + " LOAD ERROR"
                  + (set.Errors.Count == 1 ? string.Empty : "S"),
              "show or hide quest-file parse failures"),
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

  void DrawMatcherStatus(string lastEvent) {
    string line = string.IsNullOrEmpty(lastEvent) ? "none yet" : lastEvent;
    Color prior = _statusStyle.normal.textColor;
    Color priorHover = _statusStyle.hover.textColor;
    Color color = string.IsNullOrEmpty(lastEvent)
        ? new Color(0.72f, 0.76f, 0.82f, 1f)
        : (lastEvent.IndexOf("fired ", StringComparison.OrdinalIgnoreCase) >= 0
            ? new Color(0.55f, 1f, 0.64f, 1f)
            : (lastEvent.IndexOf("cooling down", StringComparison.OrdinalIgnoreCase) >= 0
                ? new Color(1f, 0.78f, 0.38f, 1f)
                : new Color(0.78f, 0.84f, 0.92f, 1f)));
    _statusStyle.normal.textColor = color;
    _statusStyle.hover.textColor = color;
    GUILayout.Label(new GUIContent("LAST MATCH  /  " + line,
        "the exact canonical event, target, and evaluator outcome"),
        _statusStyle, GUILayout.Height(26f));
    _statusStyle.normal.textColor = prior;
    _statusStyle.hover.textColor = priorHover;
  }

  void DrawQuestGridHeader() {
    GUILayout.BeginHorizontal();
    GridCell(new GUIContent("SCHOOL"), _gridHeaderStyle, SchoolColumn);
    GridCell(new GUIContent("QUEST"), _gridHeaderStyle, -1f);
    GridCell(new GUIContent("EVENT -> TARGET"), _gridHeaderStyle, QuestTriggerColumn);
    GridCell(new GUIContent("STATE"), _gridHeaderStyle, QuestStateColumn);
    GridCell(new GUIContent("FIRES"), _gridHeaderStyle, QuestFiresColumn);
    GridCell(new GUIContent("DETAIL", "open one quest's diagnostics"), _gridHeaderStyle,
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
    string triggerText = eventName + " -> " + target;
    if (quest.IsArmed && !string.IsNullOrWhiteSpace(eventName)) {
      style.normal.textColor = new Color(0.72f, 0.88f, 1f, 1f);
      style.hover.textColor = Color.white;
      if (GridButton(new GUIContent(triggerText,
          "click to copy trigger.event: " + eventName), style, QuestTriggerColumn)) {
        CopyToClipboard("trigger.event", eventName);
      }
      style.normal.textColor = prior;
      style.hover.textColor = priorHover;
    } else {
      GridCell(new GUIContent(triggerText, quest.ArmedLine() + "; no bindable ID to copy"),
          style, QuestTriggerColumn);
    }

    Color stateColor = QuestStateColor(quest.Armed);
    style.normal.textColor = stateColor;
    style.hover.textColor = stateColor;
    GridCell(new GUIContent(QuestStateCue(quest.Armed), quest.ArmedLine()),
        style, QuestStateColumn);
    style.normal.textColor = prior;
    style.hover.textColor = priorHover;

    double cooldown = quest.IsArmed ? LabQuestEngine.CooldownRemaining(quest.QuestId) : 0.0;
    string fireTip = cooldown > 0.0
        ? "re-arms in " + Mathf.CeilToInt((float) cooldown) + "s"
        : "since the last reload";
    GridCell(new GUIContent(quest.IsArmed ? quest.Fires.ToString() : "-", fireTip),
        style, QuestFiresColumn);
    if (GUILayout.Button(new GUIContent(expanded ? "▲" : "▼",
        expanded ? "collapse details" : "show source, verdict, cooldown, and advice"),
        GUILayout.Width(QuestExpandColumn),
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

  static string QuestStateCue(string armed) {
    string cue = armed == LabArmed.Yes
        ? "OK"
        : (armed == LabArmed.UnsupportedEvent || armed == LabArmed.Unknown ? "CHECK" : "INFO");
    return StatusCue(cue, QuestStateLabel(armed));
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

  /// <summary>A bounded rehearsal cockpit over the shared 34-event authoring catalog.
  /// Preparing writes only Lab-owned artifacts; running feeds one generated ordinary quest and
  /// one catalog example to the exact shared evaluator. The UI calls the existing batch
  /// lifecycle, so local clicks and allowlisted OMEN/i5 requests produce the same receipts.</summary>
  void DrawScenarioCockpit() {
    for (int row = 0; row < 2; row++) {
      GUILayout.BeginHorizontal();
      for (int i = row * 4; i < (row + 1) * 4 && i < LabCategory.All.Length; i++) {
        string category = LabCategory.All[i];
        bool selected = string.Equals(
            _scenarioCategory, category, StringComparison.OrdinalIgnoreCase);
        if (RuneToggle(selected, category, LabJournal.For(category).Title, 138f) && !selected) {
          _scenarioCategory = category;
          LabScenarioDefinition first = LabScenarioCatalog.FirstForSchool(category);
          _selectedScenarioId = first == null ? null : first.Id;
          _scenarioScroll = Vector2.zero;
        }
      }
      GUILayout.EndHorizontal();
    }
    GUILayout.Space(5f);

    GUILayout.BeginHorizontal();
    GUILayout.Label("Pick one creator event. Preview runs are exact evaluator evidence, not a "
        + "claim that gameplay happened.");
    GUILayout.FlexibleSpace();
    GUILayout.Label(LabScenarioCatalog.All.Count + " events");
    GUILayout.EndHorizontal();

    DrawScenarioGridHeader();
    _scenarioScroll = GUILayout.BeginScrollView(
        _scenarioScroll, GUILayout.Height(Mathf.Min(218f, _window.height * 0.34f)));
    int visibleIndex = 0;
    foreach (LabScenarioDefinition scenario in LabScenarioCatalog.All) {
      if (!string.Equals(
          scenario.School, _scenarioCategory, StringComparison.OrdinalIgnoreCase)) {
        continue;
      }
      DrawScenarioGridRow(scenario, visibleIndex++);
    }
    GUILayout.EndScrollView();

    LabScenarioDefinition current = LabScenarioCatalog.Find(_selectedScenarioId)
        ?? LabScenarioCatalog.FirstForSchool(_scenarioCategory);
    if (current == null) return;

    Color schoolColor = LabRunes.ColorFor(current.School);
    DrawSectionHeading(current.EventName.ToUpperInvariant(), schoolColor,
        LabRunes.For(current.School));
    GUILayout.BeginVertical(_questDetailStyle);
    GUILayout.Label("target  /  " + current.TargetKind + "  /  " + current.TargetDescription);
    GUILayout.Label("example  /  " + current.ExampleTarget
        + "    profile  /  " + current.Profile.ToUpperInvariant()
        + "    safety  /  " + current.Safety);
    GUILayout.Label("try live  /  " + current.LiveAction);
    if (current.Fields.Count > 0) {
      var fields = new List<string>();
      foreach (QuestEventCatalog.FieldDefinition field in current.Fields) {
        fields.Add(field.Name + "=" + field.Example
            + (field.DraftByDefault ? " (drafted)" : " (optional)"));
      }
      GUILayout.Label("where fields  /  " + string.Join("  Â·  ", fields.ToArray()));
    }
    GUILayout.Label(current.CourseWitnessAvailable
        ? "live lane  /  staged in the all-schools compact course"
        : "live lane  /  follow the action above; this one is not staged by the compact course");
    GUILayout.EndVertical();

    DrawScenarioControls(current);
  }

  void DrawScenarioGridHeader() {
    GUILayout.BeginHorizontal();
    GridCell(new GUIContent("EVENT"), _gridHeaderStyle, ScenarioEventColumn);
    GridCell(new GUIContent("TARGET / EXAMPLE"), _gridHeaderStyle, -1f);
    GridCell(new GUIContent("PROFILE"), _gridHeaderStyle, ScenarioProfileColumn);
    GridCell(new GUIContent("OPEN"), _gridHeaderStyle, ScenarioSelectColumn);
    GUILayout.EndHorizontal();
  }

  void DrawScenarioGridRow(LabScenarioDefinition scenario, int index) {
    GUIStyle style = index % 2 == 0 ? _gridEvenStyle : _gridOddStyle;
    bool selected = string.Equals(
        _selectedScenarioId, scenario.Id, StringComparison.OrdinalIgnoreCase);
    Color prior = style.normal.textColor;
    Color priorHover = style.hover.textColor;
    if (selected) {
      style.normal.textColor = LabRunes.ColorFor(scenario.School);
      style.hover.textColor = Color.white;
    }
    GUILayout.BeginHorizontal();
    if (GridButton(new GUIContent((selected ? "â–¶  " : string.Empty) + scenario.EventName,
        "click to select; canonical trigger.event is " + scenario.EventName),
        style, ScenarioEventColumn)) {
      _selectedScenarioId = scenario.Id;
    }
    GridCell(new GUIContent(scenario.TargetKind + " / " + scenario.ExampleTarget,
        scenario.TargetDescription), style, -1f);
    GridCell(new GUIContent(scenario.Profile.ToUpperInvariant(),
        scenario.Profile + " runtime profile"), style, ScenarioProfileColumn);
    if (GridButton(new GUIContent("Select", "open this rehearsal"),
        style, ScenarioSelectColumn)) {
      _selectedScenarioId = scenario.Id;
    }
    GUILayout.EndHorizontal();
    style.normal.textColor = prior;
    style.hover.textColor = priorHover;
  }

  void DrawScenarioControls(LabScenarioDefinition scenario) {
    LabBatchController batch = ComfyQuestLab.Batch;
    if (batch == null) {
      GUILayout.Label("Scenario runner is unavailable in this game state.");
      return;
    }

    GUILayout.BeginHorizontal();
    bool controlsEnabled = GUI.enabled;
    GUI.enabled = controlsEnabled && !batch.IsPreparing && !batch.IsLiveCaptureRunning;
    if (GUILayout.Button(new GUIContent("1  Prepare draft",
        "write an owned scenario manifest and ordinary schema-1 quest; do not load it"))) {
      ComfyQuestLab instance = ComfyQuestLab.Instance;
      if (instance != null) instance.StartCoroutine(batch.Prepare(instance, scenario.Id));
    }
    if (GUILayout.Button(new GUIContent("2  Run evaluator",
        "run this one example through the exact shared loader and evaluator"))) {
      ComfyQuestLab.Report(batch.Run(scenario.Id));
    }
    GUI.enabled = controlsEnabled;
    if (GUILayout.Button(new GUIContent("Reset", "clear volatile evidence and restore cooldown"),
        GUILayout.Width(70f))) {
      ComfyQuestLab.Report(batch.Reset());
    }
    if (GUILayout.Button(new GUIContent("Report", "show current scenario progress"),
        GUILayout.Width(70f))) {
      ComfyQuestLab.Report(batch.Report());
    }
    GUI.enabled = controlsEnabled && batch.Session != null;
    if (GUILayout.Button(new GUIContent("Export", "write a machine-readable suite receipt"),
        GUILayout.Width(70f))) {
      ComfyQuestLab.Report(batch.Export());
    }
    GUI.enabled = controlsEnabled;
    GUILayout.EndHorizontal();

    bool prepared = string.Equals(
        batch.PreparedSuiteId, scenario.Id, StringComparison.OrdinalIgnoreCase);
    LabBatchSession session = batch.Session;
    bool currentRun = session != null && string.Equals(
        session.Suite.Id, scenario.Id, StringComparison.OrdinalIgnoreCase);
    Color prior = GUI.contentColor;
    GUI.contentColor = currentRun && session.Verdict == "pass"
        ? new Color(0.55f, 1f, 0.64f, 1f)
        : (prepared
            ? new Color(0.72f, 0.88f, 1f, 1f)
            : new Color(0.72f, 0.76f, 0.82f, 1f));
    GUILayout.Label(currentRun
        ? "STATUS  /  " + session.Summary()
        : (prepared
            ? "STATUS  /  PREPARED  /  " + batch.PreparedArtifactPath
            : "STATUS  /  choose Prepare, then Run evaluator"));
    GUI.contentColor = prior;

    GUILayout.BeginHorizontal();
    if (GUILayout.Button(new GUIContent("Copy draft JSON",
        "copy the exact editable schema-1 quest generated by QuestAuthoring"),
        GUILayout.Width(128f))) {
      CopyScenarioDraft(scenario);
    }
    if (prepared && !string.IsNullOrWhiteSpace(batch.PreparedArtifactPath)) {
      if (GUILayout.Button(new GUIContent("Copy draft path",
          "copy the generated quest-view.json path"), GUILayout.Width(128f))) {
        CopyToClipboard("scenario draft", batch.PreparedQuestPath);
      }
      if (GUILayout.Button(new GUIContent("Open artifacts",
          "open the generated scenario folder"), GUILayout.Width(118f))) {
        OpenDirectory("scenario artifacts", Path.GetDirectoryName(batch.PreparedArtifactPath));
      }
    }
    GUILayout.FlexibleSpace();
    GUILayout.EndHorizontal();
  }

  void CopyScenarioDraft(LabScenarioDefinition scenario) {
    try {
      GUIUtility.systemCopyBuffer = scenario.BuildQuestView();
      ShowNotice("COPIED EDITABLE SCHEMA-1 DRAFT  /  " + scenario.EventName);
    } catch (Exception ex) {
      ShowNotice("COPY FAILED  /  " + ex.Message);
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

    Color schoolColor = LabRunes.ColorFor(current.Category);
    DrawSectionHeading(current.Title.ToUpperInvariant(), schoolColor,
        LabRunes.For(current.Category));
    foreach (string line in current.What) {
      GUILayout.Label(line);
    }

    DrawSectionHeading("TRY THIS", schoolColor);
    foreach (string line in current.Try) {
      GUILayout.Label("  " + line);
    }

    DrawSectionHeading("WORTH KNOWING", new Color(1f, 0.78f, 0.38f, 1f));
    foreach (string line in current.Watch) {
      GUILayout.Label("  " + line);
    }

    DrawSectionHeading("WORLD ACTIONS", schoolColor);
    GUILayout.BeginHorizontal();
    GUILayout.Label("One row per integrated action; hover clipped cells for the full wording.");
    GUILayout.FlexibleSpace();
    // You do not need a thing's true name to see it happen — only to command it. So the
    // method names are here, and they are not in your way until you ask.
    _showTrueNames = GUILayout.Toggle(_showTrueNames,
        new GUIContent("true names", "show exact Valheim method names"), GUI.skin.button,
        GUILayout.Width(96f));
    GUILayout.EndHorizontal();

    DrawSpellGridHeader();
    for (int i = 0; i < current.Spells.Length; i++) {
      DrawSpellGridRow(current.Spells[i], i);
    }

    GUILayout.Space(8f);
    int unavailable = 0;
    foreach (LabPatching.Outcome outcome in LabPatching.Outcomes) {
      if (!outcome.Applied) {
        unavailable++;
      }
    }
    Color statusColor = unavailable == 0
        ? new Color(0.55f, 1f, 0.64f, 1f)
        : new Color(1f, 0.78f, 0.38f, 1f);
    DrawSectionHeading((unavailable == 0 ? "[OK] " : "[CHECK] ")
        + "BUILD COVERAGE  /  " + LabPatching.AppliedCount + " OF "
        + LabPatching.Outcomes.Count + " HOOKS AVAILABLE", statusColor);
    if (unavailable > 0) {
      foreach (LabPatching.Outcome outcome in LabPatching.Outcomes) {
        if (!outcome.Applied) {
          GUILayout.Label("  unavailable: " + outcome.Label + " — " + outcome.Detail);
        }
      }
    }

    GUILayout.EndScrollView();
  }

  void DrawSectionHeading(string text, Color color, Texture texture = null) {
    Color prior = _sectionHeaderStyle.normal.textColor;
    Color priorHover = _sectionHeaderStyle.hover.textColor;
    _sectionHeaderStyle.normal.textColor = color;
    _sectionHeaderStyle.hover.textColor = color;
    GUILayout.Space(7f);
    GUILayout.Label(new GUIContent(text, texture), _sectionHeaderStyle,
        GUILayout.Height(27f));
    _sectionHeaderStyle.normal.textColor = prior;
    _sectionHeaderStyle.hover.textColor = priorHover;
  }

  void DrawSpellGridHeader() {
    GUILayout.BeginHorizontal();
    GridCell(new GUIContent("WORLD ACTION"), _gridHeaderStyle, -1f);
    GridCell(new GUIContent("QUEST USE"), _gridHeaderStyle, SpellUseColumn);
    if (_showTrueNames) {
      GridCell(new GUIContent("TRUE NAME", "exact Valheim method"),
          _gridHeaderStyle, SpellTrueNameColumn);
    }
    GUILayout.EndHorizontal();
  }

  void DrawSpellGridRow(LabJournal.Spell spell, int index) {
    GUIStyle style = index % 2 == 0 ? _gridEvenStyle : _gridOddStyle;
    Color prior = style.normal.textColor;
    Color priorHover = style.hover.textColor;
    Color rowColor = spell.Bound
        ? prior
        : new Color(0.62f, 0.67f, 0.74f, 1f);
    style.normal.textColor = rowColor;
    style.hover.textColor = rowColor;

    GUILayout.BeginHorizontal();
    GridCell(new GUIContent(spell.Name, spell.Verdict), style, -1f);
    string bindableEvent = BindableEventName(spell);
    Color useColor = SpellUseColor(spell);
    style.normal.textColor = useColor;
    style.hover.textColor = string.IsNullOrEmpty(bindableEvent) ? useColor : Color.white;
    if (!string.IsNullOrEmpty(bindableEvent)) {
      if (GridButton(new GUIContent(SpellUseCue(spell),
          "click to copy trigger.event: " + bindableEvent), style, SpellUseColumn)) {
        CopyToClipboard("trigger.event", bindableEvent);
      }
    } else {
      GridCell(new GUIContent(SpellUseCue(spell), spell.Verdict), style, SpellUseColumn);
    }
    style.normal.textColor = rowColor;
    style.hover.textColor = rowColor;
    if (_showTrueNames) {
      GridCell(new GUIContent(spell.TrueName, "exact diagnostic provenance"),
          style, SpellTrueNameColumn);
    }
    GUILayout.EndHorizontal();
    style.normal.textColor = prior;
    style.hover.textColor = priorHover;
  }

  static string SpellUseLabel(LabJournal.Spell spell) {
    if (!spell.Bound) {
      return "NOT IN BUILD";
    }
    string bindableEvent = BindableEventName(spell);
    if (!string.IsNullOrEmpty(bindableEvent)) {
      return "BINDABLE  /  " + bindableEvent;
    }
    if ((spell.Verdict ?? string.Empty).IndexOf(
        "diagnostic", StringComparison.OrdinalIgnoreCase) >= 0) {
      return "DIAGNOSTIC";
    }
    return "OBSERVED";
  }

  static string SpellUseCue(LabJournal.Spell spell) {
    string cue = !spell.Bound
        ? "NO"
        : (!string.IsNullOrEmpty(BindableEventName(spell)) ? "OK" : "INFO");
    return StatusCue(cue, SpellUseLabel(spell));
  }

  static string BindableEventName(LabJournal.Spell spell) {
    string verdict = spell.Verdict ?? string.Empty;
    return spell.Bound && verdict.StartsWith("bindable:", StringComparison.OrdinalIgnoreCase)
        ? verdict.Substring("bindable:".Length).Trim()
        : string.Empty;
  }

  static Color SpellUseColor(LabJournal.Spell spell) {
    if (!spell.Bound) {
      return new Color(0.62f, 0.67f, 0.74f, 1f);
    }
    if (!string.IsNullOrEmpty(BindableEventName(spell))) {
      return new Color(0.55f, 1f, 0.64f, 1f);
    }
    if ((spell.Verdict ?? string.Empty).IndexOf(
        "diagnostic", StringComparison.OrdinalIgnoreCase) >= 0) {
      return new Color(1f, 0.78f, 0.38f, 1f);
    }
    return new Color(0.62f, 0.82f, 1f, 1f);
  }

  /// <summary>A factual pre-demo cockpit. It reports loaded runtime state and the bounded
  /// live suite only; it deliberately cannot certify how the gallery looks.</summary>
  void DrawReadiness() {
    LabReadinessSnapshot readiness = CaptureReadiness();
    Color heading = readiness.Status == LabReadinessSnapshot.NeedsAttention
        ? new Color(1f, 0.62f, 0.46f, 1f)
        : (readiness.Status == LabReadinessSnapshot.LiveLapProved
            ? new Color(0.55f, 1f, 0.64f, 1f)
            : new Color(0.72f, 0.88f, 1f, 1f));
    _readinessScroll = GUILayout.BeginScrollView(_readinessScroll);
    DrawSectionHeading(readiness.StatusLabel, heading);
    GUILayout.Label(readiness.Summary);

    LabQuestSet set = LabQuestEngine.Set;
    LabBatchSession live = LiveAllSchoolsSession();
    int hookTotal = LabPatching.Outcomes.Count;
    int hookApplied = LabPatching.AppliedCount;
    DrawReadinessRow("RELEASE",
        "[INFO] " + ComfyQuestLab.ReleaseId + "  /  profile " + CurrentRuntimeProfile(),
        "build identity and active integration profile");
    DrawReadinessRow("INPUT",
        StatusCue(InputGuard.PanelOwnsInput ? "OK" : "CHECK",
            InputGuard.OwnershipMode.ToUpperInvariant()
                + (InputGuard.PanelOwnsInput
                    ? " / pointer owned, player actions blocked"
                    : " / Valheim owns controls")),
        "the clickable panel must own input; closing restores gameplay");
    DrawReadinessRow("INTEGRATIONS",
        StatusCue(hookTotal > 0 && hookApplied == hookTotal ? "OK" : "CHECK",
            hookApplied + "/" + hookTotal + " hooks available"),
        "real Harmony outcomes from this running Valheim build");
    DrawReadinessRow("CREATOR EVENTS",
        StatusCue(QuestEventCatalog.Count == LabSeamCatalog.CreatorSafeEventCount ? "OK" : "CHECK",
            QuestEventCatalog.Count + " evaluator / "
                + LabSeamCatalog.CreatorSafeEventCount + " manifest"),
        "safe canonical names must agree across the shared evaluator and generated atlas");
    DrawReadinessRow("QUEST FILES",
        StatusCue(set.Errors.Count == 0 && set.ArmedCount > 0 ? "OK" : "CHECK",
            set.ArmedCount + "/" + set.Quests.Count + " armed / "
                + set.Errors.Count + " load errors"),
        "current quest directory after the latest reload");
    int schoolCount = ArmedSchoolCount(set);
    DrawReadinessRow("EXAMPLE SCHOOLS",
        StatusCue(schoolCount == LabDemoReadiness.RequiredSchools ? "OK" : "CHECK",
            schoolCount + "/" + LabDemoReadiness.RequiredSchools + " have an armed quest"),
        "one bindable loaded example in each creator school");
    if (live == null) {
      DrawReadinessRow("LIVE SUITE", "[READY] not run in this process",
          "a passed machine receipt requires eight real actions and eight completions");
    } else {
      string cue = live.Verdict == "pass"
          ? "PROVED"
          : (live.Verdict == "fail_double_completion" ? "CHECK" : "INFO");
      DrawReadinessRow("LIVE SUITE",
          StatusCue(cue, live.WitnessedCount + "/" + live.Suite.Expectations.Length
              + " witnessed / " + live.CompletedQuestCount + "/"
              + live.Suite.Expectations.Length + " completed / " + live.Verdict),
          "bounded all-schools suite state from this process; export writes the receipt");
    }

    if (readiness.Issues.Count > 0) {
      DrawSectionHeading("WHAT NEEDS ATTENTION", heading);
      foreach (string issue in readiness.Issues) {
        GUILayout.Label("[CHECK] " + issue);
      }
    } else {
      DrawSectionHeading("NEXT SAFE STEP", heading);
      if (live != null && live.Verdict == "pass") {
        GUILayout.Label("F5  /  questlab_batch export    writes the machine-readable receipt.");
      } else if (live != null) {
        GUILayout.Label("Continue the missing gallery spokes, then F5  /  questlab_batch report.");
      } else {
        GUILayout.Label("F5  /  questlab_batch prepare all-schools    then    "
            + "questlab_batch run all-schools");
      }
    }

    GUILayout.Space(6f);
    GUILayout.BeginHorizontal();
    if (GUILayout.Button(new GUIContent("Copy summary", "copy this factual readiness line"),
        GUILayout.Width(104f))) {
      CopyToClipboard("readiness", readiness.Summary);
    }
    if (GUILayout.Button(new GUIContent("Reload quests", "re-read every quest file"),
        GUILayout.Width(104f))) {
      ComfyQuestLab.Report(LabQuestEngine.Reload());
    }
    if (GUILayout.Button(new GUIContent("Open quest folder", "open the local quest directory"),
        GUILayout.Width(126f))) {
      OpenQuestFolder();
    }
    if (GUILayout.Button(new GUIContent("Reset layout",
        "restore 100% zoom and a visible default window"), GUILayout.Width(104f))) {
      ResetPanelLayout();
    }
    GUILayout.EndHorizontal();

    GUILayout.Space(8f);
    GUILayout.Label("[INFO] Runtime readiness is not visual acceptance. Gallery scale, lighting, "
        + "readability, and composition still require a human look at the rendered frame.");
    GUILayout.EndScrollView();
  }

  void DrawReadinessRow(string label, string value, string tooltip) {
    GUILayout.BeginHorizontal();
    GridCell(new GUIContent(label), _gridHeaderStyle, 152f);
    GridCell(new GUIContent(value, tooltip), _gridEvenStyle, -1f);
    GUILayout.EndHorizontal();
  }

  LabReadinessSnapshot CaptureReadiness() {
    LabQuestSet set = LabQuestEngine.Set;
    LabBatchSession live = LiveAllSchoolsSession();
    var input = new LabReadinessInput {
      HooksApplied = LabPatching.AppliedCount,
      HooksExpected = LabPatching.Outcomes.Count,
      CatalogEventCount = QuestEventCatalog.Count,
      ManifestEventCount = LabSeamCatalog.CreatorSafeEventCount,
      QuestsLoaded = set.Quests.Count,
      QuestsArmed = set.ArmedCount,
      QuestLoadErrors = set.Errors.Count,
      ArmedSchoolCount = ArmedSchoolCount(set),
      QuestsEnabled = LabConfig.QuestsEnabled != null && LabConfig.QuestsEnabled.Value,
      InputOwned = InputGuard.PanelOwnsInput,
      RuntimeProfile = CurrentRuntimeProfile(),
      ReleaseId = ComfyQuestLab.ReleaseId,
      HasLiveSuite = live != null,
      LiveSuiteRequired = live == null ? 0 : live.Suite.Expectations.Length,
      LiveSuiteWitnessed = live == null ? 0 : live.WitnessedCount,
      LiveSuiteCompleted = live == null ? 0 : live.CompletedQuestCount,
      LiveSuiteVerdict = live == null ? null : live.Verdict,
    };
    return LabDemoReadiness.Assess(input);
  }

  static int ArmedSchoolCount(LabQuestSet set) {
    var schools = new List<string>();
    if (set != null) {
      foreach (LabQuest quest in set.Quests) {
        if (quest.IsArmed) {
          schools.Add(QuestSchool(quest));
        }
      }
    }
    return LabDemoReadiness.DistinctKnownSchools(schools);
  }

  static LabBatchSession LiveAllSchoolsSession() {
    LabBatchController batch = ComfyQuestLab.Batch;
    LabBatchSession session = batch == null ? null : batch.Session;
    return session != null
        && session.Suite != null
        && string.Equals(session.Suite.Id, "all-schools", StringComparison.OrdinalIgnoreCase)
        && string.Equals(session.Suite.EvidenceKind, "live-gameplay", StringComparison.OrdinalIgnoreCase)
            ? session
            : null;
  }

  static string CurrentRuntimeProfile() {
    return LabConfig.EventProfile == null || string.IsNullOrWhiteSpace(LabConfig.EventProfile.Value)
        ? LabRuntimeProfile.Extended
        : LabConfig.EventProfile.Value;
  }

  void DrawHelpBar() {
    string tooltip = GUI.tooltip;
    bool hasTooltip = !string.IsNullOrWhiteSpace(tooltip);
    bool hasNotice = !string.IsNullOrWhiteSpace(_notice)
        && Time.realtimeSinceStartup < _noticeUntil;
    Color prior = _helpStyle.normal.textColor;
    Color priorHover = _helpStyle.hover.textColor;
    Color color = hasNotice
        ? new Color(0.55f, 1f, 0.64f, 1f)
        : (hasTooltip
            ? new Color(0.72f, 0.88f, 1f, 1f)
            : new Color(0.66f, 0.72f, 0.80f, 1f));
    _helpStyle.normal.textColor = color;
    _helpStyle.hover.textColor = color;
    GUILayout.Space(4f);
    string help = hasNotice
        ? _notice
        : (hasTooltip
            ? tooltip.Replace("\n", "  /  ")
            : "Hover for details  ·  blue cells copy exact IDs  ·  F6/Esc close  ·  drag ↘ to resize");
    GUILayout.Label(help,
        _helpStyle, GUILayout.Height(24f));
    _helpStyle.normal.textColor = prior;
    _helpStyle.hover.textColor = priorHover;
  }

  void CopyToClipboard(string label, string value) {
    if (string.IsNullOrWhiteSpace(value)) {
      return;
    }
    try {
      GUIUtility.systemCopyBuffer = value;
      ShowNotice("COPIED " + label.ToUpperInvariant() + "  /  " + value);
    } catch (Exception ex) {
      ShowNotice("COPY FAILED  /  " + ex.Message);
    }
  }

  void OpenQuestFolder() {
    try {
      Directory.CreateDirectory(LabQuestEngine.QuestDir);
      Application.OpenURL(new Uri(LabQuestEngine.QuestDir).AbsoluteUri);
      ShowNotice("OPENED QUEST FOLDER");
    } catch (Exception ex) {
      ShowNotice("OPEN FAILED  /  " + ex.Message);
    }
  }

  void OpenSheetsExporter() {
    try {
      Application.OpenURL(SheetsExporterUrl);
      ShowNotice("OPENED LOCAL EXPORTS  /  START THE COMPANION IF THE PAGE IS OFFLINE");
    } catch (Exception ex) {
      ShowNotice("EXPORTS OPEN FAILED  /  " + ex.Message);
    }
  }

  void OpenDirectory(string label, string path) {
    try {
      if (string.IsNullOrWhiteSpace(path)) return;
      Directory.CreateDirectory(path);
      Application.OpenURL(new Uri(path).AbsoluteUri);
      ShowNotice("OPENED " + (label ?? "folder").ToUpperInvariant());
    } catch (Exception ex) {
      ShowNotice("OPEN FAILED  /  " + ex.Message);
    }
  }

  void ShowNotice(string notice) {
    _notice = notice ?? string.Empty;
    _noticeUntil = Time.realtimeSinceStartup + 4f;
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

    _statusStyle = GridStyle(
        _gridEvenBackground, new Color(0.78f, 0.84f, 0.92f, 1f), FontStyle.Bold);
    _sectionHeaderStyle = GridStyle(
        _gridHeaderBackground, Color.white, FontStyle.Bold);
    _sectionHeaderStyle.fontSize = 14;
    _helpStyle = GridStyle(
        _gridEvenBackground, new Color(0.66f, 0.72f, 0.80f, 1f), FontStyle.Normal);
    _helpStyle.fontSize = 12;

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
    style.active.background = background;
    style.active.textColor = text;
    style.onNormal.background = background;
    style.onNormal.textColor = text;
    style.onHover.background = background;
    style.onHover.textColor = text;
    style.onActive.background = background;
    style.onActive.textColor = text;
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
      _saveLayoutAfterDraw = true;
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
    _saveLayoutAfterDraw = true;
  }

  void ResetPanelLayout() {
    try {
      LabConfig.PanelScale.Value = 1f;
    } catch (Exception) {
    }
    _drawScale = 1f;
    _window = ClampWindow(new Rect(
        LabPanelLayout.DefaultX,
        LabPanelLayout.DefaultY,
        DefaultWidth,
        DefaultHeight), 1f);
    _requestedWidth = -1f;
    _requestedHeight = -1f;
    _saveLayoutAfterDraw = true;
    ShowNotice("LAYOUT RESET  /  100% AND VISIBLE BOUNDS");
  }

  static Rect SavedWindow() {
    try {
      return new Rect(
          LabConfig.PanelX.Value,
          LabConfig.PanelY.Value,
          LabConfig.PanelWidth.Value,
          LabConfig.PanelHeight.Value);
    } catch (Exception) {
      return new Rect(
          LabPanelLayout.DefaultX,
          LabPanelLayout.DefaultY,
          DefaultWidth,
          DefaultHeight);
    }
  }

  void SaveWindow() {
    try {
      Rect saved = ClampWindow(_window, CurrentPanelScale());
      LabConfig.PanelX.Value = Mathf.Round(saved.x);
      LabConfig.PanelY.Value = Mathf.Round(saved.y);
      LabConfig.PanelWidth.Value = Mathf.Round(saved.width);
      LabConfig.PanelHeight.Value = Mathf.Round(saved.height);
    } catch (Exception) {
      // Layout persistence is a convenience; closing the panel must always succeed.
    }
  }

  static Rect ClampWindow(Rect rect, float scale) {
    LabPanelBounds bounds = LayoutBounds(rect, scale);
    return new Rect(bounds.X, bounds.Y, bounds.Width, bounds.Height);
  }

  static LabPanelBounds LayoutBounds(Rect rect, float scale) {
    return LabPanelLayout.Clamp(
        rect.x, rect.y, rect.width, rect.height, scale, Screen.width, Screen.height);
  }
}
