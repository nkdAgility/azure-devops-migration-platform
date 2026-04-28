Feature: Export Team Definitions
  As a migration operator
  I want all teams from the source project to be exported
  So that they can be recreated on the target

  Scenario: All teams are written to the package as individual files
    Given the source project contains teams "Alpha Team" and "Beta Team"
    When the TeamsModule ExportAsync runs
    Then "Teams/alpha-team/team.json" is written to the package
    And "Teams/beta-team/team.json" is written to the package

  Scenario: Team file contains team name and description
    Given the source project contains a team "Alpha Team" with description "The Alpha team"
    When ExportAsync runs
    Then "Teams/alpha-team/team.json" contains the team name "Alpha Team"

  Scenario: Default team is flagged in the exported package
    Given the source project has a default team "Core Team"
    When ExportAsync runs
    Then "Teams/core-team/team.json" contains isDefault = true
