Feature: Export Team Scope Filter
  As a migration operator
  I want to control which teams are exported
  So that I can migrate a subset of teams when needed

  Scenario: All teams are exported when scope is "all"
    Given the source project has teams "Alpha", "Beta", "Gamma"
    And TeamsModuleOptions has Scope = "all"
    When ExportAsync runs
    Then all 3 teams are exported

  Scenario: Only matching teams are exported when scope is "teams" with filter
    Given the source project has teams "Alpha", "Beta", "Gamma"
    And TeamsModuleOptions has Scope = "teams" and Filter = "Alpha|Beta"
    When ExportAsync runs
    Then only "Alpha" and "Beta" are exported

  Scenario: Empty filter returns all teams when scope is "teams"
    Given the source project has teams "Alpha", "Beta"
    And TeamsModuleOptions has Scope = "teams" and Filter = ""
    When ExportAsync runs
    Then both teams are exported
