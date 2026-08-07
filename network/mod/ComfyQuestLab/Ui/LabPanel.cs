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

  Rect _window = new Rect(120f, 140f, 620f, 460f);
  Vector2 _scroll;
  Vector2 _journalScroll;
  string _journalCategory = LabCategory.Harvest;   // where a new builder starts
  string _filterText = string.Empty;
  bool _paused;
  Tab _tab = Tab.Console;

  enum Tab { Console, Journal }

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
      DrawJournal();
    }

    // Drag by the title bar, same as every other panel here.
    GUI.DragWindow(new Rect(0f, 0f, 10000f, 22f));
  }

  void DrawTabs() {
    GUILayout.BeginHorizontal();
    if (GUILayout.Toggle(_tab == Tab.Console, "Console", GUI.skin.button)) {
      _tab = Tab.Console;
    }
    if (GUILayout.Toggle(_tab == Tab.Journal, "Journal", GUI.skin.button)) {
      _tab = Tab.Journal;
    }
    GUILayout.FlexibleSpace();
    GUILayout.Label(_ring.Count + " held · " + _ring.TotalSeen + " seen");
    GUILayout.EndHorizontal();
  }

  void DrawConsole() {
    // --- filters -------------------------------------------------------------------
    GUILayout.BeginHorizontal();
    foreach (string category in LabCategory.All) {
      bool on = _visible.Contains(category);
      bool now = GUILayout.Toggle(on, category, GUI.skin.button);
      if (now != on) {
        if (now) {
          _visible.Add(category);
        } else {
          _visible.Remove(category);
        }
      }
    }
    GUILayout.EndHorizontal();

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
        GUILayout.Label(row.At + "  " + row.Category + "  " + row.Seam);
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
      return "Nothing yet. Hit a tree or a bush — harvest is the wired category in this build.";
    }
    return "Nothing in these categories. " + _ring.TotalSeen + " events have fired overall; widen the filter.";
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

  /// <summary>One page per category. Picking a page also turns that category on in the
  /// console, because the next thing anyone does after reading "punch a tree" is go and
  /// punch a tree, and finding the row filtered out would be a silly way to lose them.</summary>
  void DrawJournal() {
    GUILayout.BeginHorizontal();
    foreach (LabJournal.Page page in LabJournal.Pages) {
      bool selected = _journalCategory == page.Category;
      if (GUILayout.Toggle(selected, page.Title, GUI.skin.button) && !selected) {
        _journalCategory = page.Category;
        _visible.Add(page.Category);
      }
    }
    GUILayout.EndHorizontal();
    GUILayout.Space(6f);

    LabJournal.Page current = LabJournal.For(_journalCategory);
    _journalScroll = GUILayout.BeginScrollView(_journalScroll);

    GUILayout.Label(current.Title);
    GUILayout.Space(4f);
    foreach (string line in current.What) {
      GUILayout.Label(line);
    }

    GUILayout.Space(8f);
    GUILayout.Label("Try this");
    foreach (string line in current.Try) {
      GUILayout.Label("  " + line);
    }

    GUILayout.Space(8f);
    GUILayout.Label("Worth knowing");
    foreach (string line in current.Watch) {
      GUILayout.Label("  " + line);
    }

    GUILayout.Space(8f);
    GUILayout.Label("Seams in the game  ·  [x] = this lab shows it to you");
    foreach (string seam in current.Seams) {
      GUILayout.Label("  " + seam);
    }

    GUILayout.Space(8f);
    GUILayout.Label("This build hooked " + LabPatching.AppliedCount + " of "
        + LabPatching.Outcomes.Count + " seams it tried.");
    foreach (LabPatching.Outcome outcome in LabPatching.Outcomes) {
      if (!outcome.Applied) {
        GUILayout.Label("  unavailable: " + outcome.Label + " — " + outcome.Detail);
      }
    }

    GUILayout.EndScrollView();
  }
}
