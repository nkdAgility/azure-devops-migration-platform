# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@inventory @tfs @multi-org
Feature: Multi-organisation inventory

  Scenario: Inventory_TwoOrganisations_BothContributeToInventory
    Given a Team Foundation Server inventory job with two enabled source organisations
    When the inventory job is executed
    Then inventory.json contains contributions from both organisations

  Scenario: Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts
    Given a Team Foundation Server inventory job without InventoryDiscoveryModule
    When the inventory job is executed
    Then inventory artefacts are produced by inventory-capable modules

  Scenario: Inventory_OneOrgUnreachable_RemainingOrgsStillProcessed
    Given a Team Foundation Server inventory job where one organisation endpoint is unreachable
    When the inventory job is executed
    Then a warning is emitted for the unreachable organisation
    And remaining organisations are still processed
