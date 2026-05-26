---
description: "Design-time architecture compliance review for tasks.md against spec.md and plan.md."
---

# NKDA Core Tasks Architecture Compliance

Repository-local command surface for the mandatory `after_tasks` architecture gate.

## Scope

Validate `tasks.md` design coverage against:
- `spec.md`
- `plan.md`
- active architecture guardrails
- five architecture perspectives + architecture deepening

## Required Outcome

- **PASS** only when every requirement has concrete implementation tasks and no perspective fails.
- **FAIL** when any requirement is missing, partially specified, or any perspective is non-compliant.

If `FAIL`, implementation must not begin.
