Feature: Export Node Package Validation
  As a migration operator
  I want the NodeStructureModule to validate the package before import
  So that import failures caused by bad data are detected early

  Scenario: Validation fails when source-tree.json is missing
    Given the package does not contain "Nodes/source-tree.json"
    When NodeStructureModule ValidateAsync is invoked
    Then validation fails with error mentioning "source-tree.json"

  Scenario: Validation fails when source-tree.json contains invalid JSON
    Given the package contains "Nodes/source-tree.json" with value "not-json"
    When NodeStructureModule ValidateAsync is invoked
    Then validation fails with a malformed JSON error

  Scenario: Validation passes for well-formed source-tree.json
    Given the package contains "Nodes/source-tree.json" with valid area and iteration arrays
    When NodeStructureModule ValidateAsync is invoked
    Then validation passes with no errors
