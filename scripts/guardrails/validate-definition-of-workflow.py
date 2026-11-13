#!/usr/bin/env python3
"""Validate platform/development/definition-of-workflow.json.

Checks:
1. Every shift's from/to is the initial state, a state some shift reaches, or 'closed'.
2. No shift's checks are all met by a brand-new Backlog Item, that is, an issue whose body is
   exactly the issue template and which has no comments or pull request. A check that
   matches template placeholder text (such as "**Result:** ready / blocked / ...") would let an
   unassessed item move on.

Usage: validate-definition-of-workflow.py [--definition PATH] [--template PATH]
Exit 0: valid. Exit 1: invalid (each problem printed). Exit 2: a file is missing or unreadable.
"""
import argparse
import json
import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]


def template_body(path: Path) -> str:
    text = path.read_text(encoding="utf-8")
    if text.startswith("---"):
        end = text.find("\n---", 3)
        if end != -1:
            text = text[end + 4:]
    return text


def compile_pattern(pattern: str) -> re.Pattern:
    # Definitions use .NET inline flags such as (?im); Python accepts the same leading group.
    return re.compile(pattern)


def problems(definition: dict, blank: str) -> list[str]:
    found = []
    shifts = definition.get("shifts") or []
    if not shifts:
        return ["the definition has no shifts"]
    reachable = {definition.get("initialState")} | {s.get("to") for s in shifts}
    for s in shifts:
        name = s.get("name", "<unnamed>")
        if s.get("from") not in reachable:
            found.append(f"shift {name}: from-state '{s.get('from')}' is never reached")
        checks = [c for c in s.get("checks") or [] if c.get("type") == "pattern"]
        if not checks:
            continue
        try:
            met = all(compile_pattern(c["pattern"]).search(blank) for c in checks)
        except re.error as err:
            found.append(f"shift {name}: a pattern does not compile: {err}")
            continue
        if met:
            found.append(
                f"shift {name}: every check is met by a brand-new issue made from the template, "
                f"so an item would move on without any work"
            )
    return found


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--definition", default=str(ROOT / "platform/development/definition-of-workflow.json"))
    parser.add_argument("--template", default=str(ROOT / ".github/ISSUE_TEMPLATE/backlog-item.md"))
    args = parser.parse_args()
    try:
        definition = json.loads(Path(args.definition).read_text(encoding="utf-8"))
        blank = template_body(Path(args.template))
    except (OSError, json.JSONDecodeError) as err:
        print(f"cannot read input: {err}")
        return 2
    found = problems(definition, blank)
    for p in found:
        print(f"FAIL {p}")
    if found:
        return 1
    print(f"OK {len(definition['shifts'])} shifts; none is met by a blank Backlog Item")
    return 0


if __name__ == "__main__":
    sys.exit(main())
