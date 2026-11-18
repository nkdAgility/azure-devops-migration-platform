## Outcome

A job whose agent cannot reach its source or target, cannot find the project, or lacks the permissions the job kind needs, fails before any work starts. The job stream says which endpoint failed and what to change.

## Why

- ADR-0021 (`docs/adr/0021-four-tier-validation-model.md:27`) requires Tier 1 (Connectivity): "verifies source/target reachability, project existence, and minimum permissions per job kind, plus package URI accessibility".
- No command implements it today. Found in the PR #222 review (CodeRabbit comment 4612477148 on `QueueCommand.cs`).
- Martin, 2026-11-18: not in PR #222. A check run by the CLI gives a false positive when the CLI can reach an endpoint the agent cannot. The check must run on the agent that will do the work.
- ADR-0021 describes Tier 1 as a CLI-side check. Its wording needs updating; the new wording needs Martin's approval.

## Acceptance criteria

- [ ] Before an Export, Prepare, Import, Inventory or Dependencies job does any work, the agent running it checks that each endpoint the job uses is reachable, that the project exists, and that the credentials have the minimum permissions for that job kind.
- [ ] A failed check fails the job before any package write. The job stream gets an actionable message that names the endpoint and the missing access, and contains no secrets.
- [ ] The checks cover Simulated, AzureDevOpsServices and TeamFoundationServer, including Windows authentication on-premises.
- [ ] Package URI accessibility is checked by the agent that writes or reads the package.
- [ ] ADR-0021 wording is updated to place Tier 1 on the agent, with wording approved by Martin.

## Scope

- Connectors: Simulated, AzureDevOpsServices, TeamFoundationServer
- Phases: Inventory, Export, Prepare, Import
- Agents: TfsMigrationAgent (net481), MigrationAgent (net10), shared multi-target code (Infrastructure.Agent, Abstractions*)
- Out of scope: Tier 0 structural checks, which stay in the CLI.

## Links

- Depends on:
- Decisions: Martin's ruling of 2026-11-18, recorded in `Logs/atdd-sessions/2026-11-16-cross-agent-sub-jobs-169.json`
- Spec:

---

## Pre-start assessment

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
