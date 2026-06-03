Feature: Import Team Area Paths
  As a platform operator
  I want team area path assignments imported with path translation
  So that teams are associated with the correct area paths on the target system

  Background:
    Given a team package with area path assignments

  # BLOCKED: TeamImportOrchestrator.TranslatePath() returns `result.TargetPath ?? sourcePath`.
  # When NodeTransformTool returns TargetPath=null, the fallback returns the original source path —
  # never null. The `else _logger.LogWarning(...)` branch at line 150 is unreachable for non-empty
  # paths, so "skipped with warning" cannot happen under the current implementation.
  # See: src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs:147-151
  @import @teams @nodes
  Scenario: Untranslatable included area path is skipped with warning
    Given a team package with default area path "ProjectA" and an included path "ProjectA\\ObsoleteArea"
    And the NodeTransformTool returns null for "ProjectA\\ObsoleteArea"
    When the Teams module imports the team package
    Then SetAreaPathsAsync is called without "ProjectA\\ObsoleteArea" in the included paths
    And a warning is logged for the untranslatable path

  # BLOCKED: Same TranslatePath() fallback — `defaultPath` is never null for a non-empty path,
  # so `if (defaultPath is not null)` always passes and SetAreaPathsAsync is always called.
  # See: src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs:141-157
  @import @teams @nodes
  Scenario: Untranslatable default area path prevents SetAreaPathsAsync from being called
    Given a team package with default area path "UnknownProject"
    And the NodeTransformTool returns null for "UnknownProject"
    When the Teams module imports the team package
    Then SetAreaPathsAsync is not called for this team
