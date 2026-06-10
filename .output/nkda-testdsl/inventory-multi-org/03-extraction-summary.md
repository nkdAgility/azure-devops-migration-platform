# Extraction Summary — inventory-multi-org

Feature family: `inventory-multi-org`
Feature file: `features/inventory/ado/inventory-multi-org.feature`
Extraction date: 2026-06-10
Input: `02-dsl-design.md`

---

## 1. Bootstrap Actions

`tests/DevOpsMigrationPlatform.Testing` does not exist. This is not a blocker per skill rules.
`tests/DevOpsMigrationPlatform.Testing.Dsl` exists and is the active shared DSL project.
No bootstrap was required; all DSL types needed for this family exist in
`tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModules/`.

---

## 2. Orphaned Feature File Purge

Glob `tests/**/*.feature.cs` returned no matches. No orphaned generated files were present.
Purge action: none required.

---

## 3. Changes Applied

### 3.1 `InventoryModulesBuilder` — new alias method

**File:** `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModules/InventoryModulesBuilder.cs:31`

Added `WithoutInventoryDiscoveryModule()` as a feature-vocabulary alias delegating to `WithoutInventoryAnalyser()`.
No production semantics changed.

```
public InventoryModulesBuilder WithoutInventoryDiscoveryModule()
    => WithoutInventoryAnalyser();
```

### 3.2 `InventoryModulesTests` — tag correction on existing method

**File:** `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs:34`

- Removed: `[TestCategory("UnitTest")]`
- Added: `[TestCategory("inventory")]`, `[TestCategory("multi-org")]`
- Method: `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced`
- No logic changes.

### 3.3 `InventoryModulesTests` — new test method

**File:** `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs` (appended)

Added `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` mapping
1:1 to scenario `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts`.

Tags: `[TestCategory("CodeTest")]` `[TestCategory("IntegrationTests")]` `[TestCategory("inventory")]` `[TestCategory("multi-org")]`

DSL call-chain:
```
InventoryModulesScenario.Arrange()
    .WithoutInventoryDiscoveryModule()
    .RunAsync()
```

Assertions:
- `Assert.IsFalse(result.InventoryAnalyserWasIncluded, ...)` — guard
- `result.AssertAllStandardModuleArtefactsExist()` — primary

---

## 4. Scenario Test Inventory Updates

**File:** `.output/nkda-testdsl/inventory-multi-org/00-scenario-test-inventory.md`

Both rows updated: `unwired` → `wired`.

| Row | Test Name | Before | After |
|---|---|---|---|
| 1a | `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced` | `unwired` | `wired` |
| 1b | `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` | `unwired` | `wired` |

---

## 5. Reqnroll Artefact Deletion

No Reqnroll artefacts existed for this family. No deletion was performed.

---

## 6. Build Verification

```
dotnet build tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/...csproj --no-restore -v q
Build succeeded.
  0 Error(s)
  7 Warning(s) (pre-existing CS0162 unreachable code warnings in src/, unrelated to this change)
```

---

## 7. DSL Surface After Extraction

| Type | File | State |
|---|---|---|
| `InventoryModulesBuilder` | `Modules/InventoryModules/InventoryModulesBuilder.cs` | Extended — `WithoutInventoryDiscoveryModule()` added |
| `InventoryModulesScenario` | `Modules/InventoryModules/InventoryModulesScenario.cs` | Unchanged |
| `InventoryModulesResult` | `Modules/InventoryModules/InventoryModulesResult.cs` | Unchanged |
| `InventoryModulesDriver` | `Modules/InventoryModules/InventoryModulesDriver.cs` | Unchanged |
| `InventoryModulesTests` | `Modules/InventoryModulesTests.cs` | Tag-corrected + new test method added |

---

## 8. Traceability

| Feature Step | DSL Equivalent |
|---|---|
| `Given an Azure DevOps inventory job without InventoryDiscoveryModule` | `InventoryModulesScenario.Arrange().WithoutInventoryDiscoveryModule()` |
| `When the inventory job is executed` | `.RunAsync()` |
| `Then inventory artefacts are produced by inventory-capable modules` | `result.AssertAllStandardModuleArtefactsExist()` |
