namespace ComfyQuestRuntime;

using System;

/// <summary>Process-local proof that one Creator Session completed Runtime-driven world entry.
/// A request identity is not authoritative while entry is pending or after a new entry begins.</summary>
sealed class RuntimeCreatorSessionAuthority {
  string enteredSessionId;

  public string Current => enteredSessionId;

  public void BeginEntry() => enteredSessionId = null;

  public void ConfirmEntered(string creatorSessionId) {
    if (string.IsNullOrWhiteSpace(creatorSessionId))
      throw new ArgumentException("creator_session_id_required", nameof(creatorSessionId));
    enteredSessionId = creatorSessionId;
  }
}
