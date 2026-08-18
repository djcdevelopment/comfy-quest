#!/usr/bin/env python3
"""Generate Quest Lab's exact capability manifest and compiled seam catalog.

The assembly-derived atlas says what methods exist. quest-capability-rules.json says
what those methods mean to creators. This generator joins the two and refuses drift:
every atlas method must have exactly one policy, every overload becomes an exact
signature record, and duplicate atlas rows remain visible through AtlasRowCount.

  python tools/component-packets/generate_seam_catalog.py          # write outputs
  python tools/component-packets/generate_seam_catalog.py --check  # verify outputs

Reads:
  tools/component-packets/samples/valheim-event-atlas.json
  tools/component-packets/quest-capability-rules.json
  tools/component-packets/quest-event-authoring.json
Writes:
  tools/component-packets/samples/quest-capability-manifest.json
  network/mod/ComfyQuestLab/Core/LabSeamCatalog.g.cs
  network/mod/ComfyQuestContracts/CreatorEventCatalog.g.cs
  network/mod/ComfyQuestContracts/CreatorSignalCatalog.g.cs
  network/mod/ComfyQuestContracts/RuntimeProductionEventCatalog.g.cs
  network/mod/ComfyQuestContracts/RuntimeWitnessCatalog.g.cs
  network/mod/ComfyQuestContracts/ModGlue/QuestEventCatalog.g.cs
"""

from __future__ import annotations

import argparse
import json
import re
import sys
from collections import defaultdict
from pathlib import Path


HERE = Path(__file__).resolve().parent
REPO = HERE.parent.parent
ATLAS = HERE / "samples" / "valheim-event-atlas.json"
RULES = HERE / "quest-capability-rules.json"
AUTHORING = HERE / "quest-event-authoring.json"
MANIFEST = HERE / "samples" / "quest-capability-manifest.json"
CSHARP = REPO / "network" / "mod" / "ComfyQuestLab" / "Core" / "LabSeamCatalog.g.cs"
EVENT_CATALOG = (
    REPO
    / "network"
    / "mod"
    / "ComfyQuestContracts"
    / "ModGlue"
    / "QuestEventCatalog.g.cs"
)
SIGNAL_CATALOG = (
    REPO
    / "network"
    / "mod"
    / "ComfyQuestContracts"
    / "CreatorSignalCatalog.g.cs"
)
CREATOR_EVENT_CATALOG = (
    REPO
    / "network"
    / "mod"
    / "ComfyQuestContracts"
    / "CreatorEventCatalog.g.cs"
)
PRODUCTION_EVENT_CATALOG = (
    REPO
    / "network"
    / "mod"
    / "ComfyQuestContracts"
    / "RuntimeProductionEventCatalog.g.cs"
)
RUNTIME_WITNESS_CATALOG = (
    REPO
    / "network"
    / "mod"
    / "ComfyQuestContracts"
    / "RuntimeWitnessCatalog.g.cs"
)

CATEGORY_ORDER = (
    "combat",
    "harvest",
    "inventory",
    "building",
    "crafting",
    "progression",
    "world",
    "social",
)
CATEGORY_CONST = {
    "combat": "LabCategory.Combat",
    "harvest": "LabCategory.Harvest",
    "inventory": "LabCategory.Inventory",
    "building": "LabCategory.Building",
    "crafting": "LabCategory.Crafting",
    "progression": "LabCategory.Progression",
    "world": "LabCategory.World",
    "social": "LabCategory.Social",
}
USABILITY_CONST = {
    "today": "LabUsability.Today",
    "produces-event-no-trigger": "LabUsability.ProducesEventNoTrigger",
    "lab-candidate": "LabUsability.LabCandidate",
    "not-patchable": "LabUsability.LabCandidate",
}
USABILITY_RANK = {
    "not-patchable": 0,
    "lab-candidate": 1,
    "produces-event-no-trigger": 2,
    "today": 3,
}
ROUTES = {"primary", "alternate", "corroborating", "diagnostic", "suppressed"}
PROFILES = {"core", "extended", "diagnostic", "disabled"}
EVENT_NAME = re.compile(r"^[a-z][a-z0-9_]*$")
FIELD_NAME = re.compile(r"^[a-z][a-z0-9_]*$")
FAST_SIGNAL_FIELDS = {
    "Id", "Event", "Label", "Instruction", "Target", "TargetPolicy", "Privacy",
    "RuntimeAdapter",
}
RULE_FIELDS = {
    "Methods",
    "Category",
    "Event",
    "Route",
    "Profile",
    "CreatorSafe",
    "Dedupe",
    "Actor",
    "Reason",
}
RUNTIME_EVENT_FIELDS = {
    "Event", "RuntimeAdapter", "EvidenceState", "EvidenceRevision", "TargetPolicy",
    "FixedTarget", "AllowedTargets",
    "EmitsWeaponSkill", "EmitsProjectile", "EmittedFields", "FixedWhere",
    "WitnessSignatures",
}
ENGINE_EVENT_FIELDS = {
    "Event", "Label", "Instruction", "RuntimeAdapter", "RequiredWhereFields",
    "AllowedWhereFields", "TargetPolicy", "FixedTarget", "AllowedTargets", "Privacy",
}
UNIVERSAL_WHERE_FIELDS = {"weapon_skill", "projectile", "actor_role"}


class CapabilityError(ValueError):
    """A human-authored rule or generated-input invariant is invalid."""


def read_json(path: Path) -> dict:
    with path.open(encoding="utf-8") as handle:
        return json.load(handle)


def parse_signature(row: dict) -> tuple[str, list[str], str]:
    """Return exact id, parameter types, and return type for an atlas row."""
    signature = row["Signature"]
    if "(" not in signature or not signature.endswith(")"):
        raise CapabilityError(f"malformed signature: {signature!r}")
    head, parameter_text = signature.split("(", 1)
    parameter_text = parameter_text[:-1]
    try:
        return_type, method = head.rsplit(" ", 1)
    except ValueError as error:
        raise CapabilityError(f"malformed signature head: {signature!r}") from error
    if method != row["Method"]:
        raise CapabilityError(
            f"signature method {method!r} disagrees with atlas Method {row['Method']!r}"
        )
    parameters = [part.strip() for part in parameter_text.split(",") if part.strip()]
    signature_id = f"{row['Id']}({', '.join(parameters)})"
    return signature_id, parameters, return_type


def expand_rules(rules_document: dict) -> dict[str, dict]:
    method_rules: dict[str, dict] = {}
    for number, group in enumerate(rules_document.get("Rules", []), start=1):
        unknown = set(group) - RULE_FIELDS
        missing = RULE_FIELDS - set(group)
        if unknown or missing:
            raise CapabilityError(
                f"rule {number} fields: missing={sorted(missing)}, unknown={sorted(unknown)}"
            )
        methods = group["Methods"]
        if not isinstance(methods, list) or not methods:
            raise CapabilityError(f"rule {number} must name at least one method")
        policy = {key: value for key, value in group.items() if key != "Methods"}
        for method_id in methods:
            if method_id in method_rules:
                raise CapabilityError(f"duplicate policy for {method_id}")
            method_rules[method_id] = dict(policy)
    return method_rules


def validate_policy(method_id: str, policy: dict, atlas_categories: set[str]) -> None:
    category = policy["Category"]
    event = policy["Event"]
    route = policy["Route"]
    profile = policy["Profile"]
    safe = policy["CreatorSafe"]

    if category not in CATEGORY_ORDER:
        raise CapabilityError(f"{method_id}: unknown category {category!r}")
    if category not in atlas_categories:
        raise CapabilityError(
            f"{method_id}: canonical category {category!r} not in atlas {sorted(atlas_categories)}"
        )
    if not isinstance(event, str) or not EVENT_NAME.fullmatch(event):
        raise CapabilityError(f"{method_id}: invalid canonical event {event!r}")
    if route not in ROUTES:
        raise CapabilityError(f"{method_id}: invalid route {route!r}")
    if profile not in PROFILES:
        raise CapabilityError(f"{method_id}: invalid profile {profile!r}")
    if not isinstance(safe, bool):
        raise CapabilityError(f"{method_id}: CreatorSafe must be boolean")
    if safe and profile not in {"core", "extended"}:
        raise CapabilityError(f"{method_id}: safe event cannot use {profile!r} profile")
    if not safe and profile == "core":
        raise CapabilityError(f"{method_id}: unsafe event cannot use the core profile")
    for field in ("Dedupe", "Actor", "Reason"):
        if not isinstance(policy[field], str) or not policy[field].strip():
            raise CapabilityError(f"{method_id}: {field} must be a non-empty string")


