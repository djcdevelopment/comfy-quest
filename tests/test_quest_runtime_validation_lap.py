from pathlib import Path
import unittest


ROOT = Path(__file__).resolve().parents[1]
HARNESS = ROOT / "tools" / "quest-runtime" / "Invoke-QuestRuntimeValidationLap.ps1"
SELF_TEST = ROOT / "tools" / "quest-runtime" / "Test-QuestRuntimeValidationLap.ps1"
RUNBOOK = ROOT / "docs" / "runbooks" / "I2-QUESTPACK-OMEN.md"


class QuestRuntimeValidationLapTests(unittest.TestCase):
    def test_state_changes_are_identity_and_sentinel_guarded(self) -> None:
        source = HARNESS.read_text(encoding="utf-8")
        for expected in (
            "Assert-RepoIdentity.ps1",
            ".comfy-quest-validation-lap",
            "comfy-quest-validation-lap-sentinel/v1",
            "Assert-SafeLayout",
            "Read-Context",
            "Assert-ValheimClosed",
        ):
            self.assertIn(expected, source)
        self.assertNotIn("rm -rf", source)
        self.assertNotIn("git reset", source)

    def test_line_endings_check_canonical_git_and_lap_worktree_surfaces(self) -> None:
        source = HARNESS.read_text(encoding="utf-8")
        self.assertIn("Canonical Git text is not LF", source)
        self.assertIn("Validation-lap source is not physical LF", source)
        self.assertIn("git ls-files --eol -- $lapFiles", source)
        self.assertNotIn("Where-Object { $_ -match 'w/(crlf|mixed)' }", source)

    def test_private_world_window_is_explicit_and_fails_closed(self) -> None:
        source = HARNESS.read_text(encoding="utf-8")
        self.assertIn("ArmPrivateWorld requires a machine-validated r1 first.", source)
        self.assertIn("Set-PrivateConfirmation $configPath $false", source)
        self.assertIn("Set-PrivateConfirmation $configPath $true", source)
        self.assertIn("PrivateWorldConfirmed remained true after cleanup.", source)
        self.assertLess(
            source.index("Assert-ValheimClosed 'private-world arming'"),
            source.index("Set-PrivateConfirmation $configPath $true"),
        )

    def test_exact_player_ritual_is_contract_gated(self) -> None:
        source = HARNESS.read_text(encoding="utf-8")
        for expected in (
            "The Woodbound Signal",
            "The charm wakes. Two offerings of wood, before the moment passes.",
            "The offering is heard. Reclaim one piece to seal the rite.",
            "The circuit closes. The charm remembers this telling.",
            "The circuit closes. The Charm remembers a new telling.",
            "Stage 2 must drop Wood twice within 30 seconds.",
            "Stage 3 must pick up Wood.",
            "normalized_experience_sha256",
        ):
            self.assertIn(expected, source)

    def test_monitor_separates_human_pacing_from_machine_timeout(self) -> None:
        source = HARNESS.read_text(encoding="utf-8")
        self.assertIn("waiting_for_player", source)
        self.assertIn("machine_timer_started=$false", source)
        self.assertIn("if (-not $ActionObserved)", source)
        self.assertIn("Machine receipt timeout", source)
        self.assertIn("do not repeat the player action", source)
        self.assertLess(source.index("if (-not $ActionObserved)"), source.index("Start-Sleep -Milliseconds 250"))

    def test_preflight_gate_output_cannot_pollute_the_json_receipt(self) -> None:
        source = HARNESS.read_text(encoding="utf-8")
        self.assertIn("$gateOutput = @(& $Command)", source)
        self.assertIn("$gateOutput | Out-Host", source)
        self.assertIn("$result | ConvertTo-Json -Depth 20", source)
        self.assertIn("the real process exit code remains decisive", source)

    def test_preflight_is_serialized_exactly_once_by_the_dispatcher(self) -> None:
        source = HARNESS.read_text(encoding="utf-8")
        preflight = source.split("function Invoke-Preflight", 1)[1].split(
            "function New-FileManifest", 1
        )[0]
        self.assertNotIn("ConvertTo-Json", preflight)
        self.assertEqual(source.count("$result | ConvertTo-Json -Depth 20"), 1)

    def test_studio_gates_use_hash_keyed_interim_package_cache(self) -> None:
        # The interim package version is fixed while its bytes evolve, so the Studio
        # gates must never trust NuGet's immutable-version global cache.
        source = HARNESS.read_text(encoding="utf-8")
        self.assertIn("packages-local\\Comfy.Quest.Contracts.0.6.0-local.nupkg", source)
        self.assertIn(".Hash.ToLowerInvariant().Substring(0, 16)", source)
        self.assertIn(
            "SetEnvironmentVariable('NUGET_PACKAGES', $studioGateCache, 'Process')", source
        )
        self.assertIn(
            "SetEnvironmentVariable('NUGET_PACKAGES', $previousNugetPackages, 'Process')",
            source,
        )
        # The override wraps exactly the two gates that consume the interim package
        # and is restored before the browser E2E gate manages its own cache.
        self.assertLess(
            source.index("SetEnvironmentVariable('NUGET_PACKAGES', $studioGateCache"),
            source.index("Invoke-CommandGate 'Studio build'"),
        )
        self.assertLess(
            source.index("Invoke-CommandGate 'Studio xUnit'"),
            source.index("SetEnvironmentVariable('NUGET_PACKAGES', $previousNugetPackages"),
        )
        self.assertLess(
            source.index("SetEnvironmentVariable('NUGET_PACKAGES', $previousNugetPackages"),
            source.index("Invoke-CommandGate 'Studio browser E2E'"),
        )

    def test_restore_surface_has_observed_need_tests(self) -> None:
        source = SELF_TEST.read_text(encoding="utf-8")
        for expected in (
            "running-game refusal",
            "quarantine",
            "state quarantine",
            "inbox-dev quarantine",
            "staging gate",
            "activation confirm",
            "strict JSON",
            "valid receipt object",
            "machine timeout",
            "shared live log",
            "interrupted cleanup",
            "byte-exact restore",
            "TestFaultAfter Quarantine",
        ):
            self.assertIn(expected, source)

    def test_prepare_quarantines_state_and_dev_lane_recoverably(self) -> None:
        # Phase 3 exit session 1: Prepare quarantined active/ and inbox/ but left state/
        # in place, so historical pending transitions replayed against the missing active
        # set; and the dev lane the defense publishes through was never swept at all.
        source = HARNESS.read_text(encoding="utf-8")
        self.assertIn("$stateRoot = Join-Path $runtimeRoot 'state'", source)
        self.assertIn("$inboxDevRoot = Join-Path $runtimeRoot 'inbox-dev'", source)
        self.assertIn("state_files=$state", source)
        self.assertIn("'inbox-dev'=$inboxDev", source)
        self.assertIn("quarantined_state_file_count=@($state).Count", source)
        self.assertIn("quarantined_inbox_dev_count=@($inboxDev).Count", source)
        self.assertIn("@('active','inbox','state','inbox-dev')", source)
        # Manifests persist before any move, and every move precedes the fault seam, so
        # an interrupted Prepare stays recoverable for the new kinds too.
        self.assertLess(
            source.index("state_files=$state"),
            source.index("foreach ($item in @($state)) { Move-OwnedFile"),
        )
        self.assertLess(
            source.index("foreach ($item in @($state)) { Move-OwnedFile"),
            source.index("if ($TestFaultAfter -eq 'Quarantine')"),
        )
        self.assertLess(
            source.index("foreach ($item in @($inboxDev)) { Move-OwnedFile"),
            source.index("if ($TestFaultAfter -eq 'Quarantine')"),
        )

    def test_second_revision_staging_waits_on_activation_evidence(self) -> None:
        # Phase 3 exit session 1 staged 1.0.1 before 1.0.0 was ever activated; the runtime
        # loaded the highest version directly and the first-load and update beats
        # collapsed into one press. The machine now carries the sequencing: r2 validation
        # refuses until the r1 activation is proven, and ConfirmActivation is the
        # read-only go/no-go the workbook cues before the second Publish.
        source = HARNESS.read_text(encoding="utf-8")
        self.assertIn("'ConfirmActivation' { Invoke-ConfirmActivation }", source)
        self.assertIn("r2 staging waits on r1 activation evidence", source)
        self.assertIn("Find-Expectation 'load_r1' $context $rows", source)
        self.assertIn("stage_second_revision=$false", source)
        self.assertIn("stage_second_revision=$true", source)
        self.assertIn("comfy-quest-validation-lap-confirm/v1", source)
        # The refusal sits inside the r2 branch, before the identity comparisons.
        self.assertLess(
            source.index("r2 staging waits on r1 activation evidence"),
            source.index("r2 changed the pack or experience identity."),
        )

    def test_strict_json_reader_does_not_enumerate_valid_objects(self) -> None:
        source = HARNESS.read_text(encoding="utf-8")
        self.assertIn("Write-Output -NoEnumerate $token", source)
        self.assertNotIn("return $token\n", source)

    def test_live_log_hash_allows_the_bepinex_writer(self) -> None:
        source = HARNESS.read_text(encoding="utf-8")
        self.assertIn("[IO.FileShare]::ReadWrite", source)
        self.assertNotIn("Get-FileHash -LiteralPath $Path", source)

    def test_harness_stays_local_and_does_not_grow_cross_system_reach(self) -> None:
        source = HARNESS.read_text(encoding="utf-8").lower()
        for forbidden in (
            "ssh ",
            "docker",
            "am4",
            "networksense",
            "companion",
            "gateway",
            "invoke-valheimserverruntimecontrol",
        ):
            self.assertNotIn(forbidden, source)

    def test_runbook_names_the_player_experience_and_proof_gates(self) -> None:
        # The runbook update is part of the same session: this pin keeps the tool from
        # becoming an infrastructure-only ritual in later edits.
        source = RUNBOOK.read_text(encoding="utf-8")
        self.assertIn("The Woodbound Signal", source)
        self.assertIn("player experience", source.lower())
        self.assertIn("ArmPrivateWorld", source)


if __name__ == "__main__":
    unittest.main()
