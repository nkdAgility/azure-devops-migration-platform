#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited.
"""Validate the .agents contract catalogs against their JSON Schemas and
cross-file consistency rules (ADR-0032).

Checks:
 1. Each contract YAML parses and validates against its schema in
    .agents/10-contracts/schemas/.
 2. Every routing activity's `profile` exists in task-profiles.yaml.
 3. Every profile is referenced by at least one activity (totality).
 4. Every `escalation_order` entry names an existing activity.
 5. Every guardrail/context file referenced by a profile exists on disk.
 6. Drift: the ADR digest (.agents/30-context/domains/decision-records-summary.md)
    has one entry per ADR in docs/adr/.
 7. Drift: .agents/90-index/context-index.md lists every domain context file.

Requires: pyyaml, jsonschema.  Exit code 0 = all checks pass.
"""
from __future__ import annotations

import json
import sys
from pathlib import Path

import yaml
from jsonschema import Draft202012Validator

REPO = Path(__file__).resolve().parents[2]
SCHEMAS = REPO / ".agents" / "10-contracts" / "schemas"

PAIRS = {
    REPO / ".agents/10-contracts/routing-catalog.yaml": SCHEMAS / "routing-catalog.schema.json",
    REPO / ".agents/00-entry/task-profiles.yaml": SCHEMAS / "task-profiles.schema.json",
    REPO / ".agents/10-contracts/change-classes.yaml": SCHEMAS / "change-classes.schema.json",
    REPO / ".agents/10-contracts/consent-policy.yaml": SCHEMAS / "consent-policy.schema.json",
    REPO / ".agents/10-contracts/surface-catalog.yaml": SCHEMAS / "surface-catalog.schema.json",
    REPO / ".agents/10-contracts/seam-catalog.yaml": SCHEMAS / "seam-catalog.schema.json",
}

errors: list[str] = []
docs: dict[str, dict] = {}

for yaml_path, schema_path in PAIRS.items():
    rel = yaml_path.relative_to(REPO).as_posix()
    if not yaml_path.exists():
        errors.append(f"{rel}: file missing")
        continue
    if not schema_path.exists():
        errors.append(f"{schema_path.relative_to(REPO).as_posix()}: schema missing")
        continue
    try:
        data = yaml.safe_load(yaml_path.read_text(encoding="utf-8"))
    except yaml.YAMLError as exc:
        errors.append(f"{rel}: YAML parse error: {exc}")
        continue
    try:
        schema = json.loads(schema_path.read_text(encoding="utf-8"))
    except json.JSONDecodeError as exc:
        errors.append(f"{schema_path.relative_to(REPO).as_posix()}: JSON parse error: {exc}")
        continue
    for err in sorted(Draft202012Validator(schema).iter_errors(data), key=str):
        path = "/".join(str(p) for p in err.absolute_path) or "<root>"
        errors.append(f"{rel}: {path}: {err.message}")
    docs[rel] = data

routing = docs.get(".agents/10-contracts/routing-catalog.yaml") or {}
profiles_doc = docs.get(".agents/00-entry/task-profiles.yaml") or {}
activities = routing.get("activities") or {}
profiles = profiles_doc.get("profiles") or {}

# 2. activity.profile must exist
for name, activity in activities.items():
    profile = (activity or {}).get("profile")
    if profile and profile not in profiles:
        errors.append(f"routing-catalog: activity '{name}' names unknown profile '{profile}'")

# 3. totality: every profile reachable from >=1 activity
referenced = {(a or {}).get("profile") for a in activities.values()}
for profile in profiles:
    if profile not in referenced:
        errors.append(f"task-profiles: profile '{profile}' is not reachable from any routing activity")

# 4. escalation_order entries must be activity names
for name, activity in activities.items():
    for target in (activity or {}).get("escalation_order") or []:
        if target not in activities:
            errors.append(f"routing-catalog: activity '{name}' escalates to unknown activity '{target}'")

# 5. profile guardrail/context files must exist (globs allowed in context)
for pname, profile in profiles.items():
    for kind in ("guardrails", "context"):
        for ref in (profile or {}).get(kind) or []:
            if "*" in ref:
                continue
            if not (REPO / ref).exists():
                errors.append(f"task-profiles: profile '{pname}' {kind} references missing file '{ref}'")

# 6. ADR digest drift: one digest entry per ADR file
import re

adr_files = sorted((REPO / "docs" / "adr").glob("[0-9][0-9][0-9][0-9]-*.md"))
adr_ids = {f.name[:4] for f in adr_files}
digest_path = REPO / ".agents/30-context/domains/decision-records-summary.md"
digest_ids = set(re.findall(r"^## ADR (\d{4})", digest_path.read_text(encoding="utf-8"), re.M))
for missing in sorted(adr_ids - digest_ids):
    errors.append(f"decision-records-summary: no digest entry for ADR {missing}")
for stale in sorted(digest_ids - adr_ids):
    errors.append(f"decision-records-summary: digest entry for nonexistent ADR {stale}")

# 7. Context index drift: every domain file listed
index_text = (REPO / ".agents/90-index/context-index.md").read_text(encoding="utf-8")
for domain_file in sorted((REPO / ".agents/30-context/domains").glob("*.md")):
    if f"domains/{domain_file.name}" not in index_text:
        errors.append(f"context-index: missing entry for domains/{domain_file.name}")

if errors:
    print(f"Agent contract validation FAILED ({len(errors)} error(s)):", file=sys.stderr)
    for e in errors:
        print(f"  - {e}", file=sys.stderr)
    sys.exit(1)

print(f"Agent contract validation passed: {len(PAIRS)} catalogs, "
      f"{len(activities)} activities, {len(profiles)} profiles.")
