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

  readonly LabEventRing _ring;
  readonly HashSet<string> _visible = new HashSet<string>(LabCategory.DefaultVisible);

  Rect _window = new Rect(120f, 140f, 620f, 500f);
  Vector2 _scroll;
  Vector2 _journalScroll;
  string _journalCategory = LabCategory.Harvest;   // where a new builder starts
  string _filterText = string.Empty;
  bool _paused;
  Tab _tab = Tab.Console;

  enum Tab { Console, Spellbook }

  /// <summary>One rune, used as a toggle. The same call renders a spellbook tab and a
  /// console filter, which is the whole reason the runes exist: learning the book
  /// teaches the console for free, because the mark is the same mark.</summary>
  static bool RuneToggle(bool on, string category, string label, float width) {
    Color previous = GUI.color;
    // A rune that is switched off is still legible, just quiet.
    GUI.color = on ? Color.white : new Color(1f, 1f, 1f, 0.45f);
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
    IsOpen = !IsOpen;
    if (!IsOpen) {
      InputGuard.TypingInLab = false;
    }
  }

  public void Close() {
    IsOpen = false;
    InputGuard.TypingInLab = false;
  }

  public void Draw() {
    if (!IsOpen) {
      InputGuard.TypingInLab = false;
      return;
    }
    _window = GUILayout.Window(WindowId, _window, DrawWindow, "Quest Lab");
  }

  void DrawWindow(int id) {
    DrawTabs();
    GUILayout.Space(4f);

    if (_tab == Tab.Console) {
      DrawConsole();
    } else {
      DrawSpellbook();
    }

    // Drag by the title bar, same as every other panel here.
    GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
  }

  void DrawTabs() {
    GUILayout.BeginHorizontal();
    if (GUILayout.Toggle(_tab == Tab.Console, "What just happened", GUI.skin.button)) {
      _tab = Tab.Console;
    }
    if (GUILayout.Toggle(_tab == Tab.Spellbook, "Spellbook", GUI.skin.button)) {
      _tab = Tab.Spellbook;
    }
    GUILayout.FlexibleSpace();
    GUILayout.Label(_ring.Count + " held · " + _ring.TotalSeen + " seen");
    GUILayout.EndHorizontal();
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

    GUILayout.Space(4f);

    // --- rows ----------------------------------------------------------------------
    _scroll = GUILayout.BeginScrollView(_scroll);
    List<LabEvent> rows = _paused
        ? new List<LabEvent>()
        : _ring.Recent(_visible, _filterText, LabConfig.ConsoleRows.Value);

    if (_paused) {
      GUILayout.Label("Paused. Nothing is being dropped — resume to see what arrived.");
    } else if (rows.Count == 0) {
      GUILayout.Label(EmptyMessage());
    } else {
      foreach (LabEvent row in rows) {
        GUILayout.BeginHorizontal();
        GUILayout.Label(new GUIContent(LabRunes.For(row.Category)),
            GUILayout.Width(18f), GUILayout.Height(18f));
        GUILayout.Label(row.At + "  " + row.Seam);
        GUILayout.EndHorizontal();
        GUILayout.Label("        " + row.Target + "   " + row.Detail);
        GUILayout.Label("        " + UsabilityLine(row.Usability));
        GUILayout.Space(3f);
      }
    }
    GUILayout.EndScrollView();
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
        return "-> a quest can fire on this today";
      case LabUsability.ProducesEventNoTrigger:
        return "-> emits an event, but no quest trigger matches it yet";
      default:
        return "-> lab only: nothing in the shipping mod hooks this yet";
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
    GUILayout.Label("What the world answers to  ·  [x] = this lab will show it to you");
    foreach (string seam in current.Seams) {
      GUILayout.Label("  " + seam);
    }

    GUILayout.Space(8f);
    GUILayout.Label("Bound " + LabPatching.AppliedCount + " of "
        + LabPatching.Outcomes.Count + " seams this build reached for.");
    foreach (LabPatching.Outcome outcome in LabPatching.Outcomes) {
      if (!outcome.Applied) {
        GUILayout.Label("  unavailable: " + outcome.Label + " — " + outcome.Detail);
      }
    }

    GUILayout.EndScrollView();
  }
}
