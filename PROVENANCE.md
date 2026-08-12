# Extraction provenance

VERIFIED extraction date: 2026-08-12.

- Source repository: https://github.com/djcdevelopment/baseline
- Immutable source tag: split-base-20260811
- Immutable source SHA: aceb2eb48d770885a2c4171b926867f4ee82b4a4
- History mechanism: git-filter-repo 2.47.0-compatible CLI, paths preserved
- Commit map: docs/provenance/commit-map.txt
- Full-history scan: gitleaks 8.30.1, 109 filtered commits, no leaks

The exact include paths were:

    network/mod/ComfyQuestLab
    network/mod/ComfyQuestLab.Tests
    network/mod/ComfyQuestRuntime
    network/mod/ComfyQuestContracts
    Lumberjacks/src/Quest.Studio
    tools/component-packets
    tools/questlab-doctor
    tools/questlab-events
    tools/questlab-grimoire
    tools/questlab-pacing
    tools/questlab-sheets
    tools/quest-packs
    tools/quest-bridge
    tools/quest-runtime
    tools/blueprints
    tools/i5/Invoke-I5QuestLabBatch.ps1
    tools/workbench/New-WorkbenchZip.ps1
    tools/workbench/Test-WorkbenchZipPrivacy.ps1
    tools/workbench/samples/quest-picker
    recipes/quest-catalogs
    tests/__init__.py
    tests/test_fallingwater_blueprint.py
    tests/test_gallery_profiles.py
    tests/test_i5_questlab_batch.py
    tests/test_quest_bridge.py
    tests/test_quest_capabilities.py
    tests/test_quest_packs.py
    tests/test_questlab_capture.py
    tests/test_questlab_doctor.py
    tests/test_questlab_events.py
    tests/test_questlab_grimoire.py
    tests/test_questlab_pacing.py
    tests/test_questlab_package.py
    tests/test_questlab_panel.py
    tests/test_questlab_render_inspector.py
    tests/test_questlab_sheets.py
    tests/test_questlab_truth_lens.py
    tests/test_verify_questlab_release.py
    tests/test_verify_questlab_truth.py
    tests/fixtures/quest-bridge/events-response.json
    recipes/quest-submission-bridge/bridge-consumer/mikers-demo/outbox/20260701-210000-slayer-rank-thrall-demo.json

The filter also applied this content callback during the same pass:

    blob.data = re.sub(br'sk_live_[A-Za-z0-9_]+', b'fixture-value', blob.data)

Disposition: one historical privacy-scanner self-test used a fake Stripe-shaped
value. It was a fixture, not a credential, and was rewritten throughout filtered
history. No real secret finding was observed. No blob exceeded 1 MB after filtering;
the largest retained object was a Quest Lab sample image under 100 KB packed.

Post-filter ordinary commits moved Studio to src/Quest.Studio, split Quest packaging,
made the i5 lane Quest-owned, and removed monorepo-only output paths.
