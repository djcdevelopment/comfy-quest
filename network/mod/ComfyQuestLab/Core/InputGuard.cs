namespace ComfyQuestLab;

using System;

using UnityEngine;

/// <summary>Is the player typing into something that is not us?
///
/// Ported from the camera proof kit (comfy/handoffs/valheim-camera-proof/Plugin.cs:105),
/// which is the only place in either tree that solved this. It matters more here than it
/// did there, because the lab console has a filter text field and the retired panels
/// never had one.
///
/// The failure it prevents is not subtle and not rare: without it, typing "bush" into the
/// filter box moves the player forward, swings whatever is equipped, and closes the panel
/// on the "s". Every keystroke is also a hotkey unless something says otherwise.
///
/// Each probe is wrapped because a future game build can rename or remove any of these
/// types, and a missing type must degrade to "not typing" rather than throw on every
/// frame of Update().</summary>
public static class InputGuard {
  static bool _panelOwnsInput;
  static CursorLockMode _previousCursorLock;
  static bool _previousCursorVisible;

  /// <summary>True only while the interactive Lab window owns pointer and player input.
  /// This is deliberately stronger than "the cursor happens to be visible": ownership is
  /// an acquire/release lifecycle, so opening and closing cannot leave Valheim in a
  /// half-GUI, half-camera state.</summary>
  public static bool PanelOwnsInput { get { return _panelOwnsInput; } }

  public static void AcquirePanelInput() {
    if (!_panelOwnsInput) {
      _previousCursorLock = Cursor.lockState;
      _previousCursorVisible = Cursor.visible;
      _panelOwnsInput = true;
      ResetButtons();
    }
    MaintainPanelInput();
  }

  /// <summary>Valheim evaluates mouse capture every frame. Reassert ownership after its
  /// decision as well as from the panel's own Update/OnGUI path, making the pointer
  /// usable immediately without opening inventory as an accidental workaround.</summary>
  public static void MaintainPanelInput() {
    if (!_panelOwnsInput) {
      return;
    }
    try {
      Cursor.lockState = CursorLockMode.None;
      Cursor.visible = true;
    } catch (Exception) {
    }
  }

  public static void ReleasePanelInput() {
    if (!_panelOwnsInput) {
      TypingInLab = false;
      return;
    }
    _panelOwnsInput = false;
    TypingInLab = false;
    ResetButtons();
    try {
      Cursor.lockState = _previousCursorLock;
      Cursor.visible = _previousCursorVisible;
    } catch (Exception) {
    }
  }

  static void ResetButtons() {
    try {
      // Prevent a movement or attack button held during the ownership transition from
      // sticking for one frame on either side of the panel.
      ZInput.ResetAllButtonStates();
    } catch (Exception) {
    }
    try {
      Input.ResetInputAxes();
    } catch (Exception) {
    }
  }

  /// <summary>True when the game's own console, a text-input dialog, or chat has focus.
  /// Check this before acting on any hotkey.</summary>
  public static bool TypingInGame() {
    try {
      // global:: because System.Console is in scope and Valheim's Console is not
      // namespaced. The camera kit hit this too — it is not a style choice.
      if (global::Console.IsVisible()) {
        return true;
      }
    } catch (Exception) {
      // Type absent on this build; fall through and keep checking the others.
    }

    try {
      if (TextInput.IsVisible()) {
        return true;
      }
    } catch (Exception) {
    }

    try {
      if (Chat.instance != null && Chat.instance.HasFocus()) {
        return true;
      }
    } catch (Exception) {
    }

    return false;
  }

  /// <summary>The inverse the camera kit never needed: true when the LAB owns the
  /// keyboard, because its own text field has focus.
  ///
  /// IMGUI focus is a control name, not a state you can ask a widget for, so the panel
  /// names its field and sets this while that name is focused. Anything that reads a
  /// keystroke — the panel toggle, escape-to-close, any future shortcut — has to respect
  /// it or the filter box is unusable.</summary>
  public static bool TypingInLab { get; set; }

  /// <summary>The single question every hotkey should ask. Kept as one method so a new
  /// shortcut cannot accidentally check only half of it.</summary>
  public static bool ShouldIgnoreKeystrokes() {
    return TypingInLab || TypingInGame();
  }
}
