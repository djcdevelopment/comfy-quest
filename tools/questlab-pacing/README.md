# Quest Lab Pacing Clinic

The Pacing Clinic turns one or more live `all-schools` receipts into evidence about the
creator's first trip through the Lab:

```powershell
python tools\questlab-pacing\questlab_pacing.py `
  "C:\Program Files (x86)\Steam\steamapps\common\Valheim\BepInEx\config\comfy-quest-lab\receipts\suites" `
  --output captures\questlab\pacing.json
```

It reports each school's time to first witness/completion, gaps between required actions,
completion order, high-frequency canonical events, and local/RPC coalescing ratio. The default
heuristics flag a navigation hesitation after 60 seconds and a noisy required event after more
than five canonical actions; both thresholds are configurable.

The output deliberately omits player identity, targets, positions, signatures, and raw action
keys. It reads local receipts and writes one local JSON report—there is no telemetry or upload.

Use the report to answer concrete design questions: which station does a first-time tester fail
to find, whether an “obvious” path is actually obvious, and whether a trigger is so frequent that
it teaches the wrong lesson. It diagnoses pacing; it never changes quests or the Gallery.