def build_model() -> tuple[dict, dict, dict[str, dict], list[dict]]:
    atlas = read_json(ATLAS)
    rules_document = read_json(RULES)
    expected = rules_document.get("Expected", {})
    rows = atlas.get("Seams", [])
    method_rules = expand_rules(rules_document)

    if atlas.get("SeamCount") != len(rows):
        raise CapabilityError(
            f"atlas SeamCount={atlas.get('SeamCount')} but contains {len(rows)} rows"
        )

    methods: dict[str, list[dict]] = defaultdict(list)
    signatures: dict[str, dict] = {}
    signature_order: list[str] = []
    for atlas_index, row in enumerate(rows):
        signature_id, parameters, return_type = parse_signature(row)
        methods[row["Id"]].append(row)
        if signature_id not in signatures:
            signatures[signature_id] = {
                "SignatureId": signature_id,
                "MethodId": row["Id"],
                "DeclaringType": row["DeclaringType"],
                "Method": row["Method"],
                "ReturnType": return_type,
                "Parameters": parameters,
                "AtlasCategories": [],
                "AtlasRowCount": 0,
                "AtlasRows": [],
                "Patchable": bool(row["Patchable"]),
                "AtlasQuestUsability": row["QuestUsable"],
            }
            signature_order.append(signature_id)
        entry = signatures[signature_id]
        immutable = {
            "MethodId": row["Id"],
            "DeclaringType": row["DeclaringType"],
            "Method": row["Method"],
            "ReturnType": return_type,
            "Parameters": parameters,
            "Patchable": bool(row["Patchable"]),
        }
        for field, value in immutable.items():
            if entry[field] != value:
                raise CapabilityError(
                    f"duplicate {signature_id} disagrees on {field}: {entry[field]!r} != {value!r}"
                )
        if row["Category"] not in entry["AtlasCategories"]:
            entry["AtlasCategories"].append(row["Category"])
        entry["AtlasRowCount"] += 1
        entry["AtlasRows"].append(atlas_index)
        if USABILITY_RANK[row["QuestUsable"]] > USABILITY_RANK[entry["AtlasQuestUsability"]]:
            entry["AtlasQuestUsability"] = row["QuestUsable"]

    actual_counts = {
        "AtlasRows": len(rows),
        "UniqueSignatures": len(signatures),
        "UniqueMethods": len(methods),
    }
    if actual_counts != expected:
        raise CapabilityError(f"atlas cardinality drift: expected {expected}, got {actual_counts}")

    missing = set(methods) - set(method_rules)
    extra = set(method_rules) - set(methods)
    if missing or extra:
        raise CapabilityError(
            f"capability policy drift: missing={sorted(missing)}, extra={sorted(extra)}"
        )

    for method_id, policy in method_rules.items():
        validate_policy(method_id, policy, {row["Category"] for row in methods[method_id]})

    output_signatures: list[dict] = []
    for signature_id in signature_order:
        entry = signatures[signature_id]
        policy = method_rules[entry["MethodId"]]
        entry.update(
            {
                "CanonicalCategory": policy["Category"],
                "CanonicalEvent": policy["Event"],
                "Route": policy["Route"],
                "Profile": policy["Profile"],
                "CreatorSafe": policy["CreatorSafe"],
                "DedupeGroup": policy["Dedupe"],
                "ActorScope": policy["Actor"],
                "Reason": policy["Reason"],
            }
        )
        output_signatures.append(entry)

    safe_routes: dict[str, set[str]] = defaultdict(set)
    for entry in output_signatures:
        if entry["CreatorSafe"]:
            safe_routes[entry["CanonicalEvent"]].add(entry["Route"])
    without_primary = sorted(
        event for event, routes in safe_routes.items() if "primary" not in routes
    )
    if without_primary:
        raise CapabilityError(
            "creator-safe events without a primary route: " + ", ".join(without_primary)
        )

    aliases = rules_document.get("TriggerAliases", {})
    if not isinstance(aliases, dict):
        raise CapabilityError("TriggerAliases must be an object")
    for alias, targets in aliases.items():
        if not EVENT_NAME.fullmatch(alias):
            raise CapabilityError(f"invalid trigger alias {alias!r}")
        if alias in safe_routes:
            raise CapabilityError(f"trigger alias {alias!r} collides with a canonical event")
        if not isinstance(targets, list) or not targets or len(targets) != len(set(targets)):
            raise CapabilityError(f"trigger alias {alias!r} must have unique canonical targets")
        unknown_targets = set(targets) - set(safe_routes)
        if unknown_targets:
            raise CapabilityError(
                f"trigger alias {alias!r} names unsafe/unknown events: {sorted(unknown_targets)}"
            )

    return atlas, rules_document, method_rules, output_signatures


def build_manifest(atlas: dict, rules_document: dict, signatures: list[dict]) -> dict:
    safe_events = sorted(
        {entry["CanonicalEvent"] for entry in signatures if entry["CreatorSafe"]}
    )
    all_events = sorted({entry["CanonicalEvent"] for entry in signatures})
    category_counts = {}
    for category in CATEGORY_ORDER:
        in_category = [entry for entry in signatures if entry["CanonicalCategory"] == category]
        category_counts[category] = {
            "UniqueSignatures": len(in_category),
            "AtlasRows": sum(entry["AtlasRowCount"] for entry in in_category),
            "CreatorSafeSignatures": sum(bool(entry["CreatorSafe"]) for entry in in_category),
            "CreatorEvents": len(
                {entry["CanonicalEvent"] for entry in in_category if entry["CreatorSafe"]}
            ),
        }
    authoring = build_authoring(safe_events)
    production, engine_events = build_runtime_production(
        rules_document, signatures, authoring
    )
    creator_events = enrich_creator_events(authoring, signatures, production)
    fast_signals = build_fast_signals(rules_document, signatures)
    return {
        "Schema": "comfy-quest-capabilities/v1",
        "SourceAtlas": "tools/component-packets/samples/valheim-event-atlas.json",
        "SourceRules": "tools/component-packets/quest-capability-rules.json",
        "SourceAuthoring": "tools/component-packets/quest-event-authoring.json",
        "AssemblySource": atlas["Source"],
        "Counts": {
            "AtlasRows": sum(entry["AtlasRowCount"] for entry in signatures),
            "UniqueSignatures": len(signatures),
            "UniqueMethods": len({entry["MethodId"] for entry in signatures}),
            "CanonicalEvents": len(all_events),
            "CreatorSafeEvents": len(safe_events),
            "CreatorSafeSignatures": sum(bool(entry["CreatorSafe"]) for entry in signatures),
        },
        "RuntimeCounts": {
            "ProductionEvents": len(production),
            "ProductionWitnesses": sum(
                len(entry["WitnessSignatures"]) for entry in production
            ),
            "EngineEvents": len(engine_events),
        },
        "CreatorSafeEvents": safe_events,
        "CreatorEvents": creator_events,
        "FastSignals": fast_signals,
        "RuntimeProductionEvents": production,
        "EngineEvents": engine_events,
        "TriggerAliases": rules_document.get("TriggerAliases", {}),
        "CategoryCounts": category_counts,
        "Signatures": signatures,
    }


