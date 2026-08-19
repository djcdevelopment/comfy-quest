# Slice 1.4 manual acceptance: Studio to OMEN Runtime

This is the local, private-world acceptance for the five-intent program's identity
and evidence spine:

`Studio authoring -> certified questpack -> F10 Check -> F11 Load -> CAST -> play -> receipts -> r2 orphan`

It does not deploy to AM4 or i5, exercise Companion or Gateway, or prove multiplayer
behavior. The repository-owned standalone Studio binds only to loopback at
`http://127.0.0.1:8085/quest-studio`.

## Intended player experience

**The Woodbound Signal** is a short ritual, not a test checklist. Speaking wakes the
Charm. Offering two pieces of Wood within a generous 30-second window should create
momentum without demanding dexterity. Reclaiming Wood seals the rite. Each message
should arrive at the moment its action gains meaning; the drawer supports that story
but should not be required to understand it. When r2 makes the sign say **OTHER
VERSION**, observe whether that reads as "this inscription belongs to an earlier
telling" or merely as maintainer language. Record both satisfying and confusing
moments in the validation backlog.

## Proof boundary

Use `tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1`. It owns backups,
payload deployment, inbox isolation, strict JSON parsing, receipt coaching, and
restoration. Studio remains the only authoring surface; Runtime remains the only
game-mutation surface.

The harness never times out human pacing. `Monitor` starts its short machine timer
only with `-ActionObserved`, after the player reports completing the requested action.
If an expected receipt does not arrive, stop and diagnose from preserved evidence.
Do not repeat the player action speculatively.

Choose one run ID and reuse it for the whole lap:

```powershell
$lap = @{
  RunId = 'woodbound-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')
  ValheimRoot = 'C:\Program Files (x86)\Steam\steamapps\common\Valheim'
}
```

All harness state is beneath ignored `captures/five-intent-slice1/<run-id>/` and is
sentinel guarded. Raw receipts, paths, and local identities stay there; only sanitized
findings belong in Git.

## 1. Prove and prepare before involving the player

Valheim must be closed. Run the full read-only preflight from a clean tracked commit:

```powershell
tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Preflight @lap
```

Require `verdict=ready_for_prepare`. The preflight runs repository identity,
boundary, build, xUnit, Python, generator-drift, Studio browser E2E, licensed Runtime
compile, harness self-test, release, and full-history secret gates. It also reports
whether installed DLL hashes differ; mismatch is expected to be corrected by
`Prepare`, never by hand.

Prepare the bounded machine window:

```powershell
tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Prepare @lap
```

Require `verdict=prepared`, `private_world_confirmed=false`, verified deployment
hashes, and counts for quarantined inbox and active files. Prepare moves every prior
questpack and active-state file into the run-owned quarantine rather than deleting
anything. It snapshots the installed Runtime, Contracts, Newtonsoft, and Runtime
configuration before deploying the exact tested payload.

If Prepare fails at any point, its automatic recovery must report a restored state.
Do not open Studio or Valheim until the cause is understood.

## 2. Author and prove r1 with Valheim closed

Start the standalone Studio:

```powershell
tools/quest-studio/Start-QuestStudio.ps1
```

Open `http://127.0.0.1:8085/quest-studio` and create a **Blank local quest**. Author
exactly these three beats:

1. Title: **The Woodbound Signal**. Keep the first beat as normal chat and set its
   message to `The charm wakes. Two offerings of wood, before the moment passes.`
2. Choose **Browse actions**, select **Item dropped**, and use **Add to quest**. Set
   repeat to `2`, which reveals the `within` field, then
   enter `30` seconds. Open **More options → Make this action specific** and set the
   target to `Wood`. Set the message to
   `The offering is heard. Reclaim one piece to seal the rite.`
3. Choose **Browse actions**, select **Item picked up**, and use **Add to quest**. Open
   **More options → Make this action specific**, set the target to `Wood`, and set the message to
   `The circuit closes. The charm remembers this telling.`

Run guided rehearsal. It must reach Complete and show the drop beat at `1/2` before
`2/2`. Publish version `1.0.0`, then stop touching the draft while its exact package
is proved:

```powershell
tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action ValidateRevision -ExpectedVersion 1.0.0 @lap
```

Require `verdict=valid`. This calls the shipping `QuestPackStore` contract, strictly
parses the compiled JSON, and checks all three events, targets, messages, ordering,
count, 30-second window, terminal outcome, filename, content hash, and package hash.
A failure returns to offline diagnosis; it is not a request to click Publish again.

## 3. Arm, enter, activate, and CAST

Confirm verbally that the named private solo world is the next world to be opened.
With Valheim still closed, arm the bounded safety window:

