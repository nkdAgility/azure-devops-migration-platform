# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@inventory @ado @multi-org
Feature: Multi-organisation inventory

  Scenario: Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts
    Given an Azure DevOps inventory job without InventoryDiscoveryModule
    When the inventory job is executed
    Then inventory artefacts are produced by inventory-capable modules