def build_fast_signals(rules_document: dict, signatures: list[dict]) -> list[dict]:
    """Validate the narrow Studio/Runtime lane against the generated Grimoire policy."""
    rows = rules_document.get("FastSignals")
    if not isinstance(rows, list) or not rows:
        raise CapabilityError("FastSignals must be a non-empty array")
    primary = {
        entry["CanonicalEvent"]: entry
        for entry in signatures
        if entry["CreatorSafe"] and entry["Profile"] == "core"
        and entry["Route"] == "primary"
    }
    output: list[dict] = []
    ids: set[str] = set()
    event_targets: set[tuple[str, str | None]] = set()
    for index, row in enumerate(rows, start=1):
        if not isinstance(row, dict) or set(row) != FAST_SIGNAL_FIELDS:
            raise CapabilityError(
                f"fast signal {index} must contain exactly {sorted(FAST_SIGNAL_FIELDS)}"
            )
        signal_id = row["Id"]
        event = row["Event"]
        target = row["Target"]
        if not isinstance(signal_id, str) or not EVENT_NAME.fullmatch(signal_id):
            raise CapabilityError(f"fast signal {index} has invalid Id {signal_id!r}")
        if signal_id in ids:
            raise CapabilityError(f"duplicate fast signal id {signal_id}")
        ids.add(signal_id)
        if not isinstance(event, str) or not EVENT_NAME.fullmatch(event):
            raise CapabilityError(f"{signal_id}: invalid Event {event!r}")
        if target is not None and (not isinstance(target, str) or not target.strip()):
            raise CapabilityError(f"{signal_id}: Target must be null or non-empty")
        event_target = (event, target)
        if event_target in event_targets:
            raise CapabilityError(f"duplicate fast signal event/target {event_target}")
        event_targets.add(event_target)
        for field in ("Label", "Instruction", "Privacy", "RuntimeAdapter"):
            if not isinstance(row[field], str) or not row[field].strip():
                raise CapabilityError(f"{signal_id}: {field} must be non-empty")
        if row["TargetPolicy"] not in {"fixed", "wildcard", "engine"}:
            raise CapabilityError(f"{signal_id}: invalid TargetPolicy")
        if row["TargetPolicy"] == "fixed" and target is None:
            raise CapabilityError(f"{signal_id}: fixed signals require Target")
        if row["TargetPolicy"] != "fixed" and target is not None:
            raise CapabilityError(f"{signal_id}: only fixed signals may declare Target")
        if event == "timer_elapsed":
            if row["TargetPolicy"] != "engine":
                raise CapabilityError("timer_elapsed fast signal must be engine-owned")
            lab_profile, lab_route = "engine", "engine"
        else:
            seam = primary.get(event)
            if seam is None:
                raise CapabilityError(
                    f"{signal_id}: {event} is not a core primary creator-safe Grimoire seam"
                )
            if row["TargetPolicy"] == "engine":
                raise CapabilityError(f"{signal_id}: Harmony signals cannot be engine-owned")
            lab_profile, lab_route = seam["Profile"], seam["Route"]
        output.append({**row, "LabProfile": lab_profile, "LabRoute": lab_route})
    return output


def build_authoring(safe_events: list[str]) -> list[dict]:
    """Validate the human creator vocabulary against the generated safe catalog.

    Patch policy decides whether an event is safe. This file only explains the stable
    envelope creators receive, so it must cover that policy exactly and may not invent
    an event the evaluator will reject.
    """
    document = read_json(AUTHORING)
    if document.get("Schema") != "comfy-quest-event-authoring/v1":
        raise CapabilityError("quest-event-authoring.json schema is not v1")
    rows = document.get("Events")
    if not isinstance(rows, list):
        raise CapabilityError("quest-event-authoring.json Events must be an array")

    by_name: dict[str, dict] = {}
    for index, row in enumerate(rows, start=1):
        if not isinstance(row, dict):
            raise CapabilityError(f"creator event {index} must be an object")
        expected_fields = {
            "Name", "TargetKind", "TargetDescription", "ExampleTarget",
            "SupportsWeaponSkill", "SupportsProjectile", "Fields",
        }
        unknown = set(row) - expected_fields
        missing = expected_fields - set(row)
        if unknown or missing:
            raise CapabilityError(
                f"creator event {index} fields: missing={sorted(missing)}, "
                f"unknown={sorted(unknown)}"
            )
        name = row["Name"]
        if not isinstance(name, str) or not EVENT_NAME.fullmatch(name):
            raise CapabilityError(f"creator event {index} has invalid Name {name!r}")
        if name in by_name:
            raise CapabilityError(f"duplicate creator metadata for {name}")
        for field in ("TargetKind", "TargetDescription", "ExampleTarget"):
            if not isinstance(row[field], str) or not row[field].strip():
                raise CapabilityError(f"{name}: {field} must be a non-empty string")
        for field in ("SupportsWeaponSkill", "SupportsProjectile"):
            if not isinstance(row[field], bool):
                raise CapabilityError(f"{name}: {field} must be boolean")
        fields = row["Fields"]
        if not isinstance(fields, list):
            raise CapabilityError(f"{name}: Fields must be an array")
        seen_fields: set[str] = set()
        for field_index, field in enumerate(fields, start=1):
            required = {"Name", "Description", "Example", "DraftByDefault"}
            if not isinstance(field, dict) or set(field) != required:
                raise CapabilityError(
                    f"{name}: field {field_index} must contain exactly {sorted(required)}"
                )
            field_name = field["Name"]
            if not isinstance(field_name, str) or not FIELD_NAME.fullmatch(field_name):
                raise CapabilityError(f"{name}: invalid field name {field_name!r}")
            if field_name in seen_fields:
                raise CapabilityError(f"{name}: duplicate field {field_name}")
            seen_fields.add(field_name)
            if field_name in {"event", "target", "weapon_skill", "projectile"}:
                raise CapabilityError(
                    f"{name}: universal field {field_name} belongs in its typed metadata"
                )
            if not isinstance(field["Description"], str) or not field["Description"].strip():
                raise CapabilityError(f"{name}.{field_name}: Description is empty")
            if not isinstance(field["Example"], str) or not field["Example"].strip():
                raise CapabilityError(f"{name}.{field_name}: Example is empty")
            if not isinstance(field["DraftByDefault"], bool):
                raise CapabilityError(f"{name}.{field_name}: DraftByDefault must be boolean")
        by_name[name] = row

    missing = set(safe_events) - set(by_name)
    extra = set(by_name) - set(safe_events)
    if missing or extra:
        raise CapabilityError(
            f"creator authoring drift: missing={sorted(missing)}, extra={sorted(extra)}"
        )
    return [by_name[name] for name in safe_events]


