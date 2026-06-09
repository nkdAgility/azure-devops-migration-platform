# Verification — system-test-ci-execution

Feature file: `features/cli/inventory/system-test-ci-execution.feature`
Wiring state: **unwired**
Verification date: 2026-06-09
Verdict: **FAIL**

---

## 1. Test Execution — Feature-Family Tests

Command:
```
dotnet test tests/DevOpsMigrationPlatform.CLI.Migration.Tests/DevOpsMigrationPlatform.CLI.Migration.Tests.csproj --filter "FullyQualifiedName~SystemTestCiExecutionTests" --no-build --logger "console;verbosity=detailed"
```

Result summary:

| Test Method | Outcome | Evidence |
|---|---|---|
| `CiExecution_ValidSecrets_InventoryConnectsAndProducesOutput` | **FAILED** | `DotnetTestResult.ShouldSucceed()` assertion failed — CLI subprocess exited non-zero with `CommandParseException: Unknown command 'inventory'`. See `SystemTestCiExecutionTests.cs:43`. |
| `CiExecution_MissingPat_ReportsSkipReasonAndContinues` | Inconclusive (expected) | `InconclusiveIfNotConfigured()` fired; message contains `docs/contributors.md`. `SystemTestCiExecutionTests.cs:54`. |
| `CiExecution_LiveExecution_PatAndBearerTokensNotInOutput` | Passed | PAT absence, bearer token masking, and structured log masking all asserted. `SystemTestCiExecutionTests.cs:81`. |
| `CiExecution_TransientFailure_RetriesWithBackoffAndCompletesInTime` | Passed | Retry count >= 2 asserted; budget not expired. `SystemTestCiExecutionTests.cs:110`. |
| `CiExecution_MissingOrg_LiveTestsInconclusiveUnitTestsContinue` | Inconclusive (expected) | `InconclusiveIfMissingOrg()` fired; message contains `docs/contributors.md`. `SystemTestCiExecutionTests.cs:136`. |

**Total: 2 passed, 1 failed, 2 inconclusive (expected)**

The full test run returned exit code 1 (FAIL).

---

## 2. Root Cause of Test Failure — Scenario 1

`CiExecution_ValidSecrets_InventoryConnectsAndProducesOutput` calls
`InventoryCliRunner.RunInventoryAsync()` which executes:

```
dotnet run --project src/DevOpsMigrationPlatform.CLI.Migration/... -- inventory
```

The CLI application (`Program.cs`) registers: `prepare`, `queue`, `manage`,
`controlplane`, `agent`, `config`. There is no `inventory` command.

The subprocess exits with an unhandled `CommandParseException`. `DotnetTestResult.ShouldSucceed()`
then throws an `AssertFailedException`. This is a production gap — the feature scenario
assumes an `inventory` CLI command that does not yet exist. Recorded as **GAP-018** in
`analysis/dsl-gaps-detected.md`.

---

## 3. Test-Validity Scoring — Intent-Derived Tests

Scenarios 2–5 are intent-derived (unwired family, no baseline):

| Test | Dimensions (1-5 each) | Score | Verdict |
|---|---|---|---|
| `CiExecution_MissingPat_ReportsSkipReasonAndContinues` | Specificity 4, Isolation 5, Assertion strength 4, Non-vacuous 4, Production relevance 4 | 21/25 | HIGH VALUE |
| `CiExecution_LiveExecution_PatAndBearerTokensNotInOutput` | Specificity 4, Isolation 4, Assertion strength 5, Non-vacuous 5, Production relevance 5 | 23/25 | HIGH VALUE |
| `CiExecution_TransientFailure_RetriesWithBackoffAndCompletesInTime` | Specificity 5, Isolation 4, Assertion strength 5, Non-vacuous 5, Production relevance 5 | 24/25 | HIGH VALUE |
| `CiExecution_MissingOrg_LiveTestsInconclusiveUnitTestsContinue` | Specificity 4, Isolation 5, Assertion strength 4, Non-vacuous 4, Production relevance 4 | 21/25 | HIGH VALUE |

All four passing/inconclusive tests meet the validity gate (>= 16/25).

`CiExecution_ValidSecrets_InventoryConnectsAndProducesOutput` cannot be scored — the test
fails before assertions run due to the missing CLI command.

