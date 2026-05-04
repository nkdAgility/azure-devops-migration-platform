# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@inventory @simulated
Feature: Inventory modules produce per-module artefacts

  Scenario: Inventory_AllModulesEnabled_ProducesInventoryJson
    Given a simulated inventory job with all inventory-capable modules enabled
    When the inventory job is executed
    Then WorkItems/inventory.json exists
    And Identities/inventory.json exists
    And Nodes/inventory.json exists
    And Teams/inventory.json exists

  Scenario: Inventory_WithoutInventoryModule_ProducesIdenticalArtefacts
    Given a simulated inventory job configured without InventoryModule
    When the inventory job is executed
    Then per-module inventory artefacts are still produced
