@platform
Feature: Plan-Driven Execution
  The agent must execute modules in dependency order using a topological tier sort,
  persist the plan to the package after each task transition, and resume from persisted
  state after a crash. Circular dependencies are detected before any module executes.

  Background:
    Given a migration package in the working directory
    And the package configuration enables all modules

  Scenario: Import executes in dependency order
    Given the WorkItems module depends on Identities and Nodes
    When the agent runs an Import job
    Then the Identities task completes before the WorkItems task starts
    And the Nodes task completes before the WorkItems task starts
    And the Teams task may run concurrently with Identities and Nodes

  Scenario: Failed dependency causes dependent task to be skipped
    Given the Identities module is configured to throw an exception on import
    When the agent runs an Import job
    Then the Identities task status is Failed
    And the WorkItems task status is Skipped
    And the WorkItems task SkipReason contains "Identities"
    And the Nodes task status is Completed
    And the Teams task status is Completed

  Scenario: Disabled dependency causes dependent task to be skipped
    Given the Identities module is disabled in configuration
    When the agent runs an Import job
    Then the Identities task status is Skipped
    And the Identities task SkipReason contains "disabled"
    And the WorkItems task status is Skipped
    And the WorkItems task SkipReason contains "Identities"

  Scenario: Circular dependency detected before any module executes
    Given the Identities module depends on WorkItems
    And the WorkItems module depends on Identities
    When the agent attempts to build the execution plan
    Then the plan builder throws InvalidOperationException
    And the exception message contains "Circular dependency"
    And no module ExportAsync or ImportAsync was called

  Scenario: Plan file written to package after first module completes
    Given the package has no existing plan file
    When the agent runs an Export job
    And the first module completes
    Then the plan file exists at .migration/Checkpoints/plan.json
    And the plan contains the completed task with Status = Completed
    And the completed task has a non-null CompletedAt timestamp

  Scenario: Running tasks reset to Pending on resume
    Given the plan file contains a task with Status = Running
    And the task StartedAt timestamp is 5 minutes ago
    When the agent loads the plan on resume
    Then the task Status is reset to Pending
    And the task StartedAt is set to null

  Scenario: Completed tasks not re-executed on resume
    Given an Export job completed with all tasks Completed in the plan file
    When the agent resumes the Export job without ForceFresh
    Then the Identities module ExportAsync is not called
    And the Nodes module ExportAsync is not called
    And the Teams module ExportAsync is not called
    And the WorkItems module ExportAsync is not called
    And the job completes successfully

  Scenario: ForceFresh deletes plan file and rebuilds
    Given an existing plan file with tasks Completed
    And module cursors exist for completed modules
    When the agent runs with ForceFresh resume mode
    Then the plan file is deleted before the first module executes
    And the module cursors are deleted
    And a fresh plan is built with all tasks Pending
    And all module ExportAsync or ImportAsync methods are called again
