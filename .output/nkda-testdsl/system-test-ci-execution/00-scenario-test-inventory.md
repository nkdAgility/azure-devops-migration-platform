# Scenario Test Inventory — system-test-ci-execution

DSL design: `.output/nkda-testdsl/system-test-ci-execution/02-dsl-design.md` (complete — 2026-06-10)

Feature file: `features/cli/inventory/system-test-ci-execution.feature`
Wiring state: **unwired**

| # | Wiring State | Coverage Origin | Feature File | Scenario Name | Planned / Actual DSL Test Name(s) | Mapping Status | Expected Tags | Actual Tags | Tag Compliance | Evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| 1 | unwired | pre-existing | `features/cli/inventory/system-test-ci-execution.feature` | System tests execute in CI environment with secrets | `CiExecution_ValidSecrets_ConnectivitySucceedsAndOrgUrlPresent` | matched — **retired** | `SystemTest`, `SystemTest_Live` | `SystemTest`, `SystemTest_Live` | compliant | `tests\DevOpsMigrationPlatform.CLI.Migration.Tests\SystemTests\SystemTestCiExecutionTests.cs:26` |

**Notes:**

- Scenarios 2–5 are retired in the feature file per the comment at line 10. The four retired scenarios also have pre-existing code-first tests in `SystemTestCiExecutionTests.cs` (lines 51, 69, 99, 131) and are recorded below for traceability only; they are not conversion candidates.

| # | Wiring State | Coverage Origin | Feature File | Scenario (Retired) | Actual Test Name | Mapping Status | Actual Tags | Tag Compliance | Evidence |
|---|---|---|---|---|---|---|---|---|---|
| 2 | unwired | pre-existing (retired) | `features/cli/inventory/system-test-ci-execution.feature` | *(retired)* System tests skip gracefully when secrets are missing | `CiExecution_MissingPat_InconclusiveIfNotConfigured_ThrowsWithDocsReference` | matched | `CodeTest`, `DomainTests` | compliant | `SystemTestCiExecutionTests.cs:51` |
| 3 | unwired | pre-existing (retired) | `features/cli/inventory/system-test-ci-execution.feature` | *(retired)* No credentials appear in test output or logs | `CiExecution_LiveExecution_PatAndBearerTokensNotInOutput` | matched | `CodeTest`, `IntegrationTests` | compliant | `SystemTestCiExecutionTests.cs:69` |
| 4 | unwired | pre-existing (retired) | `features/cli/inventory/system-test-ci-execution.feature` | *(retired)* Network resilience in CI with timeout and retry | `CiExecution_TransientFailure_RetriesWithBackoffAndCompletesInTime` | matched | `CodeTest`, `IntegrationTests` | compliant | `SystemTestCiExecutionTests.cs:99` |
| 5 | unwired | pre-existing (retired) | `features/cli/inventory/system-test-ci-execution.feature` | *(retired)* Conditional execution based on environment | `CiExecution_MissingOrg_InconclusiveIfMissingOrg_ThrowsWithDocsReference` | matched | `CodeTest`, `DomainTests` | compliant | `SystemTestCiExecutionTests.cs:131` |
