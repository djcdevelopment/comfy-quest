# Creator Loop design tokens — translation spec

Source of truth: `docs/design/creator-loop.dc.html`, vendored 2026-08-19 from the Claude
Design project "Comfy Quest — Creator Loop" (generated against the brief built on
`docs/creator-loop-ux-baseline.md`). This file maps every token to both renderers so the
implementation is mechanical. The design's own constraint, kept verbatim: *"Every state
reads with flat fills, 1 px borders, and type weight alone — the full set renders
identically in Unity IMGUI. No gradient or shadow ever carries meaning."*

## Palette

| Token | Hex | UnityEngine.Color | Role |
| --- | --- | --- | --- |
| Ground | `#050810` | `(.020f,.031f,.063f)` | page/window background |
| Panel | `#0B1120` | `(.043f,.067f,.125f)` | card background |
| Panel deep | `#080D18` | `(.031f,.051f,.094f)` | detail/inset background |
| Border | `#1D2A40` | `(.114f,.165f,.251f)` | 1 px card border |
| Border light | `#2A3854` | `(.165f,.220f,.329f)` | chip/idle-step border |
| Ivory | `#E9E4D8` | `(.914f,.894f,.847f)` | body text |
| Title ivory | `#EFE9DC` | `(.937f,.914f,.863f)` | quest titles |
| Muted | `#9AA3B5` | `(.604f,.639f,.710f)` | secondary text |
| Dim | `#6B7488` | `(.420f,.455f,.533f)` | plumbing rows, labels |
| Faint | `#4E5870` | `(.306f,.345f,.439f)` | timestamps, overlines |
| Amber | `#E9A83F` | `(.914f,.659f,.247f)` | THE action that changes what's running |
| Amber bright | `#F4C061` | `(.957f,.753f,.380f)` | amber hover/border/ready-state text |
| Amber ink | `#191006` | `(.098f,.063f,.024f)` | text ON amber fills |
| Warning text | `#E8C27A` | `(.910f,.761f,.478f)` | warning feed rows |
| Ready | `#7EC482` | `(.494f,.769f,.510f)` | live/confirmed accents |
| Ready bg | `#12291A` | `(.071f,.161f,.102f)` | done-step fill |
| Ready border | `#3E6B47` | `(.243f,.420f,.278f)` | done-step border/connector |
| Ready text | `#9BD49F` | `(.608f,.831f,.624f)` | "Now playing" line |
| Steel | `#7FA8CC` | `(.498f,.659f,.800f)` | informational/safe/repeatable |
| Steel bg | `#16283C` | `(.086f,.157f,.235f)` | check-button fill |
| Steel border | `#35516E` | `(.208f,.318f,.431f)` | check-button border |
| Steel text | `#A9C8E4` | `(.663f,.784f,.894f)` | check-button label |
| CAST | `#A15CFF` | `(.631f,.361f,1f)` | the signature charm moment — only tinted row |
| CAST text | `#CFA9FF` | `(.812f,.663f,1f)` | CAST feed-row text |
| Banner calm bg | `#0A0F1C` | `(.039f,.059f,.110f)` | countdown pill, calm |
| Banner calm border | `#6E5322` | `(.431f,.325f,.133f)` | countdown pill border, calm |
| Urgent bg | `#8F1616` | `(.561f,.086f,.086f)` | countdown pill, ≤ 5 s (white text) |
| Urgent border | `#C24040` | `(.761f,.251f,.251f)` | countdown pill border, urgent |

## Mapping onto the drawer's existing styles (`ComfyQuestRuntime.EnsureStyles`)

