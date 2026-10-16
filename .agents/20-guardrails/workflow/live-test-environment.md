# Live Test Environment

> **Status:** Ratified by the operator on 2026-10-15 (ADR-0034). Effective once merged to `main`.

This file defines how live end-to-end runs work. Step 3 of [completion-protocol.md](completion-protocol.md) and `nkda-backlog-deliver-workflow` both follow it.

It adopts the azure-devops-migration-tools live-test approach: the same Entra identity, the same organisations and the same conventions. Migration Tools introduced it in PR #3221; its `tools/e2e/` harness is on branch `test/e2e-regression-2026-10`. Each repository uses its own object prefix, so one repository's cleanup never touches the other's objects.

## 1. Organisations

| Role | Organisation | Status |
| --- | --- | --- |
| Source | `https://dev.azure.com/nkdagility-adomt-source` | Target state; shared with Migration Tools |
| Target | `https://dev.azure.com/nkdagility-adomt-target` | Target state; shared with Migration Tools |
| Legacy | `https://dev.azure.com/nkdagility-preview` | **Current.** The checked-in `SystemTest-Live-*` scenarios still use it (fixed projects such as `migrationTest5`). Moving them is #176. |

Live runs may touch only these organisations.

## 2. Authentication: Entra ID, no PATs

An Entra ID access token for Azure DevOps (resource `499b84ac-1321-427f-aa17-267ca6975798`) is **accepted wherever a PAT is**. This includes the platform's `AccessToken` authentication, which sends Basic auth. No code change is needed; pass the token where the PAT goes.

| Where | Identity | How the token is obtained |
| --- | --- | --- |
| CI | Shared Entra test app `adomt-integration-tests` (`AZURE_TEST_CLIENT_ID`) | `azure/login@v2` in the `integration-tests` environment through GitHub OIDC, then `az account get-access-token`. No secret is stored. The app's federated credentials and the repository variables are defined in `infra/test-env/` (#175). |
| Local | The developer's own `az login` | `az account get-access-token --resource 499b84ac-1321-427f-aa17-267ca6975798` |

- Set the token only on the test process.
- Mask it in CI output.
- Never write it to a file, log, issue or PR.
- Tokens expire after about an hour, so get a fresh token for each run.

## 3. Environment variables (what the code reads today)

| Variable | Read by | Value |
| --- | --- | --- |
| `AZDEVOPS_SYSTEM_TEST_ORG` | Live tests (`QueueCommandTests`, scenarios) | Organisation URL |
| `AZDEVOPS_SYSTEM_TEST_PAT` | Live tests and scenarios | **The Entra token** (§2) |
| `SOURCE_ORG_URL`, `SOURCE_PROJECT`, `SOURCE_PAT`, `TARGET_ORG_URL`, `TARGET_PROJECT`, `TARGET_PAT` | Per-run scenario copies (§4) | Set per run; the PAT variables hold the Entra token |
| `DEVOPS_MIGRATION_TEST_STORAGE` | CLI test runner | A new folder per run |

The CLI test runner gives every CLI run its own ephemeral Control Plane port. It also puts the run in a kill-on-close job object, so no process outlives the test host (#180).

## 4. Isolation for each run

- **Run id:** `<issue>-<invocation-id>`. The invocation id is unique per workflow invocation, never just a slot number.
  - Manual runs use `<issue>-m-<user>-<yyyyMMddHHmm>`.
  - A run id is never reused, so two deliveries of the same item cannot share resources.
- **Projects:** `adpe2e-<run-id>-src` and `adpe2e-<run-id>-tgt`, created fresh and seeded with the scenario's fixture data.
- **Config:** copy the scenario into the run's storage folder, then **rewrite every endpoint**: organisation URLs, project names, and the `$ENV:` credential references.
  - Before running, check the resolved config. It must reference only this run's `adpe2e-<run-id>-*` projects and contain no `AZDEVOPS_SYSTEM_TEST_*` or fixed project names.
  - Never run the checked-in scenario directly against per-run projects.
- **Interim, until #176:** the checked-in live tests use fixed shared projects in `nkdagility-preview`, so live runs are **serialised**. Only one runs at a time, under a single-run lock.
- **Ownership:** a delivery claims its item before creating any resources:
  - it assigns the item and adds an `in-progress` label;
  - it skips any item that is already claimed.

## 5. Guard rails

- **Prefix guard:** create or delete only objects whose names start with `adpe2e-`. Migration Tools uses `amte2e-`; neither repository touches the other's prefix.
- **Organisation guard:** refuse any organisation that is not listed in §1.
- **Cleanup:** delete only `adpe2e-<run-id>-*` objects, then purge them from the recycle bin.
  - A stale sweep (`adpe2e-*` older than 24 hours) runs only from the operator or a nightly job.

## 6. What "completes correctly" means

A live E2E run passes only when all of these hold:

1. The job completes with no module in error.
2. The target counts match the source after mapping.
3. At least 3 seeded items have been checked field by field: identities, paths, images and comments.
4. Re-running the migration creates no duplicates.

Record each check, with the values observed, in the completion report and the evidence file.

## 7. Preflight

Before any live run, check that each organisation is reachable with the token and that the run's projects exist. If either check fails, the result is **BLOCKED — live environment unavailable**, with the cause (401, missing project, or unreachable).

## 8. Porting backlog

The remaining work to match the Migration Tools setup:

- #140: native Entra acquisition. Optional, because a token can already be passed as a PAT.
- #141: preflight checks, plus a `RequiresLiveEnvironment` attribute so live tests skip rather than fail when no environment is available.
- #142: the e2e harness.
- #175: infra and CI.
- #176: move the live scenarios to per-run projects.
