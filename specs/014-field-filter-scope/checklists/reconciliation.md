# Reconciliation Requirements Checklist: Field Filter Scope Repository Alignment

**Purpose**: Validate that reconciliation requirements clearly define canonical task-status formatting and alignment with repository truth sources.
**Created**: 2026-05-17
**Feature**: [spec.md](../spec.md), [plan.md](../plan.md), [tasks.md](../tasks.md)

## Requirement Completeness

- [ ] CHK001 Are canonical status tokens explicitly defined as a closed vocabulary (including case and separators) for all task lines and summary sections? [Completeness, Gap, Tasks §Format, Spec §Current status, Plan §Current status]
- [ ] CHK002 Are requirements defined for how status token canonicalization is applied when legacy forms (e.g., `complete/superseded`, `partial`, `incomplete`) appear in existing records? [Completeness, Ambiguity, Tasks §Phase 4, Spec §Status, Plan §Current status]
- [ ] CHK003 Are superseded-task requirements defined to require a source citation under `specs/<...>/tasks.md` (not only `spec.md`) plus rationale text? [Completeness, Gap, Spec §Completed because superseded, Tasks §Phase 4]

## Requirement Clarity

- [ ] CHK004 Is checkbox/status coupling explicitly specified (e.g., `[X]` must map to canonical done token, `[ ]` must map to canonical incomplete token) with no interpretation left to reviewers? [Clarity, Tasks §Format, Tasks §Phase 1-6]
- [ ] CHK005 Is “repository truth alignment” defined with objective source precedence (which file is authoritative when spec/plan/tasks disagree)? [Clarity, Gap, Spec §Contradictions and reconciliation, Plan §Contradictions and reconciliation]
- [ ] CHK006 Are incomplete-evidence notes required to follow a measurable format (command/run scope, date, failing signal, and current confidence)? [Clarity, Gap, Spec §Verification evidence, Plan §Verification evidence, Tasks T032-T033]

## Requirement Consistency

- [ ] CHK007 Do requirements enforce consistency between each task checkbox and its trailing `Status:` token across all phases? [Consistency, Tasks §Phase 1-6]
- [ ] CHK008 Do requirements enforce that “Remaining incomplete work (IDs)” in spec and plan must match the incomplete task IDs in `tasks.md` exactly? [Consistency, Spec §Remaining incomplete work, Plan §Remaining incomplete work, Tasks §Phase 3-6]
- [ ] CHK009 Do requirements define how discrepancies status claims (“resolved”) must remain consistent with open evidence called out in spec/plan/tasks? [Consistency, Conflict, discrepancies.md §Status, Spec §Contradictions and reconciliation]

## Acceptance Criteria Quality

- [ ] CHK010 Are acceptance criteria for reconciliation quality measurable (e.g., zero non-canonical status tokens, zero checkbox/token mismatches, zero uncited superseded tasks)? [Acceptance Criteria, Measurability, Gap]
- [ ] CHK011 Is there a defined pass/fail rule for spec/plan/checklist sync that can be evaluated without subjective judgment? [Acceptance Criteria, Measurability, Gap]

## Scenario & Edge Case Coverage

- [ ] CHK012 Are requirements defined for stale-path tasks whose implementation moved (legacy path in task text vs current repository path) without losing truthful status reporting? [Coverage, Edge Case, Spec §Contradictions and reconciliation, Plan §Contradictions and reconciliation]
- [ ] CHK013 Are requirements defined for tasks marked complete or superseded but missing required evidence or citations? [Coverage, Edge Case, Gap, Tasks §Phase 4-6, Spec §Verification evidence]

## Dependencies, Assumptions & Traceability

- [ ] CHK014 Are assumptions about authoritative evidence sources (build logs, targeted tests, full test runs) explicitly documented and cross-referenced to task status decisions? [Dependencies, Assumption, Spec §Verification evidence, Plan §Verification evidence, Tasks T032-T033]
- [ ] CHK015 Is traceability explicitly required so each reconciliation claim maps to at least one source section in spec/plan/tasks/checklist? [Traceability, Gap, Spec §Current status, Plan §Current status, Tasks §Format]

## Reconciliation execution notes

- Run date: 2026-05-17.
- `/speckit.analyze` and `/speckit.checklist` were executed for this spec.
- Open task blockers aligned after reconciliation: T005, T010, T013, T016, T018, T023, T025, T027, T029, T030, T032, T033.
