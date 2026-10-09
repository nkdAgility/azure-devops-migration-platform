#!/usr/bin/env python3
# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited.
"""Validate session evidence files (ADR-0033).

Checks every Logs/atdd-sessions/*.json against
.agents/50-evidence/session-evidence.schema.json, plus consistency rules the
schema cannot express:

 - outcome SUCCESS requires every executed suite to report failed == 0;
 - a suite with run=true must carry passed/failed counts;
 - a suite with run=false must carry not_run_reason.

With --covers CHANGED_LIST, every path in CHANGED_LIST (one per line) must
match at least one files_changed entry (exact path or fnmatch glob) across
the validated evidence files, so evidence is tied to the current diff.

Usage:
  validate-evidence.py                              # validate all evidence files
  validate-evidence.py FILE...                      # validate specific files
  validate-evidence.py --covers CHANGED_LIST FILE...

Exit 0 = all valid. Requires pyyaml-free stdlib + jsonschema.
"""
from __future__ import annotations

import fnmatch
import json
import sys
from pathlib import Path

from jsonschema import Draft202012Validator

REPO = Path(__file__).resolve().parents[2]
SCHEMA = REPO / ".agents" / "50-evidence" / "session-evidence.schema.json"
EVIDENCE_DIR = REPO / "Logs" / "atdd-sessions"

validator = Draft202012Validator(json.loads(SCHEMA.read_text(encoding="utf-8")))

args = sys.argv[1:]
covers: list[str] = []
if args[:1] == ["--covers"]:
    if len(args) < 2:
        print("--covers requires a file listing changed paths", file=sys.stderr)
        sys.exit(2)
    covers = [ln.strip() for ln in Path(args[1]).read_text(encoding="utf-8").splitlines() if ln.strip()]
    args = args[2:]

if args:
    files = [Path(a) for a in args]
else:
    files = sorted(EVIDENCE_DIR.glob("*.json")) if EVIDENCE_DIR.exists() else []

if not files:
    print("No evidence files found to validate.")
    sys.exit(0)

errors: list[str] = []
declared: list[str] = []
for f in files:
    rel = f.resolve().relative_to(REPO).as_posix() if f.resolve().is_relative_to(REPO) else str(f)
    try:
        data = json.loads(f.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError) as exc:
        errors.append(f"{rel}: unreadable or invalid JSON ({exc})")
        continue

    for err in sorted(validator.iter_errors(data), key=str):
        path = "/".join(str(p) for p in err.absolute_path) or "<root>"
        errors.append(f"{rel}: {path}: {err.message}")

    declared.extend(p for p in data.get("files_changed") or [] if isinstance(p, str))

    for i, suite in enumerate(data.get("suites") or []):
        if not isinstance(suite, dict):
            continue
        name = suite.get("suite", f"#{i}")
        if suite.get("run") is True:
            if "passed" not in suite or "failed" not in suite:
                errors.append(f"{rel}: suite {name}: run=true requires passed and failed counts")
            elif data.get("outcome") == "SUCCESS" and suite.get("failed", 0) > 0:
                errors.append(f"{rel}: suite {name}: outcome SUCCESS but failed={suite['failed']}")
        elif suite.get("run") is False and not suite.get("not_run_reason"):
            errors.append(f"{rel}: suite {name}: run=false requires not_run_reason")

for changed in covers:
    if not any(changed == p or fnmatch.fnmatchcase(changed, p) for p in declared):
        errors.append(f"{changed}: changed in this PR but not listed in any evidence files_changed")

if errors:
    print(f"Evidence validation FAILED ({len(errors)} error(s)):", file=sys.stderr)
    for e in errors:
        print(f"  - {e}", file=sys.stderr)
    sys.exit(1)

print(f"Evidence validation passed: {len(files)} file(s).")
