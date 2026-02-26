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
│  written             MSTest code    + production code     Reject  │
│                                                                   │
│  Orchestrator manages handoffs and logs the outcome              │
└──────────────────────────────────────────────────────────────────┘
```

### Phase 1 — Specification (Specification Agent)

**Input:** User story or feature request.  
**Output:** A Gherkin `.feature` file under `tests/acceptance/<area>/`.  
**Gate:** Feature file must describe at least one concrete, verifiable scenario. Ambiguous requirements surface to the human before proceeding.

### Phase 2 — Test Generation (Test Generation Agent)

**Input:** The `.feature` file from Phase 1.  
**Output:** Failing MSTest `[TestMethod]` skeletons under `tests/<Project>.Tests/`.  
**Gate:** Tests must compile and **fail** when run. A test that passes before implementation is wrong.

### Phase 3 — Implementation (Implementation Agent)

**Input:** The failing tests from Phase 2, the Planner's plan (if provided), and the full docs set.  
**Output:** Production code (and any required config, schema, or documentation updates) that causes the Phase 2 tests to pass.  
**Gate:** All tests from Phase 2 pass. No new architectural violations.

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
