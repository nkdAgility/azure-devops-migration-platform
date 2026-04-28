Feature: Import Team Definitions
  As a migration operator
  I want teams to be recreated on the target from the exported package
  So that team structure is preserved after migration

  Scenario: Teams are created on the target from the package
    Given the package contains team files for "Alpha Team" and "Beta Team"
    When TeamsModule ImportAsync runs
    Then ITeamTarget.CreateOrUpdateTeamAsync is called for "Alpha Team"
    And ITeamTarget.CreateOrUpdateTeamAsync is called for "Beta Team"

  Scenario: Existing team is updated idempotently
    Given the package contains a team file for "Alpha Team"
    And the target already has a team named "Alpha Team"
    When ImportAsync runs
    Then ITeamTarget.CreateOrUpdateTeamAsync is called (update path — no error)

  Scenario: Default team on source maps to default team on target regardless of name
    Given the package contains a team flagged as default "OldDefaultTeam"
    And the target project already has a default team "NewDefaultTeam"
    When ImportAsync runs
    Then the source default team is mapped to the target default team
