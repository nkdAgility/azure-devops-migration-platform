Feature: Export source classification tree capture
  As a migration operator
  I want export to capture the complete source area and iteration tree
  So that import can replicate the full classification structure

  Background:
    Given a source project with classification nodes

  Scenario: Export captures all area nodes as strings
    Given the source project has area nodes "ProjectA\Team A" and "ProjectA\Team A\Sub"
    When the classification tree is captured during export
    Then the source-tree artifact contains area node "ProjectA\Team A"
    And the source-tree artifact contains area node "ProjectA\Team A\Sub"

  Scenario: Export captures iteration nodes with dates
    Given the source project has iteration node "ProjectA\Sprint 1" with start "2024-01-15" and finish "2024-01-28"
    When the classification tree is captured during export
    Then the source-tree artifact contains iteration node "ProjectA\Sprint 1" with start "2024-01-15" and finish "2024-01-28"

  Scenario: Export captures iteration node with null dates
    Given the source project has iteration node "ProjectA\Backlog" with no dates
    When the classification tree is captured during export
    Then the source-tree artifact contains iteration node "ProjectA\Backlog" with no dates

  Scenario: API failure during tree capture is surfaced
    Given the classification tree reader throws an exception
    When the classification tree capture is attempted
    Then the capture fails with an error
