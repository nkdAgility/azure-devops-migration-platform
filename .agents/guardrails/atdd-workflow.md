# ATDD Workflow

This document defines the end-to-end Agentic Acceptance Test-Driven Development (ATDD) session workflow, agent handoff sequence, and session discipline for the Azure DevOps Migration Platform.

---

## Core Principle

**One acceptance scenario → one session → one commit.**

No session may encompass multiple acceptance scenarios. No code is committed without passing tests. No tests are written without acceptance criteria.

---

## Session Phases

```
┌──────────────────────────────────────────────────────────────────────────────┐
│                            ATDD Session                                       │
│                                                                               │
│  1. Specification  2. Test Gen  3. Implementation  4. Review  5. Doc Sync    │
│  ────────────────→ ───────────→ ────────────────── → ──────── → ──────────   │
│  Feature file       Failing       Passing tests      Pass/     Docs updated  │
│  + 4 artifacts      Reqnroll      + unit tests       Reject    discrepancies │
│                                                                resolved       │
│  Orchestrator manages handoffs and logs the outcome                          │
└──────────────────────────────────────────────────────────────────────────────┘
```

### Phase 1 — Specification (Specification Agent)

**Input:** The canonical input to Phase 1 is the `spec.md` acceptance scenarios produced by the SpecKit `speckit.specify` agent for this feature. When a `spec.md` exists under `specs/<feature>/`, the Specification Agent must use those user stories and acceptance scenarios as the authoritative starting point and must not ask the human to re-describe the intent. When no `spec.md` exists, fall back to a human-authored draft intent description.  
**Output:** All four specification artifacts — intent description, Gherkin `.feature` file, architecture constraints, and non-functional acceptance criteria.  
**Human gate:** The human must explicitly approve the complete specification set before Phase 2 begins.

Phase 1 is a **collaborative four-step cycle** at each of four stages:

| Stage | Artifact | Cycle |
|---|---|---|
| Intent Definition | Intent description | Human drafts → Agent critiques → Human decides → Agent refines |
| Behaviour Specification | Gherkin `.feature` file | Agent generates → Agent finds gaps → Human approves scenarios |
| Architecture Specification | Constraint notes (inline comments) | Agent identifies integration points → checks guardrails → Human approves |
| Acceptance Criteria | Non-functional thresholds | Agent suggests measurables → Human approves |

After all four stages have human-approved artifacts, the Specification Agent runs a **consistency validation**: checks all four artifacts for clarity, testability, scope, terminology, completeness, and conflicts. Only after this validation passes does the agent signal readiness.

**Scope constraint:** One session = one thin vertical slice = one scenario. If the requirement implies more than one independently deliverable behaviour, split it before starting Phase 1.

**Gate:** All four artifacts are complete, consistent, and human-approved. `"human_approved": true` must be present in the Specification Agent's output JSON.

### Phase 2 — Test Generation (Test Generation Agent)

**Input:** The `.feature` file from Phase 1.  
**Output:** Failing Reqnroll `[Binding]` step definition files (`*Steps.cs` + `*Context.cs`) under `tests/<Project>.Tests/`.  
**Gate:** Steps must compile and all scenarios must be **pending or failing** when run. A step that passes before implementation is wrong.

### Phase 3 — Implementation (Implementation Agent)

**Input:** The failing Reqnroll step definition files from Phase 2 and the full docs set.  
**Output:** Production code that causes the Phase 2 Reqnroll steps to pass, **plus unit tests** for any logic that has more than one code path, involves calculation or transformation, or could fail in ways the acceptance scenario would not catch.  
**Gate:** All Reqnroll steps pass. All unit tests pass. Every new method with branching logic has at least one unit test per path. No new architectural violations.

**Unit test placement:**
```
tests/<ProjectName>.Tests/<Area>/<ClassName>Tests.cs
```
Unit tests are plain MSTest `[TestClass]`/`[TestMethod]` classes, not Reqnroll step definitions. See [.agents/guardrails/testing-standards.md](./testing-standards.md) for the required coverage table.

### Phase 4 — Review (Reviewer Agent)

**Input:** The diff from Phase 3.  
**Output:** `Approved` or `Rejected` verdict with specific findings.  
**Gate:** All items in the Reviewer's rejection checklist are clear. See [.github/agents/reviewer.agent.md](../../.github/agents/reviewer.agent.md).