def build_runtime_production(
    rules_document: dict, signatures: list[dict], authoring: list[dict]
) -> tuple[list[dict], list[dict]]:
    """Validate the shipping Runtime boundary separately from creator vocabulary."""
    document = rules_document.get("RuntimeProduction")
    if not isinstance(document, dict) or set(document) != {
        "EvidencePolicy", "Events", "EngineEvents"
    }:
        raise CapabilityError(
            "RuntimeProduction must contain EvidencePolicy, Events, and EngineEvents"
        )
    if not isinstance(document["EvidencePolicy"], str) or not document["EvidencePolicy"].strip():
        raise CapabilityError("RuntimeProduction EvidencePolicy must be non-empty")

    safe_by_signature = {
        row["SignatureId"]: row for row in signatures if row["CreatorSafe"]
    }
    authoring_by_name = {row["Name"]: row for row in authoring}
    output: list[dict] = []
    seen_events: set[str] = set()
    seen_witnesses: set[str] = set()
    for index, row in enumerate(document["Events"], start=1):
        if not isinstance(row, dict) or set(row) != RUNTIME_EVENT_FIELDS:
            raise CapabilityError(
                f"runtime production event {index} must contain exactly "
                f"{sorted(RUNTIME_EVENT_FIELDS)}"
            )
        event = row["Event"]
        if event not in authoring_by_name or event in seen_events:
            raise CapabilityError(f"runtime production event is unknown or duplicate: {event!r}")
        seen_events.add(event)
        for field in ("RuntimeAdapter", "EvidenceState"):
            if not isinstance(row[field], str) or not row[field].strip():
                raise CapabilityError(f"{event}: {field} must be non-empty")
        revision = row["EvidenceRevision"]
        if revision is not None and (
            not isinstance(revision, str) or not re.fullmatch(r"[0-9a-f]{40}", revision)
        ):
            raise CapabilityError(f"{event}: EvidenceRevision must be null or a commit SHA")
        if row["EvidenceState"] == "verified-live" and revision is None:
            raise CapabilityError(f"{event}: verified-live requires EvidenceRevision")
        if row["TargetPolicy"] not in {"optional", "closed", "fixed-output", "none"}:
            raise CapabilityError(f"{event}: invalid TargetPolicy")
        fixed_target = row["FixedTarget"]
        allowed_targets = row["AllowedTargets"]
        if fixed_target is not None and (
            not isinstance(fixed_target, str) or not fixed_target.strip()
        ):
            raise CapabilityError(f"{event}: FixedTarget must be null or non-empty")
        if (not isinstance(allowed_targets, list)
                or len(allowed_targets) != len(set(allowed_targets))
                or any(not isinstance(value, str) or not value.strip()
                       for value in allowed_targets)):
            raise CapabilityError(f"{event}: AllowedTargets must be unique non-empty strings")
        if row["TargetPolicy"] == "fixed-output" and fixed_target is None:
            raise CapabilityError(f"{event}: fixed-output requires FixedTarget")
        if row["TargetPolicy"] == "closed" and not allowed_targets:
            raise CapabilityError(f"{event}: closed target policy requires AllowedTargets")
        if row["TargetPolicy"] in {"optional", "none"} and (
            fixed_target is not None or allowed_targets
        ):
            raise CapabilityError(f"{event}: {row['TargetPolicy']} cannot constrain targets")
        for field in ("EmitsWeaponSkill", "EmitsProjectile"):
            if not isinstance(row[field], bool):
                raise CapabilityError(f"{event}: {field} must be boolean")
        emitted = row["EmittedFields"]
        known_fields = {field["Name"] for field in authoring_by_name[event]["Fields"]}
        if (not isinstance(emitted, list) or len(emitted) != len(set(emitted))
                or any(not FIELD_NAME.fullmatch(value or "") for value in emitted)):
            raise CapabilityError(f"{event}: EmittedFields must be unique field names")
        unknown_fields = set(emitted) - known_fields - UNIVERSAL_WHERE_FIELDS
        if unknown_fields:
            raise CapabilityError(f"{event}: unknown emitted fields {sorted(unknown_fields)}")
        fixed = row["FixedWhere"]
        if (not isinstance(fixed, dict)
                or any(key not in emitted or not isinstance(value, str) or not value.strip()
                       for key, value in fixed.items())):
            raise CapabilityError(f"{event}: FixedWhere must constrain emitted fields")
        witnesses = row["WitnessSignatures"]
        if not isinstance(witnesses, list) or not witnesses:
            raise CapabilityError(f"{event}: WitnessSignatures must be non-empty")
        for witness in witnesses:
            capability = safe_by_signature.get(witness)
            if capability is None or capability["CanonicalEvent"] != event:
                raise CapabilityError(f"{event}: invalid production witness {witness!r}")
            if witness in seen_witnesses:
                raise CapabilityError(f"production witness reused: {witness}")
            seen_witnesses.add(witness)
        allowed = list(emitted)
        if row["EmitsWeaponSkill"]:
            allowed.append("weapon_skill")
        if row["EmitsProjectile"]:
            allowed.append("projectile")
        output.append({**row, "AllowedWhereFields": allowed})
    if len(output) != 26:
        raise CapabilityError(f"Runtime production boundary drift: expected 26, got {len(output)}")

    engine_output: list[dict] = []
    seen_engine: set[str] = set()
    for index, row in enumerate(document["EngineEvents"], start=1):
        if not isinstance(row, dict) or set(row) != ENGINE_EVENT_FIELDS:
            raise CapabilityError(
                f"engine event {index} must contain exactly {sorted(ENGINE_EVENT_FIELDS)}"
            )
        event = row["Event"]
        if not isinstance(event, str) or not EVENT_NAME.fullmatch(event) or event in seen_engine:
            raise CapabilityError(f"invalid or duplicate engine event: {event!r}")
        seen_engine.add(event)
        for field in ("Label", "Instruction", "RuntimeAdapter", "Privacy"):
            if not isinstance(row[field], str) or not row[field].strip():
                raise CapabilityError(f"{event}: {field} must be non-empty")
        target_policy = row["TargetPolicy"]
        fixed_target = row["FixedTarget"]
        allowed_targets = row["AllowedTargets"]
        if target_policy not in {"closed", "fixed-output", "none"}:
            raise CapabilityError(f"{event}: invalid engine TargetPolicy")
        if fixed_target is not None and (
            not isinstance(fixed_target, str) or not fixed_target.strip()
        ):
            raise CapabilityError(f"{event}: invalid engine FixedTarget")
        if (not isinstance(allowed_targets, list)
                or len(allowed_targets) != len(set(allowed_targets))
                or any(not isinstance(value, str) or not value.strip()
                       for value in allowed_targets)):
            raise CapabilityError(f"{event}: invalid engine AllowedTargets")
        if target_policy == "closed" and not allowed_targets:
            raise CapabilityError(f"{event}: closed engine target needs allowed values")
        if target_policy == "fixed-output" and fixed_target is None:
            raise CapabilityError(f"{event}: fixed-output engine target needs FixedTarget")
        if target_policy == "none" and (fixed_target is not None or allowed_targets):
            raise CapabilityError(f"{event}: target-less engine event has target constraints")
        allowed = row["AllowedWhereFields"]
        required = row["RequiredWhereFields"]
        if (not isinstance(allowed, list) or len(allowed) != len(set(allowed))
                or not isinstance(required, list) or not set(required).issubset(allowed)):
            raise CapabilityError(f"{event}: invalid engine where-field policy")
        engine_output.append(row)
    if seen_engine != {"timer_elapsed", "chat_received"}:
        raise CapabilityError(f"engine event registry drift: {sorted(seen_engine)}")
    return output, engine_output


def human_label(name: str) -> str:
    return name.replace("_", " ").capitalize()


def enrich_creator_events(
    authoring: list[dict], signatures: list[dict], production: list[dict]
) -> list[dict]:
    event_policy: dict[str, dict] = {}
    for signature in signatures:
        if not signature["CreatorSafe"]:
            continue
        event = signature["CanonicalEvent"]
        value = {
            "Category": signature["CanonicalCategory"],
            "Profile": signature["Profile"],
        }
        if event in event_policy and event_policy[event] != value:
            raise CapabilityError(f"inconsistent creator policy for {event}")
        event_policy[event] = value
    production_by_name = {row["Event"]: row for row in production}
    output = []
    for row in authoring:
        event = row["Name"]
        runtime = production_by_name.get(event)
        fields = [
            {**field, "Label": human_label(field["Name"])} for field in row["Fields"]
        ]
        privacy = (
            "Message text is redacted; only the normalized chat mode is retained."
            if event == "chat_sent"
            else "Sign text is redacted; only the normalized sign target is retained."
            if event == "sign_written"
            else "Only the normalized target and catalog-approved scalar fields are retained."
        )
        label = human_label(event)
        output.append({
            **row,
            **event_policy[event],
            "Label": label,
            "Instruction": f"Observe {label.lower()}; optionally narrow it to {row['TargetDescription']}.",
            "Privacy": privacy,
            "Fields": fields,
            "Availability": {
                "ProductionAvailable": runtime is not None,
                "RuntimeAdapter": None if runtime is None else runtime["RuntimeAdapter"],
                "EvidenceState": "synthetic-only" if runtime is None else runtime["EvidenceState"],
                "EvidenceRevision": None if runtime is None else runtime["EvidenceRevision"],
            },
        })
    return output


def cs(value: str) -> str:
    """A JSON string literal is also a valid C# string literal for this data."""
    return json.dumps(value, ensure_ascii=False)


