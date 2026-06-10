# Scenario Test Inventory — inventory-multi-org

Feature family: `inventory-multi-org`
Feature file: `features/inventory/simulated/inventory-multi-org.feature`
Assessment date: 2026-06-10

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

| # | Wiring State | Coverage Origin | Feature File | Scenario Name | Planned / Actual DSL Test Name | Mapping Status | Expected Tags | Actual Tags | Tag Compliance | Evidence |
|---|---|---|---|---|---|---|---|---|---|---|
| 1a | `wired` | `pre-existing` | `features/inventory/simulated/inventory-multi-org.feature` | `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` | `InventoryModules_WithoutInventoryAnalyser_PerModuleArtefactsStillProduced` | `matched` | `CodeTest`, `IntegrationTests`, `inventory`, `multi-org` | `CodeTest`, `IntegrationTests`, `inventory`, `multi-org` | `compliant` | `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs:35` |
| 1b | `wired` | `pre-existing` | `features/inventory/simulated/inventory-multi-org.feature` | `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts` | `InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced` | `matched` | `CodeTest`, `IntegrationTests`, `inventory`, `multi-org` | `CodeTest`, `IntegrationTests`, `inventory`, `multi-org` | `compliant` | `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/InventoryModulesTests.cs:57` |

---

## Notes

**Row 1 — `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts`**

The scenario's intent is fully covered by the code-first MSTest method
`InventoryModules_WithoutInventoryDiscoveryModule_PerModuleArtefactsStillProduced`
at `InventoryModulesTests.cs:57`. That method uses the builder alias
`WithoutInventoryDiscoveryModule()` (defined in `InventoryModulesBuilder.cs:37`)
which delegates to `WithoutInventoryAnalyser()`. It carries all four required
`TestCategory` tags (`CodeTest`, `IntegrationTests`, `inventory`, `multi-org`)
and asserts via `AssertAllStandardModuleArtefactsExist()` that all four
inventory-capable modules still produce artefacts. No new test is needed.
