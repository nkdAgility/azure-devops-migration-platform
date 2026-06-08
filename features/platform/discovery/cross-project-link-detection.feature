Feature: Cross-Project Link Detection
  As a migration planner
  I want to identify work items that link across project boundaries
  So that I can plan migration order and avoid broken references

  Scenario: Detect cross-organisation links
    Given project "ProjectA" has work items linking to a different organisation
    When I run dependency discovery for "ProjectA"
    Then the dependencies report should flag cross-organisation links
    And cross-organisation links should be counted separately