def render_csharp(atlas: dict, signatures: list[dict]) -> str:
    by_method: dict[str, list[dict]] = defaultdict(list)
    for entry in signatures:
        by_method[entry["MethodId"]].append(entry)
    safe_events = {entry["CanonicalEvent"] for entry in signatures if entry["CreatorSafe"]}

    lines = [
        "// <auto-generated>",
        "//   Generated by tools/component-packets/generate_seam_catalog.py from the",
        "//   assembly-derived atlas plus quest-capability-rules.json. Do not edit.",
        "//",
        f"//   Source: {atlas['Source']}",
        f"//   Atlas rows: {sum(e['AtlasRowCount'] for e in signatures)}; exact signatures: {len(signatures)}; method ids: {len(by_method)}.",
        "// </auto-generated>",
        "",
        "namespace ComfyQuestLab;",
        "",
        "using System.Collections.Generic;",
        "",
        "/// <summary>Exact atlas identities joined to stable creator-event policy.</summary>",
        "public static class LabSeamCatalog {",
        "  public struct Entry {",
        "    public string SignatureId;",
        "    public string MethodId;",
        "    public string Category;",
        "    public string AtlasCategories;",
        "    public string Usability;",
        "    public string CanonicalEvent;",
        "    public string Route;",
        "    public string Profile;",
        "    public bool CreatorSafe;",
        "    public string DedupeGroup;",
        "    public string ActorScope;",
        "  }",
        "",
        "  static readonly Dictionary<string, Entry> _signatures = new Dictionary<string, Entry> {",
    ]

    for entry in sorted(signatures, key=lambda item: item["SignatureId"]):
        lines.append(
            "    { "
            + cs(entry["SignatureId"])
            + ", new Entry { SignatureId = "
            + cs(entry["SignatureId"])
            + ", MethodId = "
            + cs(entry["MethodId"])
            + ", Category = "
            + CATEGORY_CONST[entry["CanonicalCategory"]]
            + ", AtlasCategories = "
            + cs("|".join(entry["AtlasCategories"]))
            + ", Usability = "
            + USABILITY_CONST[entry["AtlasQuestUsability"]]
            + ", CanonicalEvent = "
            + cs(entry["CanonicalEvent"])
            + ", Route = "
            + cs(entry["Route"])
            + ", Profile = "
            + cs(entry["Profile"])
            + ", CreatorSafe = "
            + str(entry["CreatorSafe"]).lower()
            + ", DedupeGroup = "
            + cs(entry["DedupeGroup"])
            + ", ActorScope = "
            + cs(entry["ActorScope"])
            + " } },"
        )

    lines += [
        "  };",
        "",
        "  // Legacy patch calls name a method id. Resolve those to a deterministic exact",
        "  // signature while new patches name the exact overload directly.",
        "  static readonly Dictionary<string, string> _methodSignatures = new Dictionary<string, string> {",
    ]
    for method_id in sorted(by_method):
        first = sorted(by_method[method_id], key=lambda item: item["SignatureId"])[0]
        lines.append(f"    {{ {cs(method_id)}, {cs(first['SignatureId'])} }},")
    lines += [
        "  };",
        "",
        "  public const int AtlasRowCount = "
        + str(sum(entry["AtlasRowCount"] for entry in signatures))
        + ";",
        "  public static int Count { get { return _methodSignatures.Count; } }",
        "  public static int SignatureCount { get { return _signatures.Count; } }",
        "  public static int CreatorSafeSignatureCount { get { return "
        + str(sum(bool(entry["CreatorSafe"]) for entry in signatures))
        + "; } }",
        "  public const int CreatorSafeEventCount = " + str(len(safe_events)) + ";",
        "",
        "  public static IEnumerable<string> AllSeamIds { get { return _methodSignatures.Keys; } }",
        "  public static IEnumerable<string> AllSignatureIds { get { return _signatures.Keys; } }",
        "",
        "  public static bool TryGet(string seamOrSignatureId, out Entry entry) {",
        "    if (_signatures.TryGetValue(seamOrSignatureId, out entry)) {",
        "      return true;",
        "    }",
        "    string signatureId;",
        "    if (_methodSignatures.TryGetValue(seamOrSignatureId, out signatureId)) {",
        "      return _signatures.TryGetValue(signatureId, out entry);",
        "    }",
        "    entry = default(Entry);",
        "    return false;",
        "  }",
        "",
        "  public static string Usability(string seamOrSignatureId) {",
        "    Entry entry;",
        "    return TryGet(seamOrSignatureId, out entry) ? entry.Usability : LabUsability.LabCandidate;",
        "  }",
        "",
        "  public static string Category(string seamOrSignatureId) {",
        "    Entry entry;",
        "    return TryGet(seamOrSignatureId, out entry) ? entry.Category : LabCategory.Combat;",
        "  }",
        "",
        "  public static string CanonicalEvent(string seamOrSignatureId) {",
        "    Entry entry;",
        "    return TryGet(seamOrSignatureId, out entry) ? entry.CanonicalEvent : string.Empty;",
        "  }",
        "",
        "  public static int UsableTodayIn(string category) {",
        "    int count = 0;",
        "    foreach (string signatureId in _methodSignatures.Values) {",
        "      Entry entry = _signatures[signatureId];",
        "      if (entry.Category == category && entry.Usability == LabUsability.Today) {",
        "        count++;",
        "      }",
        "    }",
        "    return count;",
        "  }",
        "",
        "  public static int CountIn(string category) {",
        "    int count = 0;",
        "    foreach (string signatureId in _methodSignatures.Values) {",
        "      if (_signatures[signatureId].Category == category) {",
        "        count++;",
        "      }",
        "    }",
        "    return count;",
        "  }",
        "}",
        "",
    ]
    return "\n".join(lines)


