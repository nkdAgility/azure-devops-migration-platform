Feature: Area and iteration path mapping
  As a migration operator
  I want to configure regex mapping rules for area and iteration paths
  So that work items are migrated with correctly remapped classification paths

  Background:
    Given a source project named "SourceProject" and a target project named "TargetProject"

  Scenario: Regex mapping rule translates a matching area path
    Given a NodeTranslation tool configured with area path mapping "^SourceProject\\(.*)" replaced by "TargetProject\$1"
    When the tool translates area path "SourceProject\Team A\Feature 1"
    Then the translated area path is "TargetProject\Team A\Feature 1"
    And the translation was matched by a mapping rule

  Scenario: Auto-swap translates source project name when no rule matches
    Given a NodeTranslation tool with no mapping rules configured
    When the tool translates area path "SourceProject\Team B"
    Then the translated area path is "TargetProject\Team B"
    And the translation was matched by auto project-name swap

  Scenario: External path is passed through unchanged
    Given a NodeTranslation tool with no mapping rules configured
    When the tool translates area path "OtherProject\Team C"
    Then the translated area path is "OtherProject\Team C"
    And the translation is marked as an external path

  Scenario: Tool disabled bypasses all translation
    Given a NodeTranslation tool that is disabled
    When the tool is checked for enabled state
    Then the tool reports it is not enabled

  Scenario: Regex mapping uses capture groups for path restructuring
    Given a NodeTranslation tool configured with area path mapping "^SourceProject\\Team A\\(.*)" replaced by "TargetProject\Merged\$1"
    When the tool translates area path "SourceProject\Team A\Feature 2"
    Then the translated area path is "TargetProject\Merged\Feature 2"
    And the translation was matched by a mapping rule

  Scenario: First matching rule wins when multiple rules are configured
    Given a NodeTranslation tool configured with two area path mappings:
      | Match                              | Replacement              |
      | ^SourceProject\\\\Team A\\\\(.*) | TargetProject\First\$1   |
      | ^SourceProject\\\\(.*)           | TargetProject\Second\$1  |
    When the tool translates area path "SourceProject\Team A\Feature 3"
    Then the translated area path is "TargetProject\First\Feature 3"

  Scenario: Case-insensitive matching applies to path values
    Given a NodeTranslation tool configured with area path mapping "^sourceproject\\(.*)" replaced by "TargetProject\$1"
    When the tool translates area path "SOURCEPROJECT\Team D"
    Then the translated area path is "TargetProject\Team D"
