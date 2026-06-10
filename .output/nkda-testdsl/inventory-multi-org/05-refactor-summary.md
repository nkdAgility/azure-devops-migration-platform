# Refactor Summary — inventory-multi-org

Feature family: `inventory-multi-org`
Feature file: `features/inventory/tfs/inventory-multi-org.feature`
Refactor date: 2026-06-10

---

## Outcome

**No structural DSL changes required.** The extracted DSL was already well-formed with clear
separation of Scenario / Builder / Driver / Result / Factory types. One inventory correction
was applied: `00-scenario-test-inventory.md` row 1 was split into rows 1a/1b and both wiring
states were updated from `unwired` to `wired` to reflect test pass confirmation.

All 3 `InventoryModulesTests` tests pass after the refactor (3/3 passed, 0 failed).

---

## DSL Structure Assessment

| Type | File | Separation Verdict |
|---|---|---|
| `InventoryModulesScenario` | `Modules/InventoryModules/InventoryModulesScenario.cs` | Entry point only — correct |
| `InventoryModulesBuilder` | `Modules/InventoryModules/InventoryModulesBuilder.cs` | Arrangement only — correct |
| `InventoryModulesDriver` | `Modules/InventoryModules/InventoryModulesDriver.cs` | Execution only — correct (`internal`) |
| `InventoryModulesResult` | `Modules/InventoryModules/InventoryModulesResult.cs` | Assertions only — correct |
| `InventoryModuleFactory` | `Modules/InventoryModules/InventoryModuleFactory.cs` | Construction only — correct |

No cross-boundary leakage found. Builder does not contain assertions. Result does not contain
arrangement logic. Driver is `internal` and inaccessible from tests except through Builder.

---

## Duplication Review

`InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced` and
`InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` follow the
same execution path via different builder vocabulary. This is intentional per the DSL design
(`02-dsl-design.md`): both tests exist to provide vocabulary traceability — the production
internal name (`WithoutInventoryAnalyser`) and the feature-file name
(`WithoutInventoryDiscoveryModule`). No deduplication is appropriate; removing either test
would sever the feature–test mapping established during conversion.

`WithoutInventoryDiscoveryModule()` delegates to `WithoutInventoryAnalyser()` via a
single-line alias — no logic duplication in the builder.

---

## Changes Applied

### 1. `00-scenario-test-inventory.md` — split row 1 into 1a/1b; update wiring state

Row 1 was a single entry covering both tests. During the refactor it was confirmed that
`InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced` (line 35) was
already in scope as row 1a and should appear alongside row 1b. Both rows were updated:

| Row | Change |
|---|---|
| 1a | Added; wiring state set to `wired`; evidence points to `InventoryModulesTests.cs:35` |
| 1b | Wiring state updated `unwired` → `wired`; evidence column unchanged (`InventoryModulesTests.cs:57`) |

No test code changed. This is a documentation-only correction.

---

## Naming Review

| Identifier | Assessment |
|---|---|
| `WithoutInventoryAnalyser()` | Correct — names the production concept |
| `WithoutInventoryDiscoveryModule()` | Correct — feature-vocabulary alias; self-documenting via XML doc |
| `InventoryAnalyserWasIncluded` | Correct — clear boolean guard property on result |
| `AssertAllStandardModuleArtefactsExist()` | Correct — describes observable outcome at integration level |
| `AssertModuleArtefactExists(string)` | Correct — targeted single-module assertion for future scenarios |

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
dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests \
    --filter "FullyQualifiedName~InventoryModulesTests" --no-build

Passed!  - Failed: 0, Passed: 3, Skipped: 0, Total: 3, Duration: 760 ms
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
| 1a | Added (was implicit); wiring state `wired`; evidence `InventoryModulesTests.cs:35` |
| 1b | Wiring state `unwired` → `wired`; evidence `InventoryModulesTests.cs:57` (unchanged) |
