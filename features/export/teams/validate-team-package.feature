Feature: Export Team Package Validation
  As a migration operator
  I want the TeamsModule to validate the team package before import
  So that import failures caused by bad team data are detected early

  Scenario: Validation fails when no team files are present
    Given the package has no files under "Teams/"
    When TeamsModule ValidateAsync is invoked
    Then validation fails with a missing data error

  Scenario: Validation fails when a team.json file contains invalid JSON
    Given the package contains "Teams/alpha-team/team.json" with value "not-json"
    When TeamsModule ValidateAsync is invoked
    Then validation fails with a malformed JSON error for "Teams/alpha-team/team.json"

  Scenario: Validation passes for well-formed team files
    Given the package contains valid "Teams/alpha-team/team.json" and "Teams/beta-team/team.json"
    When TeamsModule ValidateAsync is invoked
    Then validation passes with no errors
