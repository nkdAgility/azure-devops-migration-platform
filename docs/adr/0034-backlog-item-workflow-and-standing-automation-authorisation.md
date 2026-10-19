# ADR 0034 — Backlog Item Workflow and Standing Automation Authorisation

## Status

Accepted. Operator consent was given in session on 2026-10-14 ("one target project per worktree, yes authorised, build slice 1") and the rules were ratified on 2026-10-15, including the manager model: one task = one PR = one worktree, with a manager session checking in on every running task at least every 20 minutes.

## Context

An assessment of the platform as a replacement for azure-devops-migration-tools found three problems:

- **No tracked gap list.** The platform has a large gap against Migration Tools, but there was no record of it. Specs stalled part-way through, with stale `tasks.md` checkboxes.
- **No capability-level "done".** The Definition of Done covers individual code changes only.
- **Unenforced live testing.** Live end-to-end testing was effectively optional; only 14 `SystemTest_Live` tests exist.

The operator wants to work through the backlog with agents running in parallel git worktrees. That requires a few things to be explicit:

- a shared work-item model;
- a pre-start sizing gate;
- isolated live test environments;
- a narrowly scoped standing authorisation that relaxes Constitution rule 11 ("never commit or push unless asked").

## Decision

1. **Backlog Items are the only work type.** Questions that need a ruling become **Decisions**. Both use `.github/ISSUE_TEMPLATE/`.
2. **Pre-start assessment.** Every Backlog Item gets a readiness and sizing check before work starts: no more than 5 acceptance criteria, one concern, one PR, independently shippable, and no more than about 2 days of work. It must also state its agent placement, verified against the code: net481 `TfsMigrationAgent` or net10 `MigrationAgent`.
   - An item that is too big is split into vertical-slice **sub-issues**.
   - The parent stays open, labelled `broken-down`, until every child is closed.
   - The rules are in `.agents/20-guardrails/workflow/backlog-item-workflow.md`, `completion-protocol.md` and `backlog-item-dod.md`.
3. **Definition of Done levels.** Each PR must meet L1, the existing change-level DoD. It must also follow the L2 Completion Protocol:
   - sync with main;
   - build;
   - pass the full test suite;
   - run a real live E2E migration;
   - pass an architecture check;
   - be checked against the original ask;
   - clean up after itself;
   - include a five-part completion report.

   Every item must also meet the L3 Backlog Item DoD that applies to it.
4. **Live test environment.** Live tests use the same organisations and Entra ID approach as azure-devops-migration-tools. Projects are named per run (`adpe2e-<issue>-<slot>`), with prefix and organisation guards, and cleanup is scoped to the run. The rules are in `.agents/20-guardrails/workflow/live-test-environment.md`.
5. **Standing authorisation `backlog-automation`.** This is recorded in `.agents/10-contracts/consent-policy.yaml` under the new optional `standing_authorisations` section, and the schema is extended to allow it.
   - **Scope:** only `nkda-backlog-groom-workflow`, `nkda-backlog-deliver-workflow`, `nkda-backlog-item-create` and `nkda-backlog-item-prestart`.
   - **Permitted actions:** create and label issues; commit, push and open a PR for the item being delivered; and enable auto-merge only when an independent verifier's confidence is at least 90% and every completion step passed.
   - **Explicitly excluded:** Class C changes inside an item, force-pushes, and direct pushes to main.

## Consequences

- Constitution rule 11 is relaxed **only** within the scope listed in decision 5. Everywhere else it is unchanged.
- An item without a live E2E run is reported as `BLOCKED`, not done. Live runs authenticate with an Entra access token used in place of a PAT: the shared test app through GitHub OIDC in CI, and the developer's `az login` locally. No PAT is stored.
- Running agents in parallel needs per-run isolation. The rules provide it through:
  - per-run projects;
  - per-run Control Plane ports and storage;
  - area-overlap scheduling, so two items that touch the same area never run at the same time.
- Follow-ups are tracked as Backlog Items:
  - DoD enforcement in CI (#168);
  - the completion-protocol and live E2E skills (#165, #166);
  - the parent rollup (#167).

## Related

- [ADR-0031](0031-ci-enforced-governance-gates.md), [ADR-0033](0033-machine-readable-session-evidence.md)
- `.agents/20-guardrails/workflow/definition-of-done.md` (L1)
