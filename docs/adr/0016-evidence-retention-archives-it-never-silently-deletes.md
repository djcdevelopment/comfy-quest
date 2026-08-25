# 0016 — Evidence retention archives; it never silently deletes

Status: accepted 2026-08-25.

## Context

`NFR-OBS-001` reads, verbatim: *"Receipt and evidence stores have explicit size/age retention
with archive/export before deletion. Reads stay bounded, and pruning one run cannot break
another run's audit chain."* Audit C3 found both halves violated, from opposite ends.

`RuntimeReceiptStore` had no prune, no delete and no cap at all, and `List()` enumerated and
sorted every file on each call — degrading over exactly the long campaign whose exit is
"multiple author-to-rerun cycles".

Run-control receipts had the opposite problem. `receiptDirectory` was a flat
`<root>/receipts/run-control` keyed by `request_id`, and `PruneReceipts(128)` ordered **all**
of it by write time and deleted the tail. Pruning was global across runs: a burst of resets on
one run silently evicted an older run's receipts. The registry meanwhile keeps up to 512 runs
with `PredecessorRunId` / `ResetId` lineage, so a long campaign *guaranteed* runs whose reset
receipts were gone while their lineage still pointed at them.

The severity here is about evidence integrity, not storage cost. Receipts are not logs — they
are read back as authority. `QuestStudioService` builds a `RuntimeReceiptStore` over the live
install and feeds `List(50)` into the Observe surface; `FR-EVID-002` makes "one genuine Runtime
receipt" *the* proof of a live-behaviour claim; and the established proof idiom is a
**correlated receipt set**, not a single file. Evict one member and the proof is incomplete,
with nothing signalling that anything was lost.

## Decision

**Retention moves evidence. It does not rewrite it, and it does not destroy it quietly.**

1. **Archiving moves the exact bytes.** A store read back as authority may not change the shape
   of its evidence at a retention boundary. Archived receipts are the same files under
   `receipts/archive/`, enumerable through `ListArchived`. A correlated proof set that
   straddles the boundary is still complete, just in two directories. Compaction into a
   rewritten digest was rejected for this reason.
2. **Retention is scoped to what it prunes.** Run-control receipts are partitioned by run, and
   retention is a fact about one partition that cannot reach into another. The cap is
   `MaxPerScope` per run rather than a shared total. Operations that address the activated pack
   rather than a run — currently `select_experience` — share one bounded `pack` partition.
3. **The one place evidence is destroyed writes a receipt saying so.** Past the archive bound,
   a `receipt_archive_evicted` / `run_control_archive_evicted` receipt names the count and the
   time range. A gap in a correlated proof set must be a fact a reader can find, never
   something inferred from absence.
4. **Every bound is a declared constant**, per `NFR-BOUND-001`, and retention runs on a bounded
   schedule rather than on every write, because receipts are written from the game loop.

The policy lives in Contracts, shared by both stores, so it is testable without the game.
Neither call site is.

## Consequences

- Disk use is bounded but larger than before: the archive is the price of never destroying
  evidence at the retention boundary.
- Readers of run-control receipts must know the scope. Studio always does — it is in the
  request it sent — and falls back to the flat path so an install already holding receipts
  keeps them.
- A scope key may not escape its directory. Run ids are `Safe()` identifiers and that policy
  admits `.` and `..`, which are safe as names and not as path segments; the layout refuses
  them rather than sanitising them into something surprising.
- This had to land before the `4A / guild-scale-runtime` evidence is collected. Otherwise the
  evidence that closes 4A is itself written to a store that can drop members of a correlated
  proof set.
- Loosening any of the four points — deleting instead of archiving, sharing a cap across runs,
  dropping the eviction notice, or making a bound implicit — requires superseding this record.
