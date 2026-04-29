Feature: Import Team Capacity
  As a platform operator
  I want per-sprint capacity imported with iteration and identity mapping
  So that team velocity and capacity are correctly configured on the target

  Background:
    Given a team package with capacity data

  @import @teams @capacity
  Scenario: Import sets capacity for each iteration
    Given a team package with capacity for "sprint-1": alice@src.com (6h/day) and bob@src.com (4h/day)
    And identity mappings: "alice-desc" → "alice@target.com", "bob-desc" → "bob@target.com"
    When the Teams module imports the team package
    Then SetCapacityAsync is called for iteration "sprint-1" with 2 entries

  @import @teams @capacity
  Scenario: Capacity not supported on target is gracefully skipped
    Given a team package with capacity data
    And the target throws "not supported" when SetCapacityAsync is called
    When the Teams module imports the team package
    Then the import completes without error
    And an informational message is logged about capacity not being supported

  @import @teams @capacity
  Scenario: Empty capacity map results in no SetCapacityAsync calls
    Given a team package with no capacity data (empty capacityByIteration)
    When the Teams module imports the team package
    Then SetCapacityAsync is never called
