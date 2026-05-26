---
description: "Implementation-time architecture compliance review for changed code."
---

# NKDA Core Implementation Architecture Compliance

Repository-local command surface for the mandatory `after_implement` architecture gate.

## Scope

Validate changed implementation against:
- architecture boundaries and capability seams,
- all five architecture perspectives + architecture deepening,
- guardrail-driven reject conditions for shortcuts/stubs/partial states.

## Required Outcome

- **PASS** only when all perspectives are explicitly evidenced and no guardrail violation remains.
- **FAIL** on any missing perspective evidence, seam bypass, stub placeholder, or deferral pattern.

If `FAIL`, completion must be blocked.