| Existing texture/style | Today | Becomes |
| --- | --- | --- |
| `windowBackground` | `(.02,.03,.05)` | Ground — effectively unchanged |
| `rowBackground` | `(.035,.05,.075)` | Panel |
| `helpBackground` | `(.065,.085,.12)` | Panel deep |
| `headerBackground` | `(.10,.16,.24)` | Steel bg |
| `greenBackground` / `greenGlowBackground` | `(.10,.36,.23)` / `(.18,.62,.36)` | Ready bg / Ready |
| `blueBackground` | `(.12,.30,.50)` | Steel bg (label → Steel text) |
| `amberBackground` | `(.48,.32,.10)` + white text | **inverted:** Amber fill + Amber-ink text — the biggest visible change; the primary action becomes the brightest object in the drawer |
| `dimBackground` | `(.16,.19,.23)` | Border (as fill) with Dim text |
| `deadlineBackground` | `(.05,.07,.10,.86)` | Banner calm bg (border via a 1 px trick or accept flat) |
| `deadlineStyle` text | `(1,.86,.52)` | Amber bright |
| `deadlineUrgentBackground` | `(.42,.14,.10,.92)` | Urgent bg — redder and purer, white text unchanged |

## Typography

Three faces in the design; both renderers degrade deliberately — **Studio is 100%
offline and loads no font over the network.** The design faces lead the CSS stacks and
resolve only if locally installed, exactly the pattern the page always used for `Inter`:

- **UI — `--sans:'Source Sans 3',Inter,ui-sans-serif,system-ui,sans-serif`.** IMGUI:
  the Unity default face; the 400/600/700 ladder becomes regular/bold + size steps.
- **Data — `--mono:'JetBrains Mono',ui-monospace,'Cascadia Mono',Consolas,monospace`.**
  Hashes, timestamps, hotkeys, versions. IMGUI: optionally
  `Font.CreateDynamicFontFromOSFont("Consolas", size)` — or the default face; the *rule*
  that data never dresses as prose still holds through size and color (Faint/Dim).
- **Display — `--serif:Grenze,Georgia,'Times New Roman',serif`, quest titles only.**
  IMGUI: title = largest bold size in the drawer; the face does not translate and
  nothing else earns display treatment, so hierarchy survives.

One Studio-specific token adjustment: `--dim` is `#7d87a0`, not the design's `#6b7488`,
because the design value fails Studio's pinned WCAG 4.5:1 contrast matrix on the new
surfaces; the drawer keeps the design Dim for its plumbing rows, whose small size is not
under the same pin.

## Component semantics worth keeping exactly

- **Status card**: title + version chip + one state line; PACK / HASH / ACTIVATED behind
  a "Details" disclosure. State-line colors: Ready text (playing), Amber bright (ready —
  "1.2.0 is ready — play it"), Steel text (choice — "2 quests ready — choose").
- **Ladder**: done = Ready-bg circle with ✓, current = solid Amber circle, waiting =
  hollow Border-light circle; connectors take the color of the completed side. One
  context button below; the hotkey renders as a keycap chip *inside* the button.
- **Color grammar**: **Amber = changes what's running** (exactly one amber action visible
  at a time); **Steel = safe/repeatable**; **Ready green = confirmed state, never a
  button**; **CAST purple appears only for the charm moment**.
- **Banner**: pill, count + seconds only, `1/2, 6 seconds remaining`; urgent swaps fill
  only — same size, same position (matches the shipped `DeadlineUrgent()` fact and the
  ≤ 5 s threshold).
- **Evidence feed taxonomy** — four row kinds; implementation mapping for the drawer:

| Kind | Mark | Treatment | Maps to |
| --- | --- | --- | --- |
| Story | ◆ amber | Ivory, semibold, full size | `Matched …` / `Advanced …` engine evidence |
| CAST | ✦ purple | CAST text, the only tinted row | CHECK/CAST capture outcomes |
| Warning | ▲ amber | Warning text, regular weight, always says what to do next | orphaned bindings, rejected loads/dev revisions |
| Plumbing | · faint | Dim, smaller, never bold | check results, snapshots, reloads, status |

## Deliberately not adopted (yet)

- Rounded corners and the pill shape: IMGUI flat textures render square; the tokens carry
  the meaning, the radius does not. Studio (CSS) takes the radii as drawn.
- The design shows the *drawer of the future* (status card replacing the READY/CHECK
  charm strip's neighborhood). This spec only pins tokens and semantics; restructuring
  the drawer's section order is its own slice with its own pins.
