# Session Hooks Skill

This skill provides hooks for recording session outcomes, triggering handoffs, and integrating with CI/CD in the ATDD workflow.

## Purpose

When invoked, this skill enables the **Orchestrator Agent** to:

1. Record session start and end timestamps.
2. Write structured session log entries to `Logs/atdd-sessions/`.
3. Signal session outcomes to downstream systems (CI gate, PR creation).
4. Enforce small-batch session discipline (one scenario per session).

## Usage

Load this skill in the **Orchestrator Agent** at the start and end of every ATDD session.

## Log File Location

```
Logs/atdd-sessions/YYYY-MM-DD-<feature-slug>-<scenario-slug>.log
```

Example:
```
Logs/atdd-sessions/2026-02-26-export-work-item-revisions-export-records-a-cursor.log
```

## Session States

| State | Meaning |
|---|---|
| `STARTED` | Session initiated, Specification Agent invoked |
| `SPECIFICATION_COMPLETE` | Feature file written |
| `TESTS_GENERATED` | Failing tests committed |
| `IMPLEMENTATION_COMPLETE` | Tests passing |
| `APPROVED` | Reviewer approved |
| `REJECTED` | Reviewer rejected — returning to Implementation Agent |
| `ESCALATED` | Ambiguity or architectural conflict — awaiting human |
| `SUCCESS` | Session committed and closed |
| `FAILURE` | Session failed after maximum retries |