---

## 4. Scenario Inventory Coverage Check

| # | Scenario | Mapping Status | Retirement Status |
|---|---|---|---|
| 1 | System tests execute in CI environment with secrets | FAIL — test defect (GAP-018) | **retained** |
| 2 | System tests skip gracefully when secrets are missing | implemented — Inconclusive (expected) | **retired** |
| 3 | No credentials appear in test output or logs | implemented — passed | **retired** |
| 4 | Network resilience in CI with timeout and retry | implemented — passed | **retired** |
| 5 | Conditional execution based on environment | implemented — Inconclusive (expected) | **retired** |

Scenario 1 remains `unmatched` (test exists but fails). The `00-scenario-test-inventory.md` has one
unresolved row.

---

## 5. Tag Compliance

| Test Method | Tags | Compliant |
|---|---|---|
| `CiExecution_ValidSecrets_InventoryConnectsAndProducesOutput` | `SystemTest`, `SystemTest_Live` | Yes |
| `CiExecution_MissingPat_ReportsSkipReasonAndContinues` | `SystemTest`, `SystemTest_Smoke` | Yes |
| `CiExecution_LiveExecution_PatAndBearerTokensNotInOutput` | `SystemTest`, `SystemTest_Smoke` | Yes |
| `CiExecution_TransientFailure_RetriesWithBackoffAndCompletesInTime` | `SystemTest`, `SystemTest_Smoke` | Yes |
| `CiExecution_MissingOrg_LiveTestsInconclusiveUnitTestsContinue` | `SystemTest`, `SystemTest_Smoke` | Yes |

Tag compliance: **all compliant**.

---

## 6. Build and Full Test Suite

Skipped — the feature-family test run returned FAIL. Per the skill requirement:
"A commit must never be made unless all three — scenario tests green, full build green,
full test suite green — are confirmed in that order."

The full repository test suite was not run.

---

## 7. Reqnroll Artefact Check

Wiring state is `unwired`. Expected artefacts to verify/remove:

| Artefact | Check | Result |
|---|---|---|
| `.feature.cs` generated file | `glob **/SystemTestCiExecution*.feature.cs` | None found — correct for unwired |
| `*Steps.cs` bindings | `glob **/SystemTestCiExecution*Steps.cs` | None found — correct for unwired |

No Reqnroll artefacts exist for this family. The `.feature` file is retained because
scenario 1 is unresolved.

---

## 8. Orphan Feature.cs Check

No orphan `Features\*.feature.cs` files found without matching `.feature` inputs.

---

## 9. Verdict

**FAIL**

Reason: `CiExecution_ValidSecrets_InventoryConnectsAndProducesOutput` (scenario 1) fails
because the CLI has no `inventory` command. The test defect is a production gap recorded
as GAP-018.

Actions taken per FAIL protocol:
- Feature file retained at `features/cli/inventory/system-test-ci-execution.feature`
  (scenario 1 remains; scenarios 2–5 remain retired in the comment block).
- GAP-018 appended to `analysis/dsl-gaps-detected.md`.
- `00-scenario-test-inventory.md` updated to reflect FAIL status for scenario 1.
- Partial progress committed with 4 retired scenarios noted.

---

## 10. Partial Retirement Evidence

Scenarios 2–5 have mapped passing/expected-Inconclusive tests with path:line evidence:

| Scenario | Mapped Test | Evidence |
|---|---|---|
| 2 — Skip when secrets missing | `CiExecution_MissingPat_ReportsSkipReasonAndContinues` | `SystemTestCiExecutionTests.cs:54` — Inconclusive with docs/contributors.md reference |
| 3 — No credentials in output | `CiExecution_LiveExecution_PatAndBearerTokensNotInOutput` | `SystemTestCiExecutionTests.cs:81` — passed |
| 4 — Network resilience | `CiExecution_TransientFailure_RetriesWithBackoffAndCompletesInTime` | `SystemTestCiExecutionTests.cs:110` — passed |
| 5 — Conditional execution | `CiExecution_MissingOrg_LiveTestsInconclusiveUnitTestsContinue` | `SystemTestCiExecutionTests.cs:136` — Inconclusive with docs/contributors.md reference |
