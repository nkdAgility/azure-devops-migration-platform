# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@inventory @simulated @multi-org
Feature: Multi-organisation inventory

  Scenario: Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts
    Given a simulated inventory job without InventoryDiscoveryModule
    When the inventory job is executed
    Then inventory artefacts are produced by inventory-capable modules
