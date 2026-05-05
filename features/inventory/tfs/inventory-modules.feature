# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@inventory @tfs
Feature: TFS inventory modules produce per-module artefacts

  Scenario: Inventory_AllModulesEnabled_ProducesInventoryJson
    Given a Team Foundation Server inventory job with all inventory-capable modules enabled
    When the inventory job is executed
    Then WorkItems/inventory.json exists
    And Identities/inventory.json exists
    And Nodes/inventory.json exists

  Scenario: Inventory_WithoutInventoryModule_ProducesIdenticalArtefacts
    Given a Team Foundation Server inventory job configured without InventoryModule
    When the inventory job is executed
    Then per-module inventory artefacts are still produced

