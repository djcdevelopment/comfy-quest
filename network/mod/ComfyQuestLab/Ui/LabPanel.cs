namespace ComfyQuestLab;

using System;
using System.Collections.Generic;

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
  bool _resizing;
  Vector2 _resizeStartMouse;
  Vector2 _resizeStartSize;
  float _requestedWidth = -1f;
  float _requestedHeight = -1f;
  Texture2D _windowBackground;
  Texture2D _gridHeaderBackground;
  Texture2D _gridEvenBackground;
  Texture2D _gridOddBackground;
  Texture2D _resizeBackground;
  GUIStyle _windowStyle;
  GUIStyle _gridHeaderStyle;
  GUIStyle _gridEvenStyle;
  GUIStyle _gridOddStyle;
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

    GUIStyle label = GUI.skin.label;
    int oldFontSize = label.fontSize;
    bool oldWordWrap = label.wordWrap;
    Color oldLabelColor = label.normal.textColor;
    Color oldContentColor = GUI.contentColor;
    try {
      label.fontSize = 14;
      label.wordWrap = true;
      label.normal.textColor = new Color(0.94f, 0.96f, 1f, 1f);
      GUI.contentColor = Color.white;
      _window = GUILayout.Window(WindowId, ClampWindow(_window), DrawWindow, "Quest Lab",
          _windowStyle, GUILayout.MinWidth(MinWidth), GUILayout.MinHeight(MinHeight));
      if (_requestedWidth > 0f && _requestedHeight > 0f) {
        _window.width = _requestedWidth;
        _window.height = _requestedHeight;
      }
      _window = ClampWindow(_window);
    } finally {
      label.fontSize = oldFontSize;
      label.wordWrap = oldWordWrap;
      label.normal.textColor = oldLabelColor;
      GUI.contentColor = oldContentColor;
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
      DrawQuests();
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

  /// <summary>Your quest files, and what the lab thinks of them.
  ///
  /// A tab rather than rows in the console, because this is standing state: a validation
  /// problem persists until somebody fixes it, and the ring holds ConsoleRows * 8 events
  /// filtered by category — one fight would scroll a roster away and a filter toggle would
  /// hide it. Only the moments (a reload, a firing) go in the console.
  ///
  /// The line that earns its place here is "last event" below: it shows the canonical name and
  /// target the matcher was actually handed, which turns "why didn't my quest fire" from a
  /// guess into a read.</summary>
  void DrawQuests() {
    LabQuestSet set = LabQuestEngine.Set;

    GUILayout.BeginHorizontal();
    GUILayout.Label(set.Quests.Count + " loaded · " + set.ArmedCount + " armed");
    GUILayout.FlexibleSpace();
    if (GUILayout.Button("Reload", GUILayout.Width(70f))) {
      ComfyQuestLab.Report(LabQuestEngine.Reload());
    }
    GUILayout.EndHorizontal();

    GUILayout.Label("from " + LabQuestEngine.QuestDir);

    string lastEvent = LabQuestEngine.LastEventLine;
    GUILayout.Label("last event the matcher was given: "
        + (string.IsNullOrEmpty(lastEvent) ? "none yet" : lastEvent));

    if (!LabConfig.QuestsEnabled.Value) {
      GUILayout.Label("questsEnabled is OFF — files are loaded, but nothing will fire.");
    }

    GUILayout.Space(4f);
    _questScroll = GUILayout.BeginScrollView(_questScroll);

    if (set.Quests.Count == 0 && set.Errors.Count == 0) {
      GUILayout.Label("No quest files yet. Run lab_setup for a starter one, or drop a");
      GUILayout.Label("quest-view.json into the folder above and run lab_reload.");
    }

    string file = null;
    foreach (LabQuest quest in set.Quests) {
      if (quest.SourceFile != file) {
        file = quest.SourceFile;
        GUILayout.Space(6f);
        GUILayout.Label(file);
      }

      Color before = GUI.color;
      // The same dimming the spellbook uses for a seam it cannot witness: real, but not
      // available to you. A creator should be able to tell at a glance without reading.
      if (!quest.IsArmed) {
        GUI.color = new Color(1f, 1f, 1f, 0.68f);
      }

      GUILayout.Label("  " + (quest.IsArmed ? "*" : "-") + "  " + quest.Quest.Name
          + "  (" + quest.QuestId + ")");
      GUILayout.Label("        -> " + quest.ArmedLine());

      if (quest.IsArmed) {
        double cooldown = LabQuestEngine.CooldownRemaining(quest.QuestId);
        GUILayout.Label("        fired " + quest.Fires
            + (quest.Fires == 1 ? " time" : " times") + " since the last reload"
            + (cooldown > 0.0
                ? "  ·  re-arms in " + Mathf.CeilToInt((float) cooldown) + "s"
                : string.Empty));
      }

      foreach (string note in quest.Advisories) {
        GUILayout.Label("        ! " + note);
      }

      GUI.color = before;
    }

    if (set.Errors.Count > 0) {
      GUILayout.Space(8f);
      GUILayout.Label("Files that would not load");
      foreach (LabQuestFileError error in set.Errors) {
        GUILayout.Label("  " + error.SourceFile);
        // The contract's own sentence first, then ours. Keeping them apart is the point:
        // a creator can tell which half came from the thing that will actually run their
        // quest, and the lab never gets to paraphrase that half.
        GUILayout.Label("      " + error.ContractMessage);
        GUILayout.Label("      " + error.Remedy);
      }
    }

    GUILayout.EndScrollView();
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
      Vector2 delta = mouse - _resizeStartMouse;
      _requestedWidth = _resizeStartSize.x + delta.x;
      _requestedHeight = _resizeStartSize.y + delta.y;
      current.Use();
    }
    if (_resizing && current.rawType == EventType.MouseUp) {
      _resizing = false;
    }
  }

  static Rect ClampWindow(Rect rect) {
    float maxWidth = Mathf.Max(360f, Screen.width - 24f);
    float maxHeight = Mathf.Max(280f, Screen.height - 24f);
    rect.width = Mathf.Clamp(rect.width, Mathf.Min(MinWidth, maxWidth), maxWidth);
    rect.height = Mathf.Clamp(rect.height, Mathf.Min(MinHeight, maxHeight), maxHeight);
    rect.x = Mathf.Clamp(rect.x, 0f, Mathf.Max(0f, Screen.width - rect.width));
    rect.y = Mathf.Clamp(rect.y, 0f, Mathf.Max(0f, Screen.height - rect.height));
    return rect;
  }
}
