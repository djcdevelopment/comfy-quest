# Quest Lab Grimoire

Generate the Norse creator vocabulary from the shipping event catalog:

```powershell
python tools\questlab-grimoire\generate_grimoire.py
```

Outputs are `artifacts/questlab-grimoire.json` and `docs/questlab-grimoire.md`.
The generator reads `QuestEventCatalog.g.cs`, so event names and categories cannot
drift from the evaluator. The Markdown/JSON artifacts are local-first and can be
handed to the existing Quest Lab Sheets companion; no Google OAuth is required.