def render_event_catalog(
    signatures: list[dict], aliases: dict[str, list[str]], authoring: list[dict]
) -> str:
    """Render the Unity-free creator vocabulary shared by both mod assemblies."""
    events: dict[str, dict[str, str]] = {}
    for entry in signatures:
        if not entry["CreatorSafe"]:
            continue
        name = entry["CanonicalEvent"]
        definition = {
            "Category": entry["CanonicalCategory"],
            "Profile": entry["Profile"],
        }
        previous = events.get(name)
        if previous is not None and previous != definition:
            raise CapabilityError(
                f"creator event {name!r} has inconsistent definitions: {previous} != {definition}"
            )
        events[name] = definition

    authoring_by_name = {row["Name"]: row for row in authoring}
    lines = [
        "// <auto-generated>",
        "//   Generated by tools/component-packets/generate_seam_catalog.py from the",
        "//   creator-safe rows in quest-capability-manifest.json. Do not edit.",
        "// </auto-generated>",
        "",
        "namespace ComfyNetworkSense;",
        "",
        "using System;",
        "using System.Collections.Generic;",
        "",
        "/// <summary>Stable quest event names shared by the shipping mod and Quest Lab.</summary>",
        "public static class QuestEventCatalog {",
        "  public const string Schema = \"comfy-quest-event/v1\";",
        "",
        "  /// <summary>One event-specific scalar accepted by trigger.where.</summary>",
        "  public readonly struct FieldDefinition {",
        "    public string Name { get; }",
        "    public string Description { get; }",
        "    public string Example { get; }",
        "    public bool DraftByDefault { get; }",
        "",
        "    public FieldDefinition(",
        "        string name, string description, string example, bool draftByDefault) {",
        "      Name = name;",
        "      Description = description;",
        "      Example = example;",
        "      DraftByDefault = draftByDefault;",
        "    }",
        "  }",
        "",
        "  public readonly struct Definition {",
        "    public string Name { get; }",
        "    public string Category { get; }",
        "    public string Profile { get; }",
        "    public string TargetKind { get; }",
        "    public string TargetDescription { get; }",
        "    public string ExampleTarget { get; }",
        "    public bool SupportsWeaponSkill { get; }",
        "    public bool SupportsProjectile { get; }",
        "    public IReadOnlyList<FieldDefinition> Fields { get; }",
        "",
        "    public Definition(string name, string category, string profile) {",
        "      Name = name;",
        "      Category = category;",
        "      Profile = profile;",
        "      TargetKind = \"subject\";",
        "      TargetDescription = \"the event subject\";",
        "      ExampleTarget = \"any\";",
        "      SupportsWeaponSkill = false;",
        "      SupportsProjectile = false;",
        "      Fields = new FieldDefinition[0];",
        "    }",
        "",
        "    public Definition(",
        "        string name, string category, string profile, string targetKind,",
        "        string targetDescription, string exampleTarget, bool supportsWeaponSkill,",
        "        bool supportsProjectile, FieldDefinition[] fields) {",
        "      Name = name;",
        "      Category = category;",
        "      Profile = profile;",
        "      TargetKind = targetKind;",
        "      TargetDescription = targetDescription;",
        "      ExampleTarget = exampleTarget;",
        "      SupportsWeaponSkill = supportsWeaponSkill;",
        "      SupportsProjectile = supportsProjectile;",
        "      Fields = fields ?? new FieldDefinition[0];",
        "    }",
        "  }",
        "",
        "  static readonly Dictionary<string, Definition> _events =",
        "      new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase) {",
    ]
    for name, definition in sorted(events.items()):
        creator = authoring_by_name[name]
        fields = creator["Fields"]
        rendered_fields = "new FieldDefinition[0]"
        if fields:
            rendered_fields = "new[] { " + ", ".join(
                "new FieldDefinition("
                + ", ".join(
                    [
                        cs(field["Name"]),
                        cs(field["Description"]),
                        cs(field["Example"]),
                        str(field["DraftByDefault"]).lower(),
                    ]
                )
                + ")"
                for field in fields
            ) + " }"
        lines.append(
            f"    {{ {cs(name)}, new Definition({cs(name)}, {cs(definition['Category'])}, "
            f"{cs(definition['Profile'])}, {cs(creator['TargetKind'])}, "
            f"{cs(creator['TargetDescription'])}, {cs(creator['ExampleTarget'])}, "
            f"{str(creator['SupportsWeaponSkill']).lower()}, "
            f"{str(creator['SupportsProjectile']).lower()}, {rendered_fields}) }},"
        )
    lines += [
        "  };",
        "",
        "  static readonly Dictionary<string, string[]> _triggerAliases =",
        "      new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase) {",
    ]
    for alias, targets in sorted(aliases.items()):
        target_values = ", ".join(cs(target) for target in targets)
        lines.append(f"    {{ {cs(alias)}, new[] {{ {target_values} }} }},")
    lines += [
        "  };",
        "",
        "  public static int Count { get { return _events.Count; } }",
        "  public static int AliasCount { get { return _triggerAliases.Count; } }",
        "  public static IEnumerable<string> AllEventNames { get { return _events.Keys; } }",
        "",
        "  public static bool IsBindable(string eventName) {",
        "    return !string.IsNullOrWhiteSpace(eventName)",
        "        && (_events.ContainsKey(eventName) || _triggerAliases.ContainsKey(eventName));",
        "  }",
        "",
        "  /// <summary>Returns the catalog spelling, or null for an unsupported event.</summary>",
        "  public static string CanonicalName(string eventName) {",
        "    Definition definition;",
        "    return !string.IsNullOrWhiteSpace(eventName) && _events.TryGetValue(eventName, out definition)",
        "        ? definition.Name",
        "        : null;",
        "  }",
        "",
        "  /// <summary>Whether a schema trigger name accepts this canonical runtime event.</summary>",
        "  public static bool TriggerMatches(string triggerName, string canonicalEventName) {",
        "    if (string.IsNullOrWhiteSpace(triggerName)",
        "        || string.IsNullOrWhiteSpace(canonicalEventName)) {",
        "      return false;",
        "    }",
        "    if (_events.ContainsKey(triggerName)) {",
        "      return string.Equals(triggerName, canonicalEventName, StringComparison.OrdinalIgnoreCase);",
        "    }",
        "    string[] targets;",
        "    if (!_triggerAliases.TryGetValue(triggerName, out targets)) {",
        "      return false;",
        "    }",
        "    foreach (string target in targets) {",
        "      if (string.Equals(target, canonicalEventName, StringComparison.OrdinalIgnoreCase)) {",
        "        return true;",
        "      }",
        "    }",
        "    return false;",
        "  }",
        "",
        "  public static bool TryGet(string eventName, out Definition definition) {",
        "    if (string.IsNullOrWhiteSpace(eventName)) {",
        "      definition = default(Definition);",
        "      return false;",
        "    }",
        "    return _events.TryGetValue(eventName, out definition);",
        "  }",
        "}",
        "",
    ]
    return "\n".join(lines)


def render_signal_catalog(signals: list[dict]) -> str:
    """Render the small, compiled bridge from Grimoire policy to Studio and Runtime."""
    lines = [
        "// <auto-generated>",
        "//   Generated by tools/component-packets/generate_seam_catalog.py from the",
        "//   FastSignals projection in quest-capability-rules.json. Do not edit.",
        "// </auto-generated>",
        "",
        "namespace ComfyQuestContracts;",
        "",
        "using System;",
        "using System.Collections.Generic;",
        "",
        "/// <summary>The intentionally small, live-backed signal lane used by Quest Studio.</summary>",
        "public static class CreatorSignalCatalog {",
        "  public readonly struct Definition {",
        "    public string Id { get; }",
        "    public string EventName { get; }",
        "    public string Label { get; }",
        "    public string Instruction { get; }",
        "    public string Target { get; }",
        "    public string TargetPolicy { get; }",
        "    public string Privacy { get; }",
        "    public string LabProfile { get; }",
        "    public string LabRoute { get; }",
        "    public string RuntimeAdapter { get; }",
        "",
        "    public Definition(string id, string eventName, string label, string instruction,",
        "        string target, string targetPolicy, string privacy, string labProfile,",
        "        string labRoute, string runtimeAdapter) {",
        "      Id = id; EventName = eventName; Label = label; Instruction = instruction;",
        "      Target = target; TargetPolicy = targetPolicy; Privacy = privacy;",
        "      LabProfile = labProfile; LabRoute = labRoute; RuntimeAdapter = runtimeAdapter;",
        "    }",
        "  }",
        "",
        "  static readonly Definition[] _all = new[] {",
    ]
    for signal in signals:
        target = "null" if signal["Target"] is None else cs(signal["Target"])
        lines.append(
            "    new Definition("
            + ", ".join(
                [
                    cs(signal["Id"]), cs(signal["Event"]), cs(signal["Label"]),
                    cs(signal["Instruction"]), target, cs(signal["TargetPolicy"]),
                    cs(signal["Privacy"]), cs(signal["LabProfile"]),
                    cs(signal["LabRoute"]), cs(signal["RuntimeAdapter"]),
                ]
            )
            + "),"
        )
    lines += [
        "  };",
        "",
        "  public static IReadOnlyList<Definition> All { get { return _all; } }",
        "",
        "  public static bool TryGet(string id, out Definition definition) {",
        "    foreach (Definition candidate in _all) {",
        "      if (string.Equals(candidate.Id, id, StringComparison.Ordinal)) {",
        "        definition = candidate; return true;",
        "      }",
        "    }",
        "    definition = default(Definition); return false;",
        "  }",
        "",
        "  public static bool TryDescribe(string eventName, string target, out Definition definition) {",
        "    foreach (Definition candidate in _all) {",
        "      if (!string.Equals(candidate.EventName, eventName, StringComparison.OrdinalIgnoreCase)) continue;",
        "      if (candidate.Target != null",
        "          && !string.Equals(candidate.Target, target, StringComparison.OrdinalIgnoreCase)) continue;",
        "      definition = candidate; return true;",
        "    }",
        "    definition = default(Definition); return false;",
        "  }",
        "}",
        "",
    ]
    return "\n".join(lines)


