Feature: Dependency Discovery Execution
  As a migration planner
  I want to analyse cross-project work item links
  So that I can identify dependencies before migration

  Background:
    Given a package with completed inventory data

  Scenario: Discover dependencies for a single project
    Given an organisation "https://dev.azure.com/contoso" with project "ProjectA"
    When I run dependency discovery
    Then a dependencies CSV should be written to the package
    And the CSV should contain link records for "ProjectA"

  Scenario: Resume dependency discovery after interruption
    Given a dependency discovery that was interrupted after analysing "ProjectA"
    When I run dependency discovery again
    Then discovery should resume from the checkpoint
    And "ProjectA" should not be re-analysed
    And the final CSV should include all projects

  Scenario: Checkpoint is saved after each project
    Given organisations with projects "ProjectA" and "ProjectB"
    When dependency discovery completes "ProjectA"
    Then a checkpoint should be persisted
    And the checkpoint should record per-project statistics
