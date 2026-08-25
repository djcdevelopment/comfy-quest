# Quest Studio page conventions

The whole Quest Studio frontend is three C# raw-string constants — `Html`, `Css`, `Js` — in
`src/Quest.Studio/QuestStudioPage.cs`, served by `QuestStudioEndpoints`. Audit finding D3
records the consequence: 948 lines and 202 KB of string literals, asserted against by
text-matching Python tests. Guild-scale UI growth lands here, so these conventions are the
difference between an edit that works and one that silently breaks a test nobody reads.

## The rules that will bite you

**One line per JS function is load-bearing.** `tests/test_quest_studio_vnext.py` slices function
bodies with `script[idx : script.index("\n")]`. A reformatted or wrapped function silently
breaks those assertions — the page still renders, the test still runs, and it is asserting
against the wrong text. The same suite also enforces no duplicate HTML ids, that every `$('#id')`
in `Js` exists in `Html`, and that no mojibake bytes survive.

**The beat editor is parked and moved, not recreated.** A single `#beat-editor` node lives in
`#beat-editor-park` and is moved into the selected card on each render. **Park it before wiping
`#beat-list`**, or the node is destroyed with the list.

**The advanced-tools drawer stays `<details id="tools-menu">`.** The E2E suite clicks
`#tools-menu summary`; changing the element changes the selector.

**The picker's research divider is dormant, not dead.** It is hidden while all 34 creator events
are production, and reappears if a catalog demotes any. Removing it removes the affordance for a
state the catalog can still enter.

## Deliberate deviations from the design

The 2026-08-18 redesign implements the "Quest Studio" design canvas — breadcrumb journey, inline
beat editor, advanced-tools drawer. Four departures are intentional and should not be "corrected"
back:

- **A quest library and version input**, because the reality is multi-project.
- **"More options" / "Afterward" disclosure** in the beat editor, holding effects, rewards, and
  specific fields — the attenuation ladder, not a simplification.
- **Manual rehearsal tools** kept visible.
- **No "Simulate receipt" button.** The v2 routes keep game mutation out of the browser, and a
  button that fabricates a receipt would put it back.

## Visual proof when the in-app browser pane will not display

Screenshots time out and CSS transitions freeze. Drive the page with the Playwright already
bundled in the E2E build instead of installing another one: the Node binary under
`src/Quest.Studio.E2E.Tests/bin/Release/net9.0/.playwright/node/win32_x64/` can
`require` that build's `.playwright/package` and launch the pinned Chromium.

## Related

`docs/working-agreements.md` for the practices these sit under, and `../README.md` under *Local
verification* for the hash-keyed package cache Studio's tests need.
