# Wave 0 Design: Specs 001-020 Task Reconciliation

## Problem

Open task counts across specs `001` through `020` include items that are likely stale, superseded, blocked by architecture decisions, or already implemented. This causes delivery planning and progress tracking to drift from repository reality.

## Goal

Produce an evidence-backed reconciliation pass for all currently-open tasks in specs `001-020`, so each open item has a valid current status and rationale.

## Scope

- In scope:
  - `specs/<001-020>*/tasks.md` files
  - Open checklist items (`- [ ] ...`) only
  - Status normalization to one of:
    - `complete`
    - `blocked`
    - `obsolete`/`superseded`
    - `incomplete` (still open)
  - Inline rationale updates in task lines
- Out of scope:
  - New feature implementation beyond what is required to prove/close stale tasks
  - Architectural policy changes
  - Work outside specs `001-020`

## Delivery Approach

### 1. Evidence-first classification

Each open task is reconciled only after checking concrete evidence:

- Source implementation presence (file/symbol/path)
- Test presence or execution evidence (where applicable)
- Documented architecture constraints (e.g., connector policies)
- Superseding task/spec evidence when obsolete

No task is closed based on assumption.

### 2. Deterministic execution order

- Process specs in numeric order, then lexical tie-break for duplicate prefixes (e.g., two `004-*` specs).
- Within a spec, process tasks in task-number order.

### 3. Small-batch checkpointing

- Commit after each completed spec reconciliation (or small logical spec cluster if tightly coupled).
- Keep commits focused and reviewable.

## Update Contract per Task

For each reconciled task line:

1. Set checkbox and status consistently.
2. Add short evidence rationale directly on the task line.
3. If blocked, include explicit blocker reason (not generic wording).
4. If obsolete/superseded, include replacement reference.

## Error Handling

- If evidence is ambiguous, keep task open and mark rationale as `needs verification`.
- If task language conflicts with current architecture constraints, mark blocked or obsolete with explicit constraint citation.
- Do not broaden scope to “fix everything” during reconciliation.

## Validation Strategy

- Run targeted tests for touched domains after each reconciliation checkpoint if status changes imply code reality claims.
- Run periodic broader/full-suite gates at wave milestones to ensure no regressions from any supporting code edits needed to resolve stale tasks.

## Outputs

1. Updated `tasks.md` files for specs `001-020`.
2. Clean status taxonomy across reconciled tasks.
3. Commit history segmented by reconciliation checkpoints.
4. Ready handoff for Wave 1 implementation planning.

## Risks and Mitigations

- Risk: False closure due to implicit assumptions  
  - Mitigation: mandatory evidence check before status change.
- Risk: Large noisy diffs  
  - Mitigation: per-spec checkpoint commits.
- Risk: Legacy tasks with invalid acceptance assumptions  
  - Mitigation: mark obsolete/superseded with explicit rationale rather than forcing implementation.

## Completion Criteria

Wave 0 is complete when:

1. Every open task in specs `001-020` has been reviewed.
2. Every status change includes inline rationale.
3. Remaining open tasks are intentionally open (not stale ambiguity).
4. Reconciliation commits are complete and reviewable.
