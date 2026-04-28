Feature: NodeTranslation configuration and package validation
  As a migration operator
  I want to validate NodeTranslation configuration against the package before import
  So that path coverage gaps are identified before any work items are written

  Background:
    Given a package with referenced-paths.json and NodeTranslation configuration

  Scenario: All paths mapped produces a valid report
    Given the referenced-paths artifact contains area path "SourceProject\Team A"
    And a NodeTranslation mapping rule maps "SourceProject\Team A" to "TargetProject\Team A"
    When validation runs
    Then the validation report is valid

  Scenario: Unmapped path produces a finding in the report
    Given the referenced-paths artifact contains area path "OtherProject\Unknown"
    And no mapping rules are configured
    When validation runs
    Then the validation report contains an unmapped path finding for "OtherProject\Unknown"

  Scenario: External path is reported distinctly from generic unmapped paths
    Given the referenced-paths artifact contains area path "ExternalProject\Node"
    And no mapping rules are configured
    When validation runs
    Then the validation report contains an external path finding for "ExternalProject\Node"

  Scenario: Invalid regex pattern in mapping configuration is flagged
    Given a NodeTranslation mapping rule with an invalid regex pattern "["
    When validation runs
    Then the validation report contains an invalid regex finding

  Scenario: Empty package produces a valid report with no findings
    Given the referenced-paths artifact contains no paths
    When validation runs
    Then the validation report is valid
