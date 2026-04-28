Feature: Export Team Capacity
  As a platform operator
  I want per-sprint team capacity exported with the team package
  So that sprint capacity can be restored on the target

  Background:
    Given a simulated source project with teams having capacity data

  @export @teams @capacity
  Scenario: Export includes capacity for each assigned iteration
    Given a team assigned to "Sprint 1" and "Sprint 2" with capacity data for alice (6h/day Development)
    When the Teams module exports the project
    Then "Teams/alpha-team/team.json" contains a "capacityByIteration" map
    And "Sprint 1" has alice's capacity entry with 6 hours per day

  @export @teams @capacity
  Scenario: Team with no capacity data exports empty capacityByIteration
    Given a team with no capacity data configured
    When the Teams module exports the team
    Then "Teams/ops-team/team.json" contains an empty "capacityByIteration" map

  @export @teams @capacity
  Scenario: Capacity not supported gracefully skipped
    Given the source system does not support capacity API
    When the Teams module exports the team
    Then capacity export is skipped without error
    And the "capacityByIteration" map is empty in the exported package
