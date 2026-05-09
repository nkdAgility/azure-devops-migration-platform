# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

@platform @spec034
Feature: Package boundary adoption
  To standardize package access
  As a migration platform maintainer
  We need callers to route package operations through a typed package boundary

  Scenario: Package boundary contracts exist for callers
    Given the package boundary contract surface is required
    When I validate the package boundary contract availability
    Then package boundary contracts are available to callers

