# Live Test Environment

> **Status: DRAFT, pending operator approval (Class C governance change).**

This file defines how live end-to-end runs work. Completion Protocol step 3 in [backlog-item-workflow.md](backlog-item-workflow.md) and `nkda-backlog-deliver-workflow` both follow it.

It adopts the live-test infrastructure that azure-devops-migration-tools uses:

- PR #3221 on `main`: Entra ID auth, the Bicep app, `Setup-TestEnvironment.ps1`, the preflight tests, and `integration-tests.yml`.
- Branch `test/e2e-regression-2026-10`: the `tools/e2e/` seed, assert and cleanup modules.

Both repositories use the same organisations, identity and conventions. Each has its own name prefix so that one repository's cleanup never touches the other's objects.

---

## 1. Organisations

| Role | Organisation | Shared with Migration Tools |
| --- | --- | --- |
| Source | `https://dev.azure.com/nkdagility-adomt-source` | Yes |
| Target | `https://dev.azure.com/nkdagility-adomt-target` | Yes |

Live runs may only touch these two organisations. Any other URL is refused (see §5).

## 2. Authentication

**Target state: Entra ID, with no PATs. This is the same as Migration Tools.**

- **Local:** the operator runs `az login` with an account that is Project Collection Administrator in both organisations.
- **CI:** GitHub OIDC workload-identity federation through `azure/login@v2`. It uses the same Entra app as Migration Tools, with an extra federated credential whose subject is `repo:nkdAgility/azure-devops-migration-platform:environment:integration-tests`.
  - Repository variables (these are ids, not secrets): `AZURE_TEST_CLIENT_ID`, `AZURE_TEST_TENANT_ID`, `ADO_TEST_SOURCE_COLLECTION`, `ADO_TEST_TARGET_COLLECTION`.
- **Getting a token:** `az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798`. This is a **Bearer** token, not a PAT.

**Interim state: PATs.** The platform currently supports only `AuthenticationType.AccessToken` (PAT, Basic auth) and `Windows`; see `EndpointAuthenticationOptions.cs`. Until the Backlog Item *"Entra ID / Bearer authentication"* ships:

- `SOURCE_PAT` and `TARGET_PAT` are read from the operator's user environment variables.
- They are never written to files, logs, PRs, issues or reports.
- They are never passed as an argument to a subagent. Subagents inherit them from the environment.

Changing a live run from PAT to Entra is a change to this file only. The workflow does not change.

## 3. Environment variables

| Variable | Set by | Value |
| --- | --- | --- |
| `SOURCE_ORG_URL` | operator / CI | source organisation URL (§1) |
| `TARGET_ORG_URL` | operator / CI | target organisation URL (§1) |
| `SOURCE_PAT`, `TARGET_PAT` | operator / CI (interim) | PAT, or empty once Entra is in use |
| `SOURCE_PROJECT` | **per run** | the project seeded for this run (§4) |
| `TARGET_PROJECT` | **per run** | an empty target project for this run (§4) |
| `DEVOPS_MIGRATION_TEST_STORAGE` | **per run** | `%TEMP%\adpe2e\<run-id>`, a new empty folder |
| Control Plane port | **per run** | `5100 + slot` (§4) |

## 4. Isolation for each run

Every live run, including every parallel worktree, gets its own objects.

- **Run id:** `<issue>-<slot>`, for example `142-2`. The slot is the delivery-workflow slot (1–N). For manual runs it is `m`.
- **Projects:** `adpe2e-<run-id>-src` in the source organisation and `adpe2e-<run-id>-tgt` in the target organisation. Each is created fresh with process `Agile` unless the scenario says otherwise.
- **Seeding:** the source project is seeded with the scenario's fixture data. Reuse Migration Tools' `tools/e2e/scenarios/<name>/seed.ps1` pattern, ported to `tools/e2e/`. The seed writes `state.local.json`, which is git-ignored, mapping seed keys to ids.
- **Port:** the Standalone Control Plane runs on `5100 + slot`. Set it through a run-local copy of the scenario config by overriding `Environment.ControlPlane.BaseUrl`. Never edit the checked-in scenario.
- **Config:** copy `scenarios/SystemTest-Live-*.json` into `DEVOPS_MIGRATION_TEST_STORAGE` and run that copy.

## 5. Guard rails

- **Prefix guard:** a script may only create or delete objects whose name starts with `adpe2e-`. Migration Tools uses `amte2e-`, and neither repository may touch the other's prefix.
- **Organisation guard:** refuse any organisation not listed in §1.
- **Run-scoped cleanup:** delete only `adpe2e-<run-id>-*`, never `adpe2e-*` across the whole organisation, so that parallel runs do not delete each other's projects. Delete the projects, purge them from the recycle bin, then report anything left over.
- **Stale sweep:** only the operator, or a nightly job, deletes `adpe2e-*` objects older than 24 hours.

## 6. What "completes correctly" means

A live E2E passes only when **all** of the following hold:

1. The job reaches the `Completed` state with no module in error.
2. Target counts match the source after mapping, per type: work items, revisions, links, attachments, nodes and teams, as applicable to the scenario.
3. A sample of at least 3 seeded items is checked field by field, including identities, area/iteration paths, embedded images and comments.
4. Re-running the job creates no duplicates.

Each check is recorded with its observed values in the completion report and the session evidence.

## 7. Preflight

Before any live run, check that both organisations are reachable with the configured credentials and that the run's projects exist. A failed check returns **BLOCKED — live environment unavailable** with the cause (401, missing project, unreachable organisation). This mirrors `LiveEnvironmentPreflightTests` in Migration Tools.

## 8. Porting backlog

These Backlog Items bring the platform repository up to the Migration Tools setup:

1. Entra ID / Bearer authentication in `EndpointAuthenticationOptions`, for both the ADO and TFS-over-ADO paths.
2. A federated credential for this repository, plus `integration-tests.yml` (windows-latest, .NET 10, net481).
3. The `tools/e2e/` harness: per-run project create, seed and assert, plus run-scoped cleanup, with the `adpe2e-` prefix and organisation guards.
4. A `RequiresLiveEnvironment` MSTest condition attribute plus preflight tests, so that unconfigured runs skip rather than fail.
5. Moving the existing `SystemTest-Live-*` scenarios onto per-run projects.

Until items 3 and 4 ship, `nkda-backlog-deliver-workflow` creates and deletes the per-run projects itself, through the ADO REST API, under the same guards.
