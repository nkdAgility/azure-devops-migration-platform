# ADR 0032 — Routing Catalog Completion and Machine-Validated Contract Schemas

## Status

Accepted — executes Phase 2 (P2.4/P2.5) of `analysis/agentic-maturity-level4-plan.md`, implemented at the maintainer's instruction (2026-07-13)

## Context

The route-first protocol is fail-closed: a task with no matching activity in `.agents/10-contracts/routing-catalog.yaml` requires the agent to stop and ask. The catalog, however, covered only four activities (package, agent, control-plane, cli) while `.agents/00-entry/task-profiles.yaml` defined five profiles — with no explicit mapping between them. The most common task categories (test-only changes, documentation, connector work, and changes to the harness itself) had no route at all, so fail-closed in theory meant improvisation in practice. Nothing validated the contract YAMLs: a typo, a missing profile, or a reference to a deleted guardrail file would go unnoticed until an agent misrouted.

## Decision

1. **The routing catalog is total over the task space.** Four new activities — `connectors`, `tests`, `docs`, `harness` — join the original four, each with triggers, `first_surfaces`, and escalation order. Every activity now carries an explicit `profile` field naming its task profile; two new profiles (`tests`, `harness`) cover the previously unmapped activities.
2. **Contract catalogs have JSON Schemas** in `.agents/10-contracts/schemas/` (routing-catalog, task-profiles, change-classes, consent-policy, surface-catalog, seam-catalog). The schemas encode structural invariants, including `mode: fail-closed` and `block_if_missing: true` as constants that cannot drift.
3. **`scripts/guardrails/validate-agent-contracts.py` enforces cross-file consistency**: every activity's profile exists; every profile is reachable from at least one activity (totality); every escalation target is a real activity; every guardrail/context file a profile references exists on disk.
4. **CI runs the validator** as the `Agent Contract Schemas` job in `.github/workflows/governance.yml` on every PR and push to main.

## Alternatives Considered

**Keep routing partial and rely on fail-closed.** Rejected: observed behaviour is that unrouted tasks get improvised, not escalated; a fail-closed rule with no route for the majority of real tasks is a rule that trains agents to ignore it.

**Validate with tests inside the MSTest suite instead of a script.** Considered; the script form runs without a .NET build (fast, ubuntu runner) and is reusable by the `strict-implement` flow. An MSTest drift test may still be added later; the CI gate is the binding control either way.

**One mega-schema for all catalogs.** Rejected: per-file schemas keep diffs reviewable and let each catalog version independently.

## Consequences

- Editing any contract catalog now has three gates: the schema (structure), the consistency script (cross-file), and the contract-ADR tripwire from ADR-0031 (governance).
- Adding a routing activity requires naming a real profile, and adding a profile requires routing something to it — dead configuration cannot accumulate silently.
- Deleting or renaming a guardrail/context file that a profile references fails CI, closing the stale-reference drift class observed in the readiness assessment.
- The validator needs Python with `pyyaml`/`jsonschema` in CI (installed in-job); locally it degrades gracefully via `pip install --user`.
