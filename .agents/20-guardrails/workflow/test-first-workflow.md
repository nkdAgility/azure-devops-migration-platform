# Tests-First Workflow

Rules for tests-first sessions. One scenario = one session = one commit.

---

## Core Principle

The repository delivery model is tests-first.

- TDD is the primary tests-first design and implementation method. It is the mechanism that drives RED → GREEN → REFACTOR.
- ATDD is used for intent: it captures expected behaviour in scenario language and acceptance assets before implementation.

Every feature change follows: Specification → Spec Hardening → Test Generation → Implementation → Review → Doc Sync. A session is atomic — it either completes all phases or rolls back.

After ATDD intent is approved and before tests or production changes proceed, the session must harden the spec by running all of the following against the feature scope:

- `.agents/skills/nkda-archimprove-red-team-review`
- `.agents/skills/nkda-observability-contract`
- `.agents/skills/nkda-archcheck-architecture-review`

Blocking findings from those runs must be resolved in the specification before continuing.

Specification hardening must also record a **Capability Seam Decision** for each concern in scope:

- canonical seam owner
- public reusable contract surface
- adapter/extension policy responsibilities
- prohibited parallel runtime entry points

Missing this decision block is a hard stop.

Specification hardening must also complete a non-skill architecture-perspectives gate for touched scope using:

- `.agents/20-guardrails/core/architecture-perspectives-ethos.md`

This gate is mandatory even when architecture-review skills are not invoked.

Implementation is mandatory RED → GREEN → REFACTOR:

- RED: add or update the smallest failing behavioural test first.
- GREEN: make the minimal production change required to turn that test green, then progressively broaden verification through the next wider relevant test layers before the final full-suite run so the repository is restored to an all-green state. GREEN must not introduce alternate runtime paths that bypass the declared canonical seam.
- REFACTOR: improve the design only after the relevant tests are green, while keeping them green. REFACTOR must consolidate duplicated concern logic back behind the canonical seam.

GREEN is not satisfied by a slice-only pass. The local failing test proves the intended behaviour; progressively wider layers reduce feedback latency while rebuilding confidence; the fresh full-suite pass is the final no-regression gate before REFACTOR or any completion claim.

No addition, bug fix, or behaviour change may skip the failing-test-first step. If work starts from existing code without a failing test, the session is out of compliance and must return to RED before continuing.

ATDD intent artefacts and TDD implementation artefacts serve different purposes and must not be conflated: acceptance scenarios express what should happen; TDD drives how the design is proven incrementally.

Implementation execution must also run `.agents/commands/nkda-tddsn-autonomous.md` scoped to the subsystem under change. Its six output artefacts are mandatory implementation evidence for the session.

---

## Session Phases

| Phase | Owner | Output | Gate |
| --- | --- | --- | --- |
| 1. Specification | speckit.specify | `spec.md` | Human approval of spec |
| 2. Spec Hardening | `.agents/skills/nkda-archimprove-red-team-review` + `.agents/skills/nkda-observability-contract` + `.agents/skills/nkda-archcheck-architecture-review` | Reviewed and corrected `spec.md` plus review outputs | All blocking architecture, observability, and red-team findings resolved or explicitly approved by the human before continuing |
| 2a. Perspectives Gate | Guardrail-driven review against `.agents/20-guardrails/core/architecture-perspectives-ethos.md` | Perspective evidence for touched scope | Pass required for all six perspectives before Test Generation |
| 3. Test Generation | parse-criteria + test-templates | `.feature` + `*Steps.cs` | Tests compile and fail for the intended missing behaviour (RED) |
| 4. Implementation | `.agents/commands/nkda-tddsn-autonomous.md` | Production code plus six NKDA TDD Safety Net artefacts | Minimal code turns the relevant tests green, a fresh full-suite run is green, refactor stays green, and the command produces all required outputs |
| 5. Review | review skill | Verdict in session log | Pass verdict (no blockers) |
| 6. Doc Sync | Manual/agent | Updated docs | Docs match implementation |

---

## Handoff Rules

- Phase N output is Phase N+1 input. No skipping phases.
- Phase 2 is mandatory for every approved spec; do not proceed from specification directly to tests or implementation.
- Phase 3 must preserve the RED → GREEN → REFACTOR order; writing production code before the intended failing test exists is a workflow violation.
- Phase 3 and Phase 4 must treat GREEN as a widening chain of proofs: the targeted red test now passes, the next wider relevant layers pass, and a fresh full-suite run passes with zero regressions.
- Phase 4 must run `.agents/commands/nkda-tddsn-autonomous.md`; bypassing the command or omitting any of its six artefacts is a workflow violation.
- If Phase 3 or Phase 4 reveals spec gaps → return to Phase 1, then re-run Phase 2 before continuing.
- If Review finds issues → return to Phase 4 (fix, re-review).
- Each return-to-earlier-phase is logged in session log.

---

## Session Logging

File: `Logs/atdd-sessions/<session-id>.md`

Format:

```markdown
# Session: <session-id>
Scenario: <feature>/<scenario-name>
Started: <ISO 8601>

## Phase 1: Specification
- Status: Complete
- Output: specs/<feature>/spec.md

## Phase 2: Spec Hardening
- Status: Complete
- Reviews: nkda-archimprove-red-team-review, nkda-observability-contract, nkda-archcheck-architecture-review
- Findings: <none or list>

## Phase 3: Test Generation
- Status: Complete
- Tests: <list of generated test files>

## Phase 4: Implementation
- Status: Complete
- Command: .agents/commands/nkda-tddsn-autonomous.md
- Files changed: <list>
- Artefacts:
  - .output/nkda-tddsn/<subsystem>/01-assessment.md
  - .output/nkda-tddsn/<subsystem>/02-target-test-suite.md
  - .output/nkda-tddsn/<subsystem>/03-architecture-update.md
  - .output/nkda-tddsn/<subsystem>/04-rebuild-plan.md
  - .output/nkda-tddsn/<subsystem>/05-implementation-summary.md
  - .output/nkda-tddsn/<subsystem>/06-verification.md

## Phase 5: Review
- Status: Pass
- Findings: <none or list>

## Phase 6: Doc Sync
- Status: Complete
- Docs updated: <list>

Completed: <ISO 8601>
```

---

## Preconditions

- Working directory clean (no uncommitted changes unrelated to session).
- Branch exists for feature (e.g. `024-teams-module`).
- Guardrails read and acknowledged before Phase 2.
- The four mandatory assets are available before execution begins:
  - `.agents/skills/nkda-archimprove-red-team-review`
  - `.agents/skills/nkda-observability-contract`
  - `.agents/skills/nkda-archcheck-architecture-review`
  - `.agents/commands/nkda-tddsn-autonomous.md`

---

## Boundaries

- One session = one scenario (one `.feature` file or one scenario within).
- Sessions MUST NOT span multiple unrelated features.
- Sessions MUST NOT modify code outside the feature scope without explicit justification logged.
- `@ignore` / `[Ignore]` may be used during Phase 4 for isolation — MUST be removed before Phase 5 verdict.

---

## Adoption

Sessions can start at any phase if prior phases are already complete:

- Spec exists but has not been hardened → start at Phase 2.
- Spec exists, hardening is complete, and tests exist → start at Phase 4.
- Implementation done + needs review → start at Phase 5.
- All phases done but docs stale → Phase 6 only.