### Phase 5 — Documentation Sync (Implementation Agent + human gate)

**Input:** The diff from Phase 3 and the spec's `specs/<feature>/discrepancies.md`.  
**Output:** All canonical docs updated to reflect the implementation:
- `/docs/*.md` files for any behaviour, CLI, configuration, or architectural changes
- `.agents/context/*.md` files for any changes to package format, streaming, checkpointing, identity, job contracts, or artefact store abstractions
- `.agents/guardrails/*.md` files if a guardrail was affected
- `.specify/memory/constitution.md` if a principle was introduced or changed
- `.vscode/launch.json` if a new CLI command was added
- `specs/<feature>/discrepancies.md` updated to mark all items `Resolved` or `N/A`
- `analysis/pending-actions.md` reviewed and any items now implemented removed

**Gate (blocking — cannot be skipped):** Before the session log can be finalised:
1. Every item in `specs/<feature>/discrepancies.md` is marked `Resolved` or `N/A`.
2. Every doc-task in `tasks.md` that names a canonical doc file is marked `[X]`.
3. `analysis/pending-actions.md` has been reviewed and stale resolved items removed.
4. If there are no doc changes, the agent must explicitly state "no documentation changes required" with a written justification — silence is not acceptable.

**Human gate:** The human must confirm the documentation sync output is complete before the session log is finalised.

---

## Handoff Rules

- The Orchestrator enforces phase ordering. Phases cannot be skipped.
- If Phase 1 returns an ambiguity, the Orchestrator pauses and surfaces it to the human. It does not proceed.
- If Phase 2 tests pass before implementation, Phase 2 has failed. Return to Phase 2.
- If Phase 4 rejects, return to Phase 3. Do not restart from Phase 1 or 2 unless the feature definition itself is wrong.
- A session that fails Phase 4 more than twice should be escalated to a human reviewer.
- Phase 5 cannot be skipped. A session that reaches Phase 4 approval must complete Phase 5 before it is logged as `SUCCESS`.

Phase 5 — Documentation Sync MUST update the session log with a `"doc_sync"` field (see Session Logging).

---

## Session Logging

Each session must produce a log entry in `Logs/atdd-sessions/`:

```
YYYY-MM-DD-<feature>-<scenario-slug>.log
```

Log contents:
```
Session Start: <timestamp>
Feature:       <feature file path>
Scenario:      <scenario title>

Phase 1 — Specification Agent:   COMPLETE | ESCALATED
Phase 2 — Test Generation Agent: COMPLETE | FAILED
Phase 3 — Implementation Agent:  COMPLETE | INCOMPLETE
Phase 4 — Reviewer Agent:        APPROVED | REJECTED (<reason>)
Phase 5 — Documentation Sync:    COMPLETE | SKIPPED (justification required)

Doc Sync:
  discrepancies.md all resolved:  YES | NO
  doc-tasks all checked:          YES | NO
  pending-actions.md updated:     YES | NO
  no-change justification:        <text or N/A>

Tests Run:    <count>
Tests Passed: <count>
Tests Failed: <count>

Session End:  <timestamp>
Outcome:      SUCCESS | FAILURE
Commit:       <sha or PR>
```

---

## Preconditions

Before starting a session:

1. The requirement must be written as a user story or feature description. The preferred source is a SpecKit `spec.md` under `specs/<feature>/`. If none exists, document the intent before proceeding.
2. The acceptance area folder must exist under `features/` (see `.agents/guardrails/acceptance-test-format.md` for naming rules).
3. The ATDD workflow must not be initiated for a scenario that already has a passing test.

---

## Boundaries

| In scope | Out of scope |
|---|---|
| New module implementation | Hotfixes to passing production code |
| New acceptance scenario | Refactoring without a failing test |
| Adding a missing test | Changing test assertions without a requirement change |
| Doc sync on spec completion | Doc sync mid-spec (only required at session close) |

---

## Adoption Phases

**Phase 1 — Manual**  
Humans write acceptance criteria → agents are invoked interactively for each phase.

**Phase 2 — Semi-Autonomous**  
Orchestrator runs small sessions with minimal human input. Human approves Phase 4 verdicts.

**Phase 3 — Automated PRs**  
GitHub Copilot coding agent produces feature PRs from ATDD sessions end-to-end. Human reviews PRs.
