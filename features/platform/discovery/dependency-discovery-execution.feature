Feature: Dependency Discovery Execution
  As a migration planner
  I want to analyse cross-project work item links
  So that I can identify dependencies before migration

  Background:
    Given a package with completed inventory data

  Scenario: Resume dependency discovery after interruption
    Given a dependency discovery that was interrupted after analysing "ProjectA"
    When I run dependency discovery again
    Then discovery should resume from the checkpoint
    And "ProjectA" should not be re-analysed
    And the final CSV should include all projects
