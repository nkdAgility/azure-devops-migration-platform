Feature: Import Default Team Detection
  As a platform operator
  I want the default team from the source to be mapped to the default team on the target regardless of name
  So that the target project's default team configuration is correctly established

  Background:
    Given a source project with a designated default team

  # BLOCKED: TeamImportOrchestrator.ImportTeamAsync (line 64-71) detects IsDefault=true but
  # only logs a warning: "target API does not support explicit default team assignment."
  # No settings are applied to the target's default team. Expected outcome cannot be confirmed.
  # See: src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs:64
  @import @teams
  Scenario: Source default team maps to target default team by IsDefault flag not by name
    Given a source project with a team named "Source Team" flagged as the default team (isDefault=true)
    And a target project with a team named "Target Team" flagged as the default team
    When the Teams module imports the team package
    Then the default team settings from the source are applied to the target default team
    And no name-matching is used to determine the default team
