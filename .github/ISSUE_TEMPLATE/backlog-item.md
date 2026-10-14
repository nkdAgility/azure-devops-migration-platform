---
name: Backlog Item
about: A unit of work. It is assessed pre-start for readiness and size, and broken into sub-issues if it is too big.
title: ""
labels: ["backlog-item", "needs-assessment"]
---

## Outcome

<!-- What an operator can do, or what is fixed, when this is done. -->

## Why

<!-- The gap or problem, with evidence: path:line, command output, doc quote, README parity row. -->

## Acceptance criteria

<!-- Testable. No more than 5. If you need more, it is probably too big. -->

- [ ] 
- [ ] 

## Scope

- Connectors: Simulated · AzureDevOpsServices · TeamFoundationServer
- Phases: Inventory · Export · Prepare · Import · Validate
- Agents: <!-- TfsMigrationAgent (net481: TFS Object Model, Infrastructure.TfsObjectModel) · MigrationAgent (net10: ADO, Simulated) · shared multi-target code (Infrastructure.Agent, Abstractions*) -->
- Out of scope:

## azure-devops-migration-tools equivalent

<!-- The processor or tool this matches, if any. Delete the table if none applies. -->

| Behaviour in Migration Tools | Match / improve / drop (why) |
| --- | --- |
| | |

## Links

- Depends on:
- Decisions:
- Spec: <!-- specs/NNN-name/, if any -->

---

## Pre-start assessment

<!-- Completed before any work starts. See .agents/20-guardrails/workflow/backlog-item-workflow.md §0. -->

**Ready?**
- [ ] Outcome and acceptance criteria are clear and testable
- [ ] No open questions. Any that existed are raised as Decisions and ruled.
- [ ] Connectors, phases, agent placement (net481 TfsMigrationAgent vs net10 MigrationAgent) and the Migration Tools equivalent are identified
- [ ] Dependencies are done

**Right-sized?**
- [ ] One PR can meet the full DoD, including the live E2E
- [ ] One concern
- [ ] No more than 5 acceptance criteria
- [ ] Independently testable and shippable
- [ ] No more than about 2 days of effort

**Result:** ready / blocked (link Decision) / broken down into #, #, #

<!-- If broken down:
     1. Create vertical-slice sub-issues with `gh issue create --parent <this>`.
     2. Check that every acceptance criterion is covered by at least one sub-issue.
     3. Label this item `broken-down` and keep it open.
     4. Close it only when all sub-issues are closed, after a final live E2E of the whole outcome (§0.5).
     The DoD below then applies to the sub-issues, not to this item. -->

**Parent close** (only if broken down)
- [ ] All sub-issues are closed
- [ ] Every acceptance criterion is met by a sub-issue that was done, or is recorded as dropped with a reason
- [ ] A final live end-to-end migration of the whole outcome passes
- [ ] The README parity table and module matrix reflect the outcome

---

## Definition of Done

**Completion protocol (L2)**
- [ ] 0. Up to date with main
- [ ] 1. Builds successfully (Debug and Release; `build.ps1 install`)
- [ ] 2. Full test suite passes; RED → GREEN evidence
- [ ] 3. Real end-to-end migration completes correctly (live scenario; target state inspected)
- [ ] 4. Architecture compliance validated
- [ ] 5. Checked against the acceptance criteria above
- [ ] 6. Session-only artefacts cleaned up
- [ ] Completion report in the PR: what was done, how it was tested, docs, confidence %, what was not done
- [ ] Auto-merge on at 90% confidence or above; otherwise off

**Backlog Item DoD (L3)**: mark each line N/A with a reason if it does not apply
- [ ] Every acceptance criterion met, with evidence; related `tasks.md` reconciled
- [ ] Simulated, ADO and TFS implemented for the phases in scope; no `Null*Adapter` without an ADR
- [ ] `SystemTest_Live` covers it (ADO, and TFS if in scope)
- [ ] Re-running and resuming produce no duplicates; Validate-phase checks cover it
- [ ] Migration Tools mapping resolved; README parity table and module matrix updated
- [ ] Operator doc, example config, config reference and schema updated
- [ ] Documented commands run as written; errors are actionable; progress visible
- [ ] Release notes written
- [ ] `nkda-core-definition-of-done` report linked