def render_creator_event_catalog(events: list[dict]) -> str:
    lines = [
        "// <auto-generated>",
        "//   Generated by tools/component-packets/generate_seam_catalog.py. Do not edit.",
        "// </auto-generated>",
        "",
        "namespace ComfyQuestContracts;",
        "",
        "using System;",
        "using System.Collections.Generic;",
        "",
        "/// <summary>All 34 creator-safe meanings, independent of shipping availability.</summary>",
        "public static class CreatorEventCatalog {",
        "  public readonly struct FieldDefinition {",
        "    public string Name { get; } public string Label { get; }",
        "    public string Description { get; } public string Example { get; }",
        "    public bool DraftByDefault { get; }",
        "    public FieldDefinition(string name, string label, string description, string example, bool draftByDefault) {",
        "      Name=name; Label=label; Description=description; Example=example; DraftByDefault=draftByDefault;",
        "    }",
        "  }",
        "  public readonly struct Definition {",
        "    public string Name { get; } public string Label { get; } public string Instruction { get; }",
        "    public string Category { get; } public string Profile { get; }",
        "    public string TargetKind { get; } public string TargetDescription { get; } public string ExampleTarget { get; }",
        "    public bool SupportsWeaponSkill { get; } public bool SupportsProjectile { get; }",
        "    public IReadOnlyList<FieldDefinition> Fields { get; } public string Privacy { get; }",
        "    public bool ProductionAvailable { get; } public string RuntimeAdapter { get; }",
        "    public string EvidenceState { get; } public string EvidenceRevision { get; }",
        "    public Definition(string name,string label,string instruction,string category,string profile,",
        "        string targetKind,string targetDescription,string exampleTarget,bool supportsWeaponSkill,",
        "        bool supportsProjectile,FieldDefinition[] fields,string privacy,bool productionAvailable,",
        "        string runtimeAdapter,string evidenceState,string evidenceRevision) {",
        "      Name=name; Label=label; Instruction=instruction; Category=category; Profile=profile;",
        "      TargetKind=targetKind; TargetDescription=targetDescription; ExampleTarget=exampleTarget;",
        "      SupportsWeaponSkill=supportsWeaponSkill; SupportsProjectile=supportsProjectile;",
        "      Fields=fields??new FieldDefinition[0]; Privacy=privacy; ProductionAvailable=productionAvailable;",
        "      RuntimeAdapter=runtimeAdapter; EvidenceState=evidenceState; EvidenceRevision=evidenceRevision;",
        "    }",
        "  }",
        "  static readonly Definition[] _all = new[] {",
    ]
    for event in events:
        fields = "new FieldDefinition[0]"
        if event["Fields"]:
            fields = "new[] { " + ", ".join(
                "new FieldDefinition(" + ", ".join([
                    cs(field["Name"]), cs(field["Label"]), cs(field["Description"]),
                    cs(field["Example"]), str(field["DraftByDefault"]).lower(),
                ]) + ")" for field in event["Fields"]
            ) + " }"
        availability = event["Availability"]
        lines.append(
            "    new Definition(" + ", ".join([
                cs(event["Name"]), cs(event["Label"]), cs(event["Instruction"]),
                cs(event["Category"]), cs(event["Profile"]), cs(event["TargetKind"]),
                cs(event["TargetDescription"]), cs(event["ExampleTarget"]),
                str(event["SupportsWeaponSkill"]).lower(),
                str(event["SupportsProjectile"]).lower(), fields, cs(event["Privacy"]),
                str(availability["ProductionAvailable"]).lower(),
                cs(availability["RuntimeAdapter"]), cs(availability["EvidenceState"]),
                cs(availability["EvidenceRevision"]),
            ]) + "),"
        )
    lines += [
        "  };",
        "  static readonly Dictionary<string, Definition> _byName = Build();",
        "  static Dictionary<string, Definition> Build() { var result=new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase); foreach(var item in _all) result[item.Name]=item; return result; }",
        "  public static IReadOnlyList<Definition> All { get { return _all; } }",
        "  public static int Count { get { return _all.Length; } }",
        "  public static bool TryGet(string name,out Definition definition) { if(!string.IsNullOrWhiteSpace(name)&&_byName.TryGetValue(name,out definition))return true;definition=default(Definition);return false; }",
        "}",
        "",
    ]
    return "\n".join(lines)


def render_production_event_catalog(events: list[dict], engine_events: list[dict]) -> str:
    lines = [
        "// <auto-generated>",
        "//   Generated by tools/component-packets/generate_seam_catalog.py. Do not edit.",
        "// </auto-generated>",
        "",
        "namespace ComfyQuestContracts;",
        "",
        "using System;",
        "using System.Collections.Generic;",
        "",
        "/// <summary>Fail-closed Runtime event registry; synthetic-only creator meanings are absent.</summary>",
        "public static class RuntimeProductionEventCatalog {",
        "  public readonly struct Definition {",
        "    public string Name { get; } public string RuntimeAdapter { get; }",
        "    public string EvidenceState { get; } public string EvidenceRevision { get; }",
        "    public string TargetPolicy { get; } public string FixedTarget { get; } public IReadOnlyList<string> AllowedTargets { get; } public bool EmitsWeaponSkill { get; } public bool EmitsProjectile { get; }",
        "    public IReadOnlyList<string> AllowedWhereFields { get; } public IReadOnlyDictionary<string,string> FixedWhere { get; } public IReadOnlyList<string> WitnessSignatures { get; }",
        "    public Definition(string name,string runtimeAdapter,string evidenceState,string evidenceRevision,string targetPolicy,string fixedTarget,string[] allowedTargets,",
        "        bool emitsWeaponSkill,bool emitsProjectile,string[] allowedWhereFields,Dictionary<string,string> fixedWhere,string[] witnessSignatures) {",
        "      Name=name; RuntimeAdapter=runtimeAdapter; EvidenceState=evidenceState; EvidenceRevision=evidenceRevision;",
        "      TargetPolicy=targetPolicy; FixedTarget=fixedTarget; AllowedTargets=allowedTargets??new string[0]; EmitsWeaponSkill=emitsWeaponSkill; EmitsProjectile=emitsProjectile;",
        "      AllowedWhereFields=allowedWhereFields??new string[0]; FixedWhere=fixedWhere??new Dictionary<string,string>(); WitnessSignatures=witnessSignatures??new string[0];",
        "    }",
        "  }",
        "  public readonly struct EngineDefinition {",
        "    public string Name { get; } public string Label { get; } public string Instruction { get; }",
        "    public string RuntimeAdapter { get; } public string TargetPolicy { get; } public string FixedTarget { get; } public IReadOnlyList<string> AllowedTargets { get; } public IReadOnlyList<string> RequiredWhereFields { get; }",
        "    public IReadOnlyList<string> AllowedWhereFields { get; } public string Privacy { get; }",
        "    public EngineDefinition(string name,string label,string instruction,string runtimeAdapter,string targetPolicy,string fixedTarget,string[] allowedTargets,string[] required,string[] allowed,string privacy) {",
        "      Name=name; Label=label; Instruction=instruction; RuntimeAdapter=runtimeAdapter; TargetPolicy=targetPolicy; FixedTarget=fixedTarget; AllowedTargets=allowedTargets??new string[0]; RequiredWhereFields=required; AllowedWhereFields=allowed; Privacy=privacy;",
        "    }",
        "  }",
        "  static readonly Definition[] _all = new[] {",
    ]
    for event in events:
        allowed = "new[] { " + ", ".join(cs(value) for value in event["AllowedWhereFields"]) + " }" if event["AllowedWhereFields"] else "new string[0]"
        allowed_targets = "new[] { " + ", ".join(cs(value) for value in event["AllowedTargets"]) + " }" if event["AllowedTargets"] else "new string[0]"
        fixed = "new Dictionary<string,string> { " + ", ".join(
            "{ " + cs(key) + ", " + cs(value) + " }"
            for key, value in event["FixedWhere"].items()
        ) + " }" if event["FixedWhere"] else "new Dictionary<string,string>()"
        witnesses = "new[] { " + ", ".join(cs(value) for value in event["WitnessSignatures"]) + " }"
        lines.append("    new Definition(" + ", ".join([
            cs(event["Event"]), cs(event["RuntimeAdapter"]), cs(event["EvidenceState"]),
            cs(event["EvidenceRevision"]), cs(event["TargetPolicy"]),
            cs(event["FixedTarget"]), allowed_targets,
            str(event["EmitsWeaponSkill"]).lower(), str(event["EmitsProjectile"]).lower(),
            allowed, fixed, witnesses,
        ]) + "),")
    lines += ["  };", "  static readonly EngineDefinition[] _engine = new[] {"]
    for event in engine_events:
        required = "new[] { " + ", ".join(cs(value) for value in event["RequiredWhereFields"]) + " }" if event["RequiredWhereFields"] else "new string[0]"
        allowed = "new[] { " + ", ".join(cs(value) for value in event["AllowedWhereFields"]) + " }" if event["AllowedWhereFields"] else "new string[0]"
        allowed_targets = "new[] { " + ", ".join(cs(value) for value in event["AllowedTargets"]) + " }" if event["AllowedTargets"] else "new string[0]"
        lines.append("    new EngineDefinition(" + ", ".join([
            cs(event["Event"]), cs(event["Label"]), cs(event["Instruction"]),
            cs(event["RuntimeAdapter"]), cs(event["TargetPolicy"]),
            cs(event["FixedTarget"]), allowed_targets, required, allowed,
            cs(event["Privacy"]),
        ]) + "),")
    lines += [
        "  };",
        "  static readonly Dictionary<string, Definition> _byName=Build();",
        "  static readonly Dictionary<string, EngineDefinition> _engineByName=BuildEngine();",
        "  static Dictionary<string, Definition> Build(){var value=new Dictionary<string, Definition>(StringComparer.OrdinalIgnoreCase);foreach(var item in _all)value[item.Name]=item;return value;}",
        "  static Dictionary<string, EngineDefinition> BuildEngine(){var value=new Dictionary<string, EngineDefinition>(StringComparer.OrdinalIgnoreCase);foreach(var item in _engine)value[item.Name]=item;return value;}",
        "  public static IReadOnlyList<Definition> All { get { return _all; } }",
        "  public static IReadOnlyList<EngineDefinition> EngineEvents { get { return _engine; } }",
        "  public static int Count { get { return _all.Length; } }",
        "  public static bool Contains(string name){return !string.IsNullOrWhiteSpace(name)&&_byName.ContainsKey(name);}",
        "  public static bool IsEngineEvent(string name){return !string.IsNullOrWhiteSpace(name)&&_engineByName.ContainsKey(name);}",
        "  public static bool TryGet(string name,out Definition definition){if(!string.IsNullOrWhiteSpace(name)&&_byName.TryGetValue(name,out definition))return true;definition=default(Definition);return false;}",
        "  public static bool TryGetEngine(string name,out EngineDefinition definition){if(!string.IsNullOrWhiteSpace(name)&&_engineByName.TryGetValue(name,out definition))return true;definition=default(EngineDefinition);return false;}",
        "  public static ISet<string> CreateSet(){return new HashSet<string>(_byName.Keys,StringComparer.OrdinalIgnoreCase);}",
        "  public static bool IsAllowedWhere(string eventName,string field){",
        "    if(string.IsNullOrWhiteSpace(field))return false;",
        "    if(TryGet(eventName,out var definition)){foreach(var allowed in definition.AllowedWhereFields)if(string.Equals(allowed,field,StringComparison.OrdinalIgnoreCase))return true;return false;}",
        "    if(TryGetEngine(eventName,out var engine)){foreach(var allowed in engine.AllowedWhereFields)if(string.Equals(allowed,field,StringComparison.OrdinalIgnoreCase))return true;}",
        "    return false;",
        "  }",
        "}",
        "",
    ]
    return "\n".join(lines)


