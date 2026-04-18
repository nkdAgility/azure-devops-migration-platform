Feature: Simulated Work Item Export
  As a migration developer
  I want to export work items from a Simulated source
  So that I can test the export pipeline without connecting to a real Azure DevOps instance

  Background:
    Given the export module is configured with a Simulated source endpoint
    And the generator config specifies 2 projects with 3 work item types each

  @simulated @offline
  Scenario: Export generates deterministic revision folders in lexicographic order
    When the WorkItems export module runs
    Then the package contains revision folders under "WorkItems/"
    And each folder name follows the pattern "WorkItems/yyyy-MM-dd/<ticks>-<id>-<rev>/"
    And the folders are ordered lexicographically ascending by folder name

  @simulated @offline
  Scenario: Each revision folder contains a valid revision.json
    When the WorkItems export module runs
    Then each revision folder contains a "revision.json" file
    And each "revision.json" contains the expected System.Id and System.Rev fields

  @simulated @offline
  Scenario: Export is deterministic across multiple runs
    When the WorkItems export module runs twice with the same generator config
    Then the second run produces identical revision.json content
    And the folder names are identical

  @simulated @offline
  Scenario: Export with no projects produces no output
    Given the generator config specifies an empty projects list
    When the WorkItems export module runs
    Then no folders are created under "WorkItems/"

  @simulated @offline
  Scenario: RevisionsPerItem zero causes startup failure
    Given a work item type config with RevisionsPerItem of 0
    When the WorkItems export module starts
    Then an InvalidOperationException is thrown before any revision is written

  @simulated @offline
  Scenario: Export does not make any network calls
    When the WorkItems export module runs with a Simulated source
    Then no HTTP requests are made to any external system
