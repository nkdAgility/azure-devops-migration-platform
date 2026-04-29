Feature: Export Team Members
  As a platform operator
  I want team membership exported with the team package
  So that team assignments can be restored on the target

  Background:
    Given a simulated source project with teams having members

  @export @teams @members
  Scenario: Export includes all team members
    Given a team "Alpha Team" has 3 members: "alice@test.com" (admin), "bob@test.com" (member), "carol@test.com" (member)
    When the Teams module exports the project
    Then "Teams/alpha-team/team.json" contains a "members" array with 3 entries
    And alice's entry has isAdmin=true

  @export @teams @members
  Scenario: Export includes member display names and unique names
    Given a team member with displayName "Alice Smith" and uniqueName "alice@test.com"
    When the Teams module exports the team
    Then the exported member entry contains "displayName" and "uniqueName" fields

  @export @teams @members
  Scenario: Team with no members exports an empty members array
    Given a team "Ops Team" with no members
    When the Teams module exports the team
    Then "Teams/ops-team/team.json" contains a "members" array with 0 entries