def render_runtime_witness_catalog(signatures: list[dict], production: list[dict]) -> str:
    safe = [entry for entry in signatures if entry["CreatorSafe"]]
    production_witnesses = {
        value for event in production for value in event["WitnessSignatures"]
    }
    lines = [
        "// <auto-generated>",
        "//   Generated by tools/component-packets/generate_seam_catalog.py. Do not edit.",
        "// </auto-generated>",
        "",
        "namespace ComfyQuestContracts;",
        "",
        "using System;",
        "using System.Collections.Generic;",
        "",
        "/// <summary>Exact creator-safe assembly witnesses; availability is explicit per signature.</summary>",
        "public static class RuntimeWitnessCatalog {",
        "  public readonly struct Definition {",
        "    public string SignatureId { get; } public string MethodId { get; } public string EventName { get; }",
        "    public string Route { get; } public string Profile { get; } public string DedupeGroup { get; } public string ActorScope { get; }",
        "    public bool ProductionAvailable { get; }",
        "    public Definition(string signatureId,string methodId,string eventName,string route,string profile,string dedupeGroup,string actorScope,bool productionAvailable){",
        "      SignatureId=signatureId;MethodId=methodId;EventName=eventName;Route=route;Profile=profile;DedupeGroup=dedupeGroup;ActorScope=actorScope;ProductionAvailable=productionAvailable;",
        "    }",
        "  }",
        "  static readonly Definition[] _all=new[] {",
    ]
    for entry in sorted(safe, key=lambda value: value["SignatureId"]):
        lines.append("    new Definition(" + ", ".join([
            cs(entry["SignatureId"]), cs(entry["MethodId"]), cs(entry["CanonicalEvent"]),
            cs(entry["Route"]), cs(entry["Profile"]), cs(entry["DedupeGroup"]),
            cs(entry["ActorScope"]), str(entry["SignatureId"] in production_witnesses).lower(),
        ]) + "),")
    lines += [
        "  };",
        "  static readonly Dictionary<string,Definition> _bySignature=Build();",
        "  static Dictionary<string,Definition> Build(){var result=new Dictionary<string,Definition>(StringComparer.Ordinal);foreach(var item in _all)result[item.SignatureId]=item;return result;}",
        "  public static IReadOnlyList<Definition> All { get { return _all; } }",
        "  public static int Count { get { return _all.Length; } }",
        "  public static bool TryGet(string signatureId,out Definition definition){if(signatureId!=null&&_bySignature.TryGetValue(signatureId,out definition))return true;definition=default(Definition);return false;}",
        "}",
        "",
    ]
    return "\n".join(lines)


def render_outputs() -> dict[Path, str]:
    atlas, rules_document, method_rules, signatures = build_model()
    manifest = build_manifest(atlas, rules_document, signatures)
    return {
        MANIFEST: json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        CSHARP: render_csharp(atlas, signatures),
        SIGNAL_CATALOG: render_signal_catalog(manifest["FastSignals"]),
        CREATOR_EVENT_CATALOG: render_creator_event_catalog(manifest["CreatorEvents"]),
        PRODUCTION_EVENT_CATALOG: render_production_event_catalog(
            manifest["RuntimeProductionEvents"], manifest["EngineEvents"]
        ),
        RUNTIME_WITNESS_CATALOG: render_runtime_witness_catalog(
            signatures, manifest["RuntimeProductionEvents"]
        ),
        EVENT_CATALOG: render_event_catalog(
            signatures, rules_document.get("TriggerAliases", {}), manifest["CreatorEvents"]
        ),
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--check",
        action="store_true",
        help="fail if committed generated outputs are missing or stale",
    )
    args = parser.parse_args()
    try:
        outputs = render_outputs()
    except (CapabilityError, KeyError, TypeError, json.JSONDecodeError) as error:
        print(f"capability generation failed: {error}", file=sys.stderr)
        return 1

    stale = []
    for path, content in outputs.items():
        if args.check:
            if not path.exists() or path.read_text(encoding="utf-8") != content:
                stale.append(path)
        else:
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(content, encoding="utf-8", newline="\n")
            print(f"  {path}")

    if stale:
        for path in stale:
            print(f"stale generated artifact: {path.relative_to(REPO)}", file=sys.stderr)
        print("run tools/component-packets/generate_seam_catalog.py", file=sys.stderr)
        return 1

    manifest = json.loads(outputs[MANIFEST])
    counts = manifest["Counts"]
    verb = "verified" if args.check else "generated"
    print(
        f"{verb} {counts['AtlasRows']} atlas rows / {counts['UniqueSignatures']} signatures / "
        f"{counts['UniqueMethods']} methods; {counts['CreatorSafeEvents']} creator events"
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
