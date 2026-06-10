# Refactor Summary — inventory-multi-org

Feature family: `inventory-multi-org`
Feature file: `features/inventory/ado/inventory-multi-org.feature`
Refactor date: 2026-06-10

---

## Outcome

**No structural DSL changes required.** The extracted DSL was already well-formed with clear
separation of Scenario / Builder / Driver / Result / Factory types. Minor tag and comment
corrections were applied to bring the test file into full consistency.

All 3 `InventoryModulesTests` tests pass after the refactor (3/3 passed, 0 failed).

---

## DSL Structure Assessment

| Type | File | Separation Verdict |
|---|---|---|
| `InventoryModulesScenario` | `Modules/InventoryModules/InventoryModulesScenario.cs` | Entry point only — correct |
| `InventoryModulesBuilder` | `Modules/InventoryModules/InventoryModulesBuilder.cs` | Arrangement only — correct |
| `InventoryModulesDriver` | `Modules/InventoryModules/InventoryModulesDriver.cs` | Execution only — correct |
| `InventoryModulesResult` | `Modules/InventoryModules/InventoryModulesResult.cs` | Assertions only — correct |
| `InventoryModuleFactory` | `Modules/InventoryModules/InventoryModuleFactory.cs` | Construction only — correct |

No cross-boundary leakage found. Builder does not contain assertions. Result does not contain
arrangement logic. Driver is `internal` and inaccessible from tests except through Builder.

---

## Duplication Review

`InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced` and
`InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` follow the
same execution path via different builder vocabulary. This is intentional per the DSL design
(`02-dsl-design.md`): both tests exist to provide vocabulary traceability (production term and
feature term). No deduplication is appropriate here — removing either test would break the
explicit feature–test mapping established during conversion.

`WithoutInventoryDiscoveryModule()` delegates to `WithoutInventoryAnalyser()` via a single-line
alias — no logic duplication in the builder.

---

## Changes Applied

### 1. `InventoryModulesTests.cs` — remove `[TestCategory("UnitTest")]` from Scenario 1

**File:** `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs`

`InventoryModules_AllModulesEnabled_ProducesPerModuleInventoryArtefacts` carried
`[TestCategory("UnitTest")]` which was incorrect — this test exercises real DI wiring and
module execution via the integration test harness. The tag was removed and
`[TestCategory("inventory")]` was added for consistent feature-family classification.

| Attribute | Before | After |
|---|---|---|
| `[TestCategory("UnitTest")]` | present | removed |
| `[TestCategory("inventory")]` | absent | added |

### 2. `InventoryModulesTests.cs` — normalise section comment format

Section comments were inconsistent:

- Scenario 1: `// --- Scenario 1 ---` (anonymous)
- Scenario 2: `// --- Scenario 2 ---` (anonymous)
- Scenario 3: `// --- Scenario: name ---` (named)

All three comments now use the named form matching the convention established by Scenario 3.
The comment for Scenario 2 now reads:

```
// --- Scenario: Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts (analyser vocabulary) ---
```

The qualifier `(analyser vocabulary)` distinguishes it from the direct-vocabulary mapping in
Scenario 3, making the relationship between the two tests immediately readable.

### 3. `00-scenario-test-inventory.md` — correct wiring-state to `wired`

Both rows 1a and 1b still showed `unwired` (a carry-over from the assessment phase). Both
tests are confirmed wired and passing. Updated to `wired`.

---

## Naming Review

| Identifier | Assessment |
|---|---|
| `WithoutInventoryAnalyser()` | Correct — names the production concept |
| `WithoutInventoryDiscoveryModule()` | Correct — names the feature-vocabulary concept; alias is self-documenting via XML doc |
| `InventoryAnalyserWasIncluded` | Correct — clear boolean property on result |
| `AssertAllStandardModuleArtefactsExist()` | Correct — describes observable outcome |
| `AssertModuleArtefactExists(string)` | Correct — targeted single-module assertion |

No renaming required.

---

## Tag Compliance After Refactor

| Test | Tags | Compliant |
|---|---|---|
| `InventoryModules_AllModulesEnabled_ProducesPerModuleInventoryArtefacts` | `CodeTest`, `IntegrationTests`, `inventory` | Yes |
| `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced` | `CodeTest`, `IntegrationTests`, `inventory`, `multi-org` | Yes |
| `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` | `CodeTest`, `IntegrationTests`, `inventory`, `multi-org` | Yes |

---

## Test Run Evidence

```
dotnet test ... --filter "FullyQualifiedName~InventoryModulesTests"
Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3
```

All tests pass. No production behaviour changes.

---

## Speculative Abstraction Check

No abstraction was introduced for unmigrated families. The DSL surface covers only the
`inventory-modules` scenarios and is scoped entirely within the
`DevOpsMigrationPlatform.Infrastructure.Agent.Tests` project.

---

## Inventory Update

`00-scenario-test-inventory.md` updated:

| Row | Change |
|---|---|
| 1a | Wiring state `unwired` → `wired`; Evidence column updated |
| 1b | Wiring state `unwired` → `wired`; Evidence column updated |
