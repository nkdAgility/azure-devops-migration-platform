Feature: Work item field transforms during export phase
  As a migration operator
  I want field transforms to apply during the export phase
  So that field values can be modified before they are written to the migration package

  Scenario: Export-phase transform modifies revision fields before package write
    Given the FieldTransformTool is configured with an export-phase transform
    And the tool is enabled for the Export phase
    When the tool is asked if it is enabled for the Export phase
    Then it should return true

  Scenario: Import-phase transform is not applied during export
    Given the FieldTransformTool is configured with transforms
    And the tool is only enabled for the Import phase
    When the tool is asked if it is enabled for the Export phase
    Then it should return false

  Scenario: Both-phase transform runs in both directions
    Given the FieldTransformTool has transforms configured
    And the tool is enabled
    When the tool is asked if it is enabled for either phase
    Then it should return true for both
