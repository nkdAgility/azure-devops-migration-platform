# ATDD Workflow

Rules for Acceptance Test-Driven Development sessions. One scenario = one session = one commit.

---

## Core Principle

Every feature change follows: Specification → Test Generation → Implementation → Review → Doc Sync. A session is atomic — it either completes all phases or rolls back.

---

## Session Phases

| Phase | Owner | Output | Gate |
|-------|-------|--------|------|
| 1. Specification | speckit.specify | `spec.md` | Human approval of spec |
| 2. Test Generation | parse-criteria + test-templates | `.feature` + `*Steps.cs` | Tests compile (red — failures expected) |
| 3. Implementation | speckit.implement | Production code | All tests green + build clean |
| 4. Review | review skill | Verdict in session log | Pass verdict (no blockers) |
| 5. Doc Sync | Manual/agent | Updated docs | Docs match implementation |

---

## Handoff Rules

- Phase N output is Phase N+1 input. No skipping phases.
- If Phase 3 reveals spec gaps → return to Phase 1 (update spec, regenerate tests).
- If Review finds issues → return to Phase 3 (fix, re-review).
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

## Phase 2: Test Generation
- Status: Complete
- Tests: <list of generated test files>

## Phase 3: Implementation
- Status: Complete
- Files changed: <list>

## Phase 4: Review
- Status: Pass
- Findings: <none or list>

## Phase 5: Doc Sync
- Status: Complete
- Docs updated: <list>

Completed: <ISO 8601>
```

---

## Preconditions

- Working directory clean (no uncommitted changes unrelated to session).
- Branch exists for feature (e.g. `024-teams-module`).
- Guardrails read and acknowledged before Phase 3.

---

## Boundaries

- One session = one scenario (one `.feature` file or one scenario within).
- Sessions MUST NOT span multiple unrelated features.
- Sessions MUST NOT modify code outside the feature scope without explicit justification logged.
- `@ignore` / `[Ignore]` may be used during Phase 3 for isolation — MUST be removed before Phase 4 verdict.

---

## Adoption

Sessions can start at any phase if prior phases are already complete:
- Spec exists + tests exist → start at Phase 3.
- Implementation done + needs review → start at Phase 4.
- All phases done but docs stale → Phase 5 only.
