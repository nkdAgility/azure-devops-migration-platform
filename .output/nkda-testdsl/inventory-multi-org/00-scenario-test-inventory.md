# Scenario Test Inventory — inventory-multi-org

Feature family: `inventory-multi-org`
Feature file: `features/inventory/ado/inventory-multi-org.feature`

---

## Legend

| Column | Values |
|---|---|
| Wiring State | `wired`, `miswired`, `unwired` |
| Coverage Origin | `pre-existing`, `partial-existing`, `to-build` |
| Mapping Status | `matched`, `partial`, `unmatched` |
| Tag Compliance | `compliant`, `non-compliant`, `unknown` |

---

## Inventory Table

| # | Wiring State | Coverage Origin | Feature File | Scenario Name | Planned / Actual DSL Test Name | Mapping Status | Expected Tags | Actual Tags (target) | Tag Compliance | Evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| 1a | `wired` | `partial-existing` | `features/inventory/ado/inventory-multi-org.feature` | `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` | `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced` | `matched` | `[TestCategory("CodeTest")]` `[TestCategory("IntegrationTests")]` `[TestCategory("inventory")]` `[TestCategory("multi-org")]` | `[TestCategory("CodeTest")]` `[TestCategory("IntegrationTests")]` `[TestCategory("inventory")]` `[TestCategory("multi-org")]` | `compliant` | `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs` — section comment normalised; tags confirmed |
| 1b | `wired` | `to-build` | `features/inventory/ado/inventory-multi-org.feature` | `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` | `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` | `matched` | `[TestCategory("CodeTest")]` `[TestCategory("IntegrationTests")]` `[TestCategory("inventory")]` `[TestCategory("multi-org")]` | `[TestCategory("CodeTest")]` `[TestCategory("IntegrationTests")]` `[TestCategory("inventory")]` `[TestCategory("multi-org")]` | `compliant` | `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs` — uses `WithoutInventoryDiscoveryModule()` builder alias |

### Notes

**Rows 1a and 1b — `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts`**

DSL design (`02-dsl-design.md`) confirmed that no production class named `InventoryDiscoveryModule`
exists in `src/`. The feature term is synonymous with `InventoryAnalyser`. The design resolves this by:

- Row 1a: the existing test `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced`
  requires tag correction only (add `inventory`, `multi-org`; remove `UnitTest`). No logic changes.
- Row 1b: a new test `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced`
  is planned using a new `WithoutInventoryDiscoveryModule()` builder alias method, providing
  direct vocabulary traceability from the feature scenario to an executing test.

The `multi-org` tags are appropriate because the feature carries `@multi-org` and these tests
cover the module-independence aspect of multi-org inventory jobs.
