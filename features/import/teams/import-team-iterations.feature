Feature: Import Team Iterations
  As a platform operator
  I want team iteration assignments imported with path translation
  So that teams are linked to the correct iterations on the target system

  Background:
    Given a team package with iteration assignments

  # BLOCKED: Same root cause as GAP-005. TranslatePath() returns `result.TargetPath ?? sourcePath`,
  # so translatedPath is never null for a non-empty iteration path. The skip branch at
  # TeamImportOrchestrator.cs:~93 (`if (translatedPath is null) { continue; }`) is unreachable.
  # See: src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs:190-200
  @import @teams @iterations
  Scenario: Unresolvable iteration path is skipped with a warning
    Given a team package with an iteration path "OldProject\\Sprint 99"
    And the NodeTransformTool returns null for "OldProject\\Sprint 99"
    When the Teams module imports the team package
    Then AssignIterationAsync is not called for that iteration
    And a warning is logged containing the unresolvable path
