# ATDD Workflow

This document defines the end-to-end Agentic Acceptance Test-Driven Development (ATDD) session workflow, agent handoff sequence, and session discipline for the Azure DevOps Migration Platform.

---

## Core Principle

**One acceptance scenario → one session → one commit.**

No session may encompass multiple acceptance scenarios. No code is committed without passing tests. No tests are written without acceptance criteria.

---

## Session Phases

```
┌──────────────────────────────────────────────────────────────────┐
│                      ATDD Session                                 │
│                                                                   │
│  1. Specification   2. Test Gen   3. Implementation   4. Review  │
│  ─────────────────→ ────────────→ ────────────────── → ────────  │
│  Feature file        Failing        Passing tests        Pass/   │
│  + 4 artifacts       Reqnroll       + unit tests         Reject  │
│                                                                   │
│  Orchestrator manages handoffs and logs the outcome              │
└──────────────────────────────────────────────────────────────────┘
```

### Phase 1 — Specification (Specification Agent)

**Input:** Human-authored draft intent description.  
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
Unit tests are plain MSTest `[TestClass]`/`[TestMethod]` classes, not Reqnroll step definitions. See [agents/testing-standards.md](../../agents/testing-standards.md) for the required coverage table.

### Phase 4 — Review (Reviewer Agent)

**Input:** The diff from Phase 3.  
**Output:** `Approved` or `Rejected` verdict with specific findings.  
**Gate:** All items in the Reviewer's rejection checklist are clear. See [.github/agents/reviewer.agent.md](../../.github/agents/reviewer.agent.md).

---

## Handoff Rules

- The Orchestrator enforces phase ordering. Phases cannot be skipped.
- If Phase 1 returns an ambiguity, the Orchestrator pauses and surfaces it to the human. It does not proceed.
- If Phase 2 tests pass before implementation, Phase 2 has failed. Return to Phase 2.
- If Phase 4 rejects, return to Phase 3. Do not restart from Phase 1 or 2 unless the feature definition itself is wrong.
- A session that fails Phase 4 more than twice should be escalated to a human reviewer.

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

1. The requirement must be written as a user story or feature description.
2. The acceptance area folder must exist under `tests/acceptance/`.
3. The ATDD workflow must not be initiated for a scenario that already has a passing test.

---

## Boundaries

| In scope | Out of scope |
|---|---|
| New module implementation | Hotfixes to passing production code |
| New acceptance scenario | Refactoring without a failing test |
| Adding a missing test | Changing test assertions without a requirement change |

---

## Adoption Phases

**Phase 1 — Manual**  
Humans write acceptance criteria → agents are invoked interactively for each phase.

**Phase 2 — Semi-Autonomous**  
Orchestrator runs small sessions with minimal human input. Human approves Phase 4 verdicts.

**Phase 3 — Automated PRs**  
GitHub Copilot coding agent produces feature PRs from ATDD sessions end-to-end. Human reviews PRs.
