Feature: Import Default Team Detection
  As a platform operator
  I want the default team from the source to be mapped to the default team on the target regardless of name
  So that the target project's default team configuration is correctly established

  Background:
    Given a source project with a designated default team

  @import @teams
  Scenario: Source default team maps to target default team by IsDefault flag not by name
    Given a source project with a team named "Source Team" flagged as the default team (isDefault=true)
    And a target project with a team named "Target Team" flagged as the default team
    When the Teams module imports the team package
    Then the default team settings from the source are applied to the target default team
    And no name-matching is used to determine the default team

  @import @teams
  Scenario: Non-default teams are matched by name
    Given a source project with two non-default teams "Dev Team" and "Test Team"
    When the Teams module imports the team package
    Then "Dev Team" is created on the target with the correct settings
    And "Test Team" is created on the target with the correct settings

  @import @teams
  Scenario: Multiple non-default teams all imported correctly
    Given a source project with 5 non-default teams
    When the Teams module imports all teams
    Then all 5 teams exist on the target with their original names
