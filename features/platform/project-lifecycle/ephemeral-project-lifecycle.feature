# SPDX-License-Identifier: AGPL-3.0-only
# Copyright (c) Naked Agility Limited

Feature: Ephemeral project lifecycle for connector tests
  As a connector test author
  I want eligible tests to create and tear down an isolated project
  So that runs are deterministic, isolated, and leave no residue

  @US1
  Scenario: Eligible run creates and tears down project successfully
    Given a lifecycle-eligible test run for connector "Simulated"
    When lifecycle setup executes
    And lifecycle teardown executes
    Then setup should succeed
    And teardown should succeed

  @US2
  Scenario: Teardown is attempted when test execution fails
    Given a lifecycle-eligible test run for connector "Simulated"
    And the test execution fails after setup
    When lifecycle teardown executes
    Then teardown should be attempted

  @US3
  Scenario Outline: Eligibility respects connector type
    Given lifecycle eligibility allows connector "<connector>"
    Then lifecycle should be eligible for "<connector>"

    Examples:
      | connector            |
      | AzureDevOpsServices  |
      | TeamFoundationServer |
