---
name: session-hooks
description: Manages session lifecycle events for the ATDD Orchestrator, including log writes and phase transitions.
---

# Session Hooks — Skill Instructions

## Role

When this skill is active, manage session lifecycle events for the ATDD Orchestrator.

---

## Hook 1 — Session Start

At the beginning of every ATDD session, record:

```
[SESSION START]
Timestamp:   <ISO 8601 timestamp>
Feature:     <feature file path>
Scenario:    <scenario title>
Session ID:  <feature-slug>-<scenario-slug>-<YYYYMMDD-HHmmss>
```

Write this to `Logs/atdd-sessions/<session-id>.log`.

---

## Hook 2 — Phase Completion

After each phase completes, append to the session log:

```
[PHASE COMPLETE]
Phase:       <1 Specification | 2 Test Generation | 3 Implementation | 4 Review>
Timestamp:   <ISO 8601 timestamp>
Status:      COMPLETE | FAILED | ESCALATED
Detail:      <brief description of outcome or error>
```

---

## Hook 3 — Test Run Results

After the test suite runs (Phase 3 and Phase 4), append:

```
[TEST RUN]
Timestamp:   <ISO 8601 timestamp>
Tests Run:   <count>
Passed:      <count>
Failed:      <count>
Failed List: <comma-separated test method names, if any>
```

---

## Hook 4 — Session End

At the close of every session, append:

```
[SESSION END]
Timestamp:   <ISO 8601 timestamp>
Outcome:     SUCCESS | FAILURE | ESCALATED
Commit:      <git SHA or PR URL, if any>
```

---

## Hook 5 — Small Batch Enforcement

Before starting a session, verify:

1. Only one feature file / one scenario is being addressed.
2. No other session is currently in-progress for the same scenario (check for an open `.log` file with `STARTED` but no `SESSION END`).

If a concurrent session is detected, do not start. Report the conflict to the human.

---

## Hook 6 — CI Gate Signal

On `SUCCESS`, post a signal to CI indicating:
- Session ID
- Scenario title
- Test pass count
- Commit SHA

The CI gate will block merging until this signal is received for all acceptance scenarios associated with the PR.

On `FAILURE`, post a failure signal so the CI gate blocks the merge.

---

## Slug Generation Rules

Convert titles to slugs:
- Lowercase all characters.
- Replace spaces with hyphens.
- Remove all characters except `[a-z0-9-]`.
- Truncate to 60 characters.

Example: `"Export records a cursor after each revision is written"` → `export-records-a-cursor-after-each-revision-is-written`
