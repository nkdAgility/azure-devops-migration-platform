Feature: Cross-Project Link Detection
  As a migration planner
  I want to identify work items that link across project boundaries
  So that I can plan migration order and avoid broken references

  Scenario: Detect cross-project related links
    Given project "ProjectA" has work items linking to project "ProjectB"
    When I run dependency discovery for "ProjectA"
    Then the dependencies report should include cross-project links
    And each link should identify the source and target projects

  Scenario: Detect cross-organisation links
    Given project "ProjectA" has work items linking to a different organisation
    When I run dependency discovery for "ProjectA"
    Then the dependencies report should flag cross-organisation links
    And cross-organisation links should be counted separately

  Scenario: No dependencies found
    Given project "IsolatedProject" has no external work item links
    When I run dependency discovery for "IsolatedProject"
    Then the dependencies report should show zero cross-project links
    And a completion metric should still be recorded
