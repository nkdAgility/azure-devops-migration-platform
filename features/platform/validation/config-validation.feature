Feature: Migration Configuration Validation
  As a platform operator
  I want the platform to validate my migration.json before any migration begins
  So that misconfigured runs fail immediately with clear errors rather than partway through

  Background:
    Given the platform is configured to validate on start

  Scenario: Valid export configuration passes validation
    Given a migration config with mode "Export"
    And the config has a source endpoint of type "AzureDevOpsServices"
    And the config has an artefacts path of "D:\exports\run-001"
    When the config is validated
    Then the validation passes

  Scenario: Valid import configuration passes validation
    Given a migration config with mode "Import"
    And the config has a target endpoint of type "AzureDevOpsServices"
    And the config has an artefacts path of "D:\exports\run-001"
    When the config is validated
    Then the validation passes

  Scenario: Valid both configuration passes validation
    Given a migration config with mode "Both"
    And the config has a source endpoint of type "AzureDevOpsServices"
    And the config has a target endpoint of type "AzureDevOpsServices"
    And the config has an artefacts path of "D:\exports\run-001"
    When the config is validated
    Then the validation passes

  Scenario: Missing mode fails validation
    Given a migration config with mode ""
    And the config has an artefacts path of "D:\exports\run-001"
    When the config is validated
    Then the validation fails
    And the error mentions "Mode"

  Scenario: Invalid mode value fails validation
    Given a migration config with mode "Replicate"
    And the config has an artefacts path of "D:\exports\run-001"
    When the config is validated
    Then the validation fails
    And the error mentions "Mode"

  Scenario: Export mode without source fails validation
    Given a migration config with mode "Export"
    And the config has no source endpoint
    And the config has an artefacts path of "D:\exports\run-001"
    When the config is validated
    Then the validation fails
    And the error mentions "Source"

  Scenario: Import mode without target fails validation
    Given a migration config with mode "Import"
    And the config has no target endpoint
    And the config has an artefacts path of "D:\exports\run-001"
    When the config is validated
    Then the validation fails
    And the error mentions "Target"

  Scenario: Missing package path fails validation
    Given a migration config with mode "Export"
    And the config has a source endpoint of type "AzureDevOpsServices"
    And the config has a package path of ""
    When the config is validated
    Then the validation fails
    And the error mentions "Package"
