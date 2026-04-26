Feature: Localised area and iteration node name override
  As a migration operator
  I want to normalise localised root node names before path mapping
  So that cross-locale migrations work without separate mapping rules for each locale

  Background:
    Given a source project named "SourceProject" and a target project named "TargetProject"

  Scenario: Area language override normalises the root segment
    Given a NodeStructure tool configured with AreaLanguageOverride "Area"
    When the tool translates area path "Área\Team A"
    Then the translated area path starts with "Area\"

  Scenario: Iteration language override normalises the root segment
    Given a NodeStructure tool configured with IterationLanguageOverride "Iteration"
    When the tool translates iteration path "Iteración\Sprint 1"
    Then the translated iteration path starts with "Iteration\"
