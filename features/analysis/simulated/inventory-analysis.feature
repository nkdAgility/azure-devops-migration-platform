# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@analysis @simulated
Feature: Inventory analysis consolidation

  Scenario: Inventory_AllModulesComplete_ConsolidatedInventoryJsonWritten
    Given a simulated inventory job with all inventory-capable modules enabled
    When inventory analysis runs
    Then inventory.json is written at the package root

  Scenario: Inventory_AllModulesComplete_InventoryCsvWritten
    Given a simulated inventory job with all inventory-capable modules enabled
    When inventory analysis runs
    Then inventory.csv is written at the package root with at least one data row

  Scenario: Inventory_ZeroCountModule_EmitsWarning
    Given a simulated inventory job with one module inventory file missing
    When inventory analysis runs
    Then a warning is emitted for the missing module file
