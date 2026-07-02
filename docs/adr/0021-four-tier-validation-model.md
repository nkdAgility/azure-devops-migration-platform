# ADR 0021 — Four-Tier Validation Model

## Status

Accepted

Records the validation lifecycle that is already implemented and documented in [docs/validation.md](../validation.md); this ADR captures the decision and its rationale so the tier split is not re-litigated.

## Context

Validation failures have very different costs depending on when they surface. A malformed config discovered by the agent mid-import wastes queue time, agent capacity, and possibly leaves a half-imported target. Conversely, forcing the CLI to fully verify migration outcomes before submission is impossible — outcomes only exist after import.

The platform needed an explicit answer to *what is validated, when, by which component, and with what network access* — aligned with the architecture rules that the Control Plane never executes migration logic (ADR-0004) and only agents touch the package and the target (ADR-0005).

## Decision

Validation runs at four fixed points in the lifecycle. Fail-fast is the default at every tier; continue-on-error must be explicitly configured.

| Tier | When | Who runs it | Network required |
|---|---|---|---|
| **0 — Structural** | Before CLI submits anything | CLI | No |
| **1 — Connectivity** | Before CLI submits anything | CLI | Yes |
| **2 — Pre-flight** | Before import begins | Migration Agent / Job Engine | Yes |
| **3 — Post-flight** | After import completes (and as the standalone `Validate` phase) | Migration Agent / Job Engine | Yes |

1. **Tier 0 (Structural)** is purely local: config parse, schema version, required fields, module names against bundled per-module JSON Schemas, policy ranges, path normalisation. No credentials, no network.
2. **Tier 1 (Connectivity)** verifies source/target reachability, project existence, and minimum permissions per job kind, plus package URI accessibility. Tiers 0+1 form the CLI pre-validation pass — a `Job` is submitted only if both pass.
3. **Tier 2 (Pre-flight)** validates the package before import: manifest schema, required folders, `revision.json` validity, attachment existence and hash, identity-mapping integrity. Each module's `ValidateAsync` participates and must be side-effect free.
4. **Tier 3 (Post-flight)** validates outcomes: count parity within configured tolerance, sampled link and attachment integrity, unresolved-identity accounting, deterministic cursor completion. It is idempotent and re-runnable via `mode: Validate`, writes `validation-report.json` regardless of pass/fail, and emits correctness metrics.
5. The Control Plane performs only job deduplication and final schema validation at submission — it never executes validation work (per ADR-0004).

## Alternatives Considered

**Validate everything agent-side**: Simpler CLI, but operators discover trivial config typos only after queueing a job and waiting for an agent — slow feedback and wasted agent capacity.

**Validate everything CLI-side**: Impossible for package and outcome checks (Tiers 2–3 require the package and the target at execution time), and would push agent-only concerns across the ADR-0005 write boundary.

**Control-Plane-hosted validation service**: Rejected. It would put migration logic and package/target access in the Control Plane, violating ADR-0004/ADR-0005 and the data-sovereignty boundary.

## Consequences

- Cheap failures surface at the cheapest point: config errors never reach the queue; unreachable targets never consume an agent; corrupt packages never begin import; incomplete imports are detected and reported.
- Every tier has a fixed owner, so validation logic has one home per failure class — new checks are added to a tier, not scattered.
- Module authors implement `ValidateAsync` as a side-effect-free pre-flight participant with fail-fast semantics (see [docs/validation.md](../validation.md) for the contract).
- `policies.validation.*` configuration (`continueOnError`, `workItemCountTolerance`, `failOnUnresolvedIdentities`, `sampleRate`) governs tier strictness; defaults are strict.

## Enforced By

- `.agents/10-contracts/specs/validation-safety-contract.md`
- `.agents/20-guardrails/domains/migration-rules.md`
- `.agents/20-guardrails/domains/module-rules.md`

## Related

- [ADR-0004](0004-control-plane-does-not-execute-migrations.md) — Control Plane coordination-only boundary
- [ADR-0005](0005-agent-only-package-write-access.md) — agent-only package/target access
- [docs/validation.md](../validation.md) — full check tables, failure behaviour, and report format
