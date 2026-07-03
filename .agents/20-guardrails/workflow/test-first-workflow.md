# Tests-First Workflow Rules

Enforced rules for tests-first sessions. The full session model — phase table,
handoff detail, session-log template, and adoption paths — lives in
[`docs/test-first-workflow.md`](../../../docs/test-first-workflow.md).

One scenario = one session = one commit. A session is atomic: it completes all
phases or rolls back.

## Mandatory Rules

1. Phase order is fixed: Specification → Spec Hardening → Test Generation → Implementation → Review → Doc Sync. Phase N output is Phase N+1 input. No skipping phases.
2. Spec Hardening is mandatory for every approved spec and must run `.agents/skills/nkda-archimprove-red-team-review`, `.agents/skills/nkda-observability-contract`, and `.agents/skills/nkda-archcheck-architecture-review`. Blocking findings must be resolved in the spec before continuing.
3. Spec Hardening must record a Capability Seam Decision (canonical seam owner, public contract surface, adapter/extension policy, prohibited parallel entry points) for each concern in scope. Missing this block is a hard stop.
4. The architecture-perspectives gate against `.agents/20-guardrails/core/architecture-perspectives-ethos.md` is mandatory for touched scope even when review skills are not invoked.
5. Implementation is RED → GREEN → REFACTOR. The smallest failing behavioural test comes first; work started without a failing test must return to RED.
6. GREEN is not satisfied by a slice-only pass: the targeted test, the next wider relevant layers, and a fresh full-suite run must all pass with zero regressions. GREEN must not introduce runtime paths that bypass the declared canonical seam.
7. REFACTOR happens only on green and must consolidate duplicated concern logic back behind the canonical seam.
8. Implementation must run `.agents/commands/nkda-tddsn-autonomous.md` scoped to the subsystem; its six artefacts are mandatory session evidence.
9. Sessions must not span unrelated features or modify code outside the feature scope without logged justification.
10. `@ignore` / `[Ignore]` may be used for isolation during implementation only and must be removed before the Review verdict.
11. Every phase transition and return-to-earlier-phase is logged in `Logs/atdd-sessions/<session-id>.md` per the documented template.

## Reject Conditions

Reject any session that:

- writes production code before the intended failing test exists
- proceeds from specification directly to tests or implementation
- bypasses `.agents/commands/nkda-tddsn-autonomous.md` or omits any of its six artefacts
- claims GREEN without a fresh full-suite pass
- lacks the Capability Seam Decision or perspective evidence for touched scope

## Related

- [`docs/test-first-workflow.md`](../../../docs/test-first-workflow.md) — full session model
- [`docs/failing-tests-workflow.md`](../../../docs/failing-tests-workflow.md) — procedure when tests fail
- `.agents/20-guardrails/workflow/definition-of-done.md`
- `.agents/20-guardrails/workflow/testing-rules.md`
