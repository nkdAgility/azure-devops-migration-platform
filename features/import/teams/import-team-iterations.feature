Feature: Import Team Iterations
  As a platform operator
  I want team iteration assignments imported with path translation
  So that teams are linked to the correct iterations on the target system

  Background:
    Given a team package with iteration assignments

  @import @teams @iterations
  Scenario: Import assigns all team iterations to target team
    Given a team package with iterations "ProjectA\\Sprint 1", "ProjectA\\Sprint 2"
    And a NodeTranslationTool mapping "ProjectA" → "TargetProject"
    When the Teams module imports the team package
    Then AssignIterationAsync is called twice
    And the iteration paths are translated to "TargetProject\\Sprint 1" and "TargetProject\\Sprint 2"

  @import @teams @iterations
  Scenario: Unresolvable iteration path is skipped with a warning
    Given a team package with an iteration path "OldProject\\Sprint 99"
    And the NodeTranslationTool returns null for "OldProject\\Sprint 99"
    When the Teams module imports the team package
    Then AssignIterationAsync is not called for that iteration
    And a warning is logged containing the unresolvable path

  @import @teams @iterations
  Scenario: Iterations imported without NodeTranslationTool use source paths as-is
    Given a team package with iteration "ProjectA\\Sprint 1"
    And no NodeTranslationTool is registered
    When the Teams module imports the team package
    Then AssignIterationAsync is called with path "ProjectA\\Sprint 1" unchanged
