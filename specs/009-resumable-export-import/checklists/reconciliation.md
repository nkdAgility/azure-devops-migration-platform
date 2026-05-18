# Reconciliation Checklist: Resumable Export and Import

**Purpose**: Validate whether feature requirements/plans/tasks/checklists are reconciled with current implementation truth and supported by evidence.
**Created**: 2026-05-16
**Feature**: [spec.md](../spec.md), [plan.md](../plan.md), [tasks.md](../tasks.md)

## Requirement Completeness

- [x] CHK001 Are current implementation realities for import orchestration documented in spec/plan rather than left as historical gaps? [Completeness, Plan §Summary, Spec §User Story 2, Gap]
- [x] CHK002 Are completed foundational capabilities (cursor deletion, phase tracking, resume mode contract) explicitly reflected in task completion state? [Completeness, Tasks §Phase 1/4, Gap]
- [ ] CHK003 Does the spec define reconciliation requirements for keeping task status aligned with implemented code after major refactors? [Completeness, Spec §Requirements, Gap]
- [x] CHK004 Are required evidence artifacts (tests, feature files, docs updates) enumerated per story so completion can be proven without inference? [Completeness, Tasks §US1/US2/US3, Gap]

## Requirement Clarity

- [x] CHK005 Is “current implementation truth” defined with explicit evidence sources (code paths, tests, features, docs) to avoid subjective interpretation? [Clarity, Gap]
- [x] CHK006 Are “superseded tasks” criteria defined (renamed component, merged command surface, architectural replacement) so reviewers classify them consistently? [Clarity, Gap]
- [ ] CHK007 Is “missing evidence” quantified with objective thresholds (e.g., required proof types per task) rather than qualitative wording? [Clarity, Gap]
- [x] CHK008 Are update obligations for spec vs plan vs tasks vs checklists clearly separated to prevent duplicate or conflicting edits? [Clarity, Ambiguity]

## Requirement Consistency

- [x] CHK009 Do plan assertions about import implementation status align with current codebase state and no longer claim “unimplemented” behavior? [Consistency, Plan §Summary, Conflict]
- [ ] CHK010 Are planned type names and namespaces consistent with actual contract names used in code (e.g., job/resume models)? [Consistency, Plan §Project Structure, Conflict]
- [ ] CHK011 Are CLI requirements in plan/tasks consistent with the current command model (queue-centric surface) and not tied to superseded command classes? [Consistency, Plan §Project Structure, Tasks §US1/US3, Conflict]
- [x] CHK012 Does discrepancy status (“resolved”) remain consistent with canonical context docs and not leave unresolved architectural deltas? [Consistency, discrepancies.md §Discrepancies, .agents/30-context/domains/*]

## Acceptance Criteria Quality

- [x] CHK013 Are reconciliation acceptance criteria measurable (e.g., every implemented task mapped to Done/Superseded/Replaced with evidence links)? [Acceptance Criteria, Measurability, Gap]
- [x] CHK014 Do criteria define objective pass/fail rules for when a task may remain unchecked despite implementation (intentional defer vs stale backlog)? [Acceptance Criteria, Clarity, Gap]
- [ ] CHK015 Are criteria specified for when spec status can move from Draft to an updated state after reconciliation? [Acceptance Criteria, Spec §Status, Gap]

## Scenario Coverage

- [x] CHK016 Are requirements defined for the “implemented but undocumented” scenario (code exists, plan/tasks outdated)? [Coverage, Exception Flow, Gap]
- [x] CHK017 Are requirements defined for “documented but not evidenced” scenarios (task/checklist claims without tests/features/logs)? [Coverage, Exception Flow, Gap]
- [x] CHK018 Are requirements defined for “superseded design path” scenarios where architecture changed and original tasks should be retired or replaced? [Coverage, Alternate Flow, Gap]

## Edge Case Coverage

- [ ] CHK019 Are requirements defined for partially superseded tasks (only sub-steps replaced) to avoid all-or-nothing status errors? [Edge Case, Gap]
- [x] CHK020 Are requirements defined for cross-file drift where one artifact is updated (e.g., docs) but spec/plan/tasks remain stale? [Edge Case, Gap]

## Non-Functional Requirements

- [x] CHK021 Are traceability requirements explicit enough that each reconciliation decision is auditable to a source artifact reference? [Traceability, Gap]
- [ ] CHK022 Are governance/timing requirements defined for how often reconciliation must run (per PR, per milestone, pre-release) and by whom? [Non-Functional, Gap]

## Dependencies & Assumptions

- [x] CHK023 Are assumptions about repository structure and renamed modules/assemblies documented so reconciliation is resilient to ongoing refactors? [Assumption, Gap]
- [x] CHK024 Are dependencies on canonical context docs (`.agents/30-context/domains/*`) explicitly required when deciding whether discrepancies are truly resolved? [Dependency, discrepancies.md, Gap]

## Ambiguities & Conflicts

- [x] CHK025 Is there an explicit rule for resolving conflicts when spec intent and current implementation diverge (update spec vs file follow-on work)? [Ambiguity, Conflict, Gap]
- [x] CHK026 Are stale references in plan/tasks (file paths, project names, command names) required to be corrected or marked superseded with rationale? [Conflict, Plan §Project Structure, Tasks §Implementation, Gap]

## Reconciliation Extension (2026-05-17)

- [x] CHK027 Are all currently open tasks explicitly traceable to unmet requirement intent rather than stale task text? [Completeness, Tasks §Phase 2/3/4/5, Spec §User Stories, Gap]
- [ ] CHK028 Is the requirement for export force-fresh scenario coverage fully specified in executable Gherkin artifacts? [Completeness, Tasks §T005, Spec §US1 Scenario 4, Gap]
- [ ] CHK029 Are Both-mode resume requirements fully represented in CLI feature scenarios rather than only export/import-only scenarios? [Completeness, Tasks §T026, Spec §US3, Gap]
- [x] CHK030 Are remaining incomplete IDs consistent across spec, plan, and tasks documents? [Consistency, Spec §Reconciliation Snapshot, Plan §Reconciliation Snapshot, Tasks §Phase 5]
- [ ] CHK031 Is T015 status wording consistent with repository reality (implementation present) and scoped to the specific unmet requirement behavior? [Consistency, Tasks §T015, src/.../WorkItemImportOrchestrator.cs, Conflict]
- [x] CHK032 Are queue-centric resume/force-fresh requirements consistently reflected where legacy command tasks were marked superseded? [Consistency, Tasks §T006/T009/T031, src/.../QueueCommand.cs, src/.../QueueCommandSettings.cs]
- [ ] CHK033 Are evidence requirements for “incomplete” status measurable (what exact proof is missing, and how to close it)? [Acceptance Criteria, Tasks §T015/T034, Gap]
- [ ] CHK034 Is evidence freshness defined for verification tasks (dated command output, environment, and run scope)? [Measurability, Tasks §T034, Gap]
- [x] CHK035 Do superseded tasks include explicit replacement lineage and concrete replacement artifact references? [Traceability, Tasks §superseded entries]
- [ ] CHK036 Is there a minimum evidence granularity rule (path + behavior assertion, not just file existence)? [Traceability, Ambiguity, Gap]
- [ ] CHK037 Are checklist closure rules defined for “partially implemented” tasks to avoid binary complete/incomplete drift? [Clarity, Edge Case, Gap]
- [ ] CHK038 Are requirement-level observability claims (resume detected + skipped count) tied to explicit verifiable evidence statements? [Non-Functional, Spec §FR-011/SC-006, Gap]
