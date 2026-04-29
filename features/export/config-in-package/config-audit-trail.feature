@us2-config-audit
Feature: Config persists in package for audit and resume
  As a migration operator
  I want migration-config.json to be preserved in the package after export
  So that the configuration used for a migration run can be audited or used to resume

  Background:
    Given a valid migration.json configuration file with field-transform and node-structure tools enabled
    And an output package directory exists

  Scenario: Config file is present after export completes
    When I run the export command with the configuration
    Then migration-config.json exists at the root of the output package

  Scenario: Config file content matches original configuration
    When I run the export command with the configuration
    Then migration-config.json contains the source, target, and module settings from the original configuration

  Scenario: Config file is not overwritten on resume
    Given a package with an existing migration-config.json
    When I run the export command again targeting the same package
    Then the command fails with an error indicating re-submission is not permitted
    And migration-config.json is unchanged

  Scenario: Config file contains a configVersion field
    When I run the export command with the configuration
    Then migration-config.json contains a "configVersion" field with value "2.0"
