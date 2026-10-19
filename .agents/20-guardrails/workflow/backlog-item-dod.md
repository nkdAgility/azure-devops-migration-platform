# Backlog Item Definition of Done (L3)

> **Status:** Ratified by the operator on 2026-10-15 (ADR-0034). Effective once merged to `main`.

Apply this before closing a Backlog Item as done, in addition to [definition-of-done.md](definition-of-done.md) (L1) and [completion-protocol.md](completion-protocol.md) (L2).

Apply every section that the item touches. A section that does not apply is marked `N/A` with a one-line reason; never leave it blank.

## C1. Scope

- [ ] Every acceptance criterion is met, with evidence.
- [ ] Any spec `tasks.md` this item covers is reconciled: every task is either checked, or descoped with a reason and a new Backlog Item.

## C2. Connectors (Constitution rule 10)

- [ ] Simulated, AzureDevOpsServices and TeamFoundationServer are each implemented for every phase in scope that the API supports.
- [ ] No `Null*Adapter`, guard-away, or "not supported" path exists unless an ADR records the API limitation.
- [ ] `connector-coverage-check` passes.

## C3. Real-world proof

- [ ] A `SystemTest_Live` test covers the behaviour against real ADO, and against real TFS where TFS is in scope.
- [ ] Re-running, and interrupting then resuming, produce no duplicates (Constitution rule 5).
- [ ] Validate-phase checks cover the behaviour (ADR-0021).

## C4. Parity

- [ ] Every azure-devops-migration-tools behaviour mapped to this item is matched, improved, or deliberately dropped with a reason.
- [ ] The README parity table and module status matrix reflect the new state.

## C5. Operator usability

- [ ] The operator doc in `docs/` is updated, and a working example config is in `scenarios/`.
- [ ] New config options are in the configuration reference and the generated JSON Schema.
- [ ] CLI help matches behaviour, and the documented commands have been run exactly as written.
- [ ] Operator-facing errors say what went wrong and what to do next.
- [ ] Progress is visible in `queue --follow` and in the TUI.

## C6. Release and sign-off

- [ ] Release notes or a changelog entry are written.
- [ ] `nkda-core-definition-of-done` has been run against this checklist, and its report is linked from the Backlog Item.

## Enforcement

- The issue templates carry the pre-start assessment and the L2 and L3 checklists.
- The PR template carries the completion report.
- *Follow-ups* (Backlog Item #168):
  - a `dod` section in `session-evidence.schema.json`;
  - CI greps for the banned patterns and for unexempted `Null*Adapter`s.
