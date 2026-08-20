# Your tools are already telling you what they can't prove. Read them.

*Paste-ready for LinkedIn. Written after a bad day that turned out to be a useful one.*

---

I lost most of a day this week to a bug that my own software had been printing on screen,
in plain English, for a fortnight.

I build a quest-authoring system for Valheim — a modding stack with a browser studio, an
in-game runtime, and a shared contract layer between them. Most of the building is done by
an AI agent working in the repo. The part that can't be automated is me: at some point a
human has to put on the headset, walk into the world, and answer questions a test can't —
does this countdown feel tense mid-fight, does this escalation read as escalation or as
punishment.

That human time is the expensive thing. Not because it's an hour. Because I'm building
about a dozen things in parallel, and sitting down to play-test one of them means unloading
the working context of the other eleven. The cost isn't the session. It's the switch.

This week I paid that cost three times in twenty-four hours, and all three sessions died in
the first two minutes.

## Three sessions, one shape

Session one: the script had me stage a software update before the previous version had ever
run, which collapsed the exact moment we'd convened to observe.

Session two: the script never told me to bind the quest to an object in the world. I stood
there talking to a quest that was structurally incapable of hearing me. We only found out
by reading a receipt.

Session three: the script had me cast a charm before loading the quest. Loading is what
creates the quest; casting fails without it, with an error that literally says
`active_set_missing`.

Notice what these have in common. Not one is a bug in the product. The product was fine
every time. All three are defects in the *handoff* — the step-by-step script I was handed
and told was ready. And each one is a hello-world-level ordering mistake, in a system that
otherwise runs two hundred and ninety automated checks before anything ships.

That gap is the whole story. We had elaborate verification pointed at the code and
literally none pointed at the sequence a human would follow. The code was gated by
machines; the choreography was gated by someone reading it over and saying it looked right.

## The part that stung

Somewhere around the third failure I said, out loud and not politely: *we literally have a
rehearse feature for this.*

We do. The studio can walk a quest end to end without the game running. And it doesn't just
walk it — it reports, on every single run, a field called `limitations`: a list of the
specific ways the rehearsal differs from real play. For the quest we'd been struggling
with, it printed this:

> Route held waits for staged objects to be cleared; rehearsal removes 8 of them on
> request, while play removes one when the object itself is gone.

That sentence is a precise description of the bug that had eaten the previous session — a
timing race where the game counts a kill before the world has finished removing the body.
We had spent a night root-causing it from logs. We had written an architecture decision
record about it, a retrospective lesson about it, and a workstream to fix it.

The feature had been saying it the whole time. On screen. In a card labelled "Coverage
limits."

## The actual lesson

It would be easy, and wrong, to conclude "AI agents are unreliable." The failure is more
interesting and much more human than that.

The pattern was: **write a document asserting what you believe, instead of opening the
artifact that already answers it.** Once we went looking, it had happened five times in a
single day. A method whose error message stated the precondition — read, then contradicted
in prose. An existing function that already did the scan we were about to build from
scratch. A correct version of the runbook, sitting in the same directory as the one written
from memory. A note-to-self written that morning containing the exact rule — never opened.

Every organisation I've worked in does this at scale. We write the wiki page instead of
reading the error. We hold the meeting instead of opening the dashboard. We produce a
process document that duplicates something the tooling already emits — and then we trust
the document more than the tool, because we wrote it.

Two things make it worse, not better, when you add AI to the mix. Agents are extremely good
at producing confident, well-structured prose about a system, which is exactly the artifact
you should trust least. And they're good at building elaborate self-consistent test
suites — code and tests sharing one author and therefore one blind spot. All green, all
measuring the same assumption.

## What we changed

None of it was "try harder next time."

The verification contract now covers choreography, not just code: a human-facing sequence
has to be *derived* from machine sources — a rehearsal run, the precondition chain in the
code, an existing proven procedure — and never written from memory.

There's now an automated check over every runbook enforcing the rule rather than the
instance. The previous fix had pinned the exact phrase that broke in session two, which is
precisely why it didn't notice that session three's script never mentioned the load step at
all. The new check knows the preconditions, refuses any document that reaches a step
without establishing what it needs, and — importantly — ships with a self-test built from
the three broken scripts we actually shipped. A checker that has never failed is not a
checker.

The human checklist shrank from thirteen items to three. The rule that produced that: the
session covers exactly what the rehearsal *declares it cannot prove*, and nothing else.
Everything the machine can establish, the machine establishes. That list isn't a judgement
call any more — the tool publishes it.

And we went hunting for other things the product knows and nobody reads. We found two
straight away: a diagnostics array fetched by the UI and silently dropped, and an itemised
list of exactly why a revision was rejected, written to disk and never shown — the user got
`pack_invalid` and had to go digging. Both now render.

## The bit worth stealing

If you build tools, make them declare their own limits. Ours reports a proof level, a
disclaimer, and a per-run list of what it faked. That is a genuinely great design
instinct — and it cost us nothing but a night's work because we didn't read it.

If you *use* tools — and if you're pointing an AI agent at real work, you use a lot of
them — then before you write the document, ask one question: **what already knows this?**
The error string. The rehearsal output. The diagnostic code. The existing procedure that
worked last time. Your own note from this morning.

The answer is usually sitting there in a field called `limitations`, on screen, in a card
you stopped looking at.

---

*Built with Claude Code, which wrote this article, wrote the bugs, and — to its credit —
wrote the check that would have caught them.*
