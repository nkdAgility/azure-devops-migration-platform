Feature: Export Team Iterations
  As a platform operator
  I want team iteration assignments exported with the team package
  So that sprint backlogs and capacity can be restored on the target

  Background:
    Given a simulated source project with teams having iteration assignments

  @export @teams @iterations
  Scenario: Export includes all team iteration assignments
    Given a team "Alpha Team" is assigned to iterations "Sprint 1", "Sprint 2", "Sprint 3"
    When the Teams module exports the project
    Then "Teams/alpha-team/team.json" contains an "iterations" array with 3 entries
    And each entry has "id", "path", and "name" fields

  @export @teams @iterations
  Scenario: Export includes iteration date ranges
    Given a team iteration "Sprint 1" with startDate "2024-01-01" and finishDate "2024-01-14"
    When the Teams module exports the project
    Then the exported iteration entry contains the start and finish dates

  @export @teams @iterations
  Scenario: Team with no iterations exports an empty iterations array
    Given a team "Ops Team" with no iteration assignments
    When the Teams module exports the team
    Then "Teams/ops-team/team.json" contains an "iterations" array with 0 entries
