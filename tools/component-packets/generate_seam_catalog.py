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
Writes:
  tools/component-packets/samples/quest-capability-manifest.json
  network/mod/ComfyQuestLab/Core/LabSeamCatalog.g.cs
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
MANIFEST = HERE / "samples" / "quest-capability-manifest.json"
CSHARP = REPO / "network" / "mod" / "ComfyQuestLab" / "Core" / "LabSeamCatalog.g.cs"

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
    if not safe and profile in {"core", "extended"}:
        raise CapabilityError(f"{method_id}: unsafe event cannot use {profile!r} profile")
    for field in ("Dedupe", "Actor", "Reason"):
        if not isinstance(policy[field], str) or not policy[field].strip():
            raise CapabilityError(f"{method_id}: {field} must be a non-empty string")


def build_model() -> tuple[dict, dict[str, dict], list[dict]]:
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

    return atlas, method_rules, output_signatures


def build_manifest(atlas: dict, signatures: list[dict]) -> dict:
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
    return {
        "Schema": "comfy-quest-capabilities/v1",
        "SourceAtlas": "tools/component-packets/samples/valheim-event-atlas.json",
        "SourceRules": "tools/component-packets/quest-capability-rules.json",
        "AssemblySource": atlas["Source"],
        "Counts": {
            "AtlasRows": sum(entry["AtlasRowCount"] for entry in signatures),
            "UniqueSignatures": len(signatures),
            "UniqueMethods": len({entry["MethodId"] for entry in signatures}),
            "CanonicalEvents": len(all_events),
            "CreatorSafeEvents": len(safe_events),
            "CreatorSafeSignatures": sum(bool(entry["CreatorSafe"]) for entry in signatures),
        },
        "CreatorSafeEvents": safe_events,
        "CategoryCounts": category_counts,
        "Signatures": signatures,
    }


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


def render_outputs() -> dict[Path, str]:
    atlas, method_rules, signatures = build_model()
    manifest = build_manifest(atlas, signatures)
    return {
        MANIFEST: json.dumps(manifest, indent=2, ensure_ascii=False) + "\n",
        CSHARP: render_csharp(atlas, signatures),
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
