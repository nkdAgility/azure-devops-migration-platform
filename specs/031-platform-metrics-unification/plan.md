# Reconciliation Plan: Platform Metrics Unification

**Spec**: `specs/031-platform-metrics-unification/spec.md`  
**Reconciled**: 2026-05-17

## Applicable guardrails

- `.agents/20-guardrails/core/architecture-boundaries.md`
- `.agents/20-guardrails/core/capability-ethos-rules.md`
- `.agents/20-guardrails/domains/observability-requirements.md`
- `.agents/20-guardrails/workflow/documentation-rules.md`
- `.agents/20-guardrails/workflow/definition-of-done.md`

## Change class

- **Class A** (documentation/status reconciliation only; no runtime surface change).

## Scope

- Reconcile spec 031 artifacts against repository truth.
- Record complete/incomplete/superseded task truth in `tasks.md`.
- Capture contradictions and verification evidence in spec/checklist artifacts.

## Authority order applied

1. `.agents` guidance
2. newer related specs (032, 033, 034, 035)
3. current spec (031)
4. implementation evidence
5. tests/verification evidence
6. inferred intent

## Remaining incomplete work tracked

1. TFS telemetry literal instrument names not yet aligned to `platform.*`.
2. Deferred breaking-change operations (package versioning/release-note/runbook migration).
3. Stale XML reference to removed `WellKnownMetricNames`.