```powershell
tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action ArmPrivateWorld @lap
```

`ArmPrivateWorld` revalidates r1, rechecks that Valheim is closed, then—and only
then—sets `PrivateWorldConfirmed=true`. Require `verdict=armed` before launching.

The player sequence has no omitted steps:

1. Launch Valheim and enter the explicitly confirmed private solo world. Do not press
   F10 yet. After the world renders, prove the fresh Runtime startup log:

   ```powershell
   tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Monitor -Expectation startup -ActionObserved @lap
   ```

2. Press **F10** once. Prove an accepted check for exact r1 with no activation change:

   ```powershell
   tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Monitor -Expectation check_r1 -ActionObserved @lap
   ```

3. Press **F11** once. Prove exact r1 and a valid fresh activation epoch:

   ```powershell
   tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Monitor -Expectation load_r1 -ActionObserved @lap
   ```

4. Open **F9**, aim at a locally owned supported sign, and press backtick once for
   **CHECK**. Require the drawer to say **READY**, then prove its receipt:

   ```powershell
   tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Monitor -Expectation charm_ready -ActionObserved @lap
   ```

5. With the captured target still selected, press backtick once for **CAST**. Do not
   double-press or re-aim. Require the inscription response, then prove the bind:

   ```powershell
   tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Monitor -Expectation bind_r1 -ActionObserved @lap
   ```

6. Before playing, confirm Arcane Sight labels the sign with r1's current activation
   suffix and binding identity. If the sign is not current, stop before emitting an
   event.

## 4. Play one proven beat at a time

Close F9 before each gameplay action and reopen it only after the receipt proof to
inspect RECENT RUNTIME EVIDENCE. This prevents the eight-line ring from hiding an
earlier observation.

1. Send one normal chat message. Require the wake message, then:

   ```powershell
   tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Monitor -Expectation chat_advance -ActionObserved @lap
   ```

2. Drop exactly one Wood. Require `1/2` with no advance, then:

   ```powershell
   tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Monitor -Expectation drop_partial -ActionObserved @lap
   ```

3. Drop the second Wood within 30 seconds. Require the reclaim message, then:

   ```powershell
   tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Monitor -Expectation drop_advance -ActionObserved @lap
   ```

4. Pick up Wood. Require the closing message and completion, then:

   ```powershell
   tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Monitor -Expectation pickup_complete -ActionObserved @lap
   ```

Gameplay event, action, and transition receipts must agree on activation and the
event-scoped `evt-…` correlation ID. Check, load, bind, and orphan receipts are
control-plane receipts; they do not invent gameplay correlations.

## 5. Publish r2 and observe the orphan

Leave Valheim in the private world. In Studio choose **Start new iteration**, producing
version `1.0.1`. Change only the final message to
`The circuit closes. The Charm remembers a new telling.` The cockpit should say the
active r1 differs from this draft. Rehearse and publish, then validate while Valheim
remains untouched:

```powershell
tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action ValidateRevision -ExpectedVersion 1.0.1 @lap
```

The validator normalizes only the final message and proves every other experience
byte is semantically unchanged. It also requires a different content hash.

Press F10 once and prove `check_r2`; press F11 once and prove `load_r2`:

```powershell
tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Monitor -Expectation check_r2 -ActionObserved @lap
tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Monitor -Expectation load_r2 -ActionObserved @lap
```

The r2 activation ID must differ from r1. Open F9 without re-CASTing so Runtime loads
the new active identity, then prove the orphan notice:

```powershell
tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Monitor -Expectation orphan_r2 -ActionObserved @lap
```

Require `candidate_count >= 1`, not exactly one: other loaded stale bindings may
legitimately exist. The newly bound sign must change to **OTHER VERSION** without a
new CAST.

At player altitude, record:

- whether the three beats read as one coherent ritual;
- whether the messages arrived at meaningful moments;
- whether 30 seconds felt tense, generous, or procedural;
- whether the drawer explained rather than distracted;
- what **OTHER VERSION** meant without maintainer explanation.

## 6. Close and restore

Close Valheim completely before restoration. Stop Studio separately, then run:

```powershell
tools/quest-runtime/Invoke-QuestRuntimeValidationLap.ps1 -Action Cleanup @lap
```

Require `verdict=restored` and `safety_private_world_confirmed=false`. Cleanup moves
lap-only active and inbox files into the capture, restores the original active state,
historical packs, and installed DLLs by recorded SHA-256, preserves new receipts, and
restores unrelated configuration while deliberately leaving private-world safety
disabled.

The committed addendum records sanitized contract results, stage evidence,
activation rotation, orphan count, player-experience observations, and every "can't
answer why" moment. If none occurred, say so explicitly rather than inventing one.
