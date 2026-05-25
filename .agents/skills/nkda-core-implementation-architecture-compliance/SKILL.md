---
name: nkda-core-implementation-architecture-compliance
description: Implementation-time architecture compliance review — validates changed code against all architecture perspectives, seam contracts, and guardrails before completion gates.
---

# Skill: NKDA Core Implementation Architecture Compliance

Run this after implementation changes and before completion claims.

## Mandatory Checks

1. Inspect changed implementation scope.
2. Evaluate compliance against:
   - architecture boundaries,
   - canonical surface and seam contracts,
   - all six architecture perspectives.
3. Confirm no reachable stubs/placeholders:
   - `NotImplementedException`
   - placeholder defaults
   - deferred follow-up implementation markers in runtime paths
4. Confirm no bypass of canonical seams by wrappers/orchestrators/extensions.
5. Emit explicit pass/fail evidence for each perspective and seam check.

## Verdict Rules

- **PASS**: all perspective and seam checks pass with explicit evidence.
- **FAIL**: any missing evidence, seam bypass, or shortcut/stub pattern.

A `FAIL` blocks completion.
