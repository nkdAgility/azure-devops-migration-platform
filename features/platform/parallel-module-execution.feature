@platform
Feature: Parallel Module Execution
  Independent modules within the same execution tier run concurrently via Task.WhenAll.
  Export tasks have no dependencies and run as a single concurrent tier. Import tier-0
  tasks (Identities, Nodes, Teams) run concurrently; tier-1 tasks (WorkItems) wait for
  their dependencies. Failed tasks do not cancel sibling tasks in the same tier.

  Background:
    Given a migration package in the working directory
    And the package configuration enables all modules

  Scenario: All export tasks start within the same tier
    When the agent runs an Export job
    Then the Identities task StartedAt timestamp is recorded
    And the Nodes task StartedAt timestamp is recorded
    And the Teams task StartedAt timestamp is recorded
    And the WorkItems task StartedAt timestamp is recorded
    And at least three of the four tasks have StartedAt within 500 ms of each other

  Scenario: Import tier-0 tasks start concurrently before WorkItems
    When the agent runs an Import job
    Then the Identities task StartedAt timestamp is recorded
    And the Nodes task StartedAt timestamp is recorded
    And the Teams task StartedAt timestamp is recorded
    And at least two of Identities, Nodes, Teams have overlapping execution windows
    And the WorkItems task StartedAt is no earlier than the Identities task CompletedAt
    And the WorkItems task StartedAt is no earlier than the Nodes task CompletedAt

  Scenario: CancellationToken cancels all running tier tasks
    Given the agent is running an Export job
    And at least one export task has started
    When the CancellationToken is cancelled
    Then all running tasks receive the cancellation signal
    And no task transitions to Failed due to cancellation
    And the job status is Cancelled

  Scenario: Failed task does not cancel sibling tasks in the same tier
    Given the Nodes module is configured to throw an exception on import
    When the agent runs an Import job
    Then the Nodes task status is Failed
    And the Identities task status is Completed
    And the Teams task status is Completed
    And the WorkItems task status is Skipped
    And the WorkItems task SkipReason contains "Nodes"
