Feature: Import Team Definitions
  As a migration operator
  I want teams to be recreated on the target from the exported package
  So that team structure is preserved after migration

  # BLOCKED: Duplicate of GAP-004. TeamImportOrchestrator.ImportTeamAsync logs a warning
  # when IsDefault=true but target API does not support explicit default team assignment.
  # See: src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs:64
  Scenario: Default team on source maps to default team on target regardless of name
    Given the package contains a team flagged as default "OldDefaultTeam"
    And the target project already has a default team "NewDefaultTeam"
    When ImportAsync runs
    Then the source default team is mapped to the target default team
