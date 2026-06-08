@platform
Feature: Plan-Driven Execution
  The agent must execute modules in dependency order using a topological tier sort,
  persist the plan to the package after each task transition, and resume from persisted
  state after a crash. Circular dependencies are detected before any module executes.

  Background:
    Given a migration package in the working directory
    And the package configuration enables all modules

  Scenario: ForceFresh deletes plan file and rebuilds
    Given an existing plan file with tasks Completed
    And module cursors exist for completed modules
    When the agent runs with ForceFresh resume mode
    Then the plan file is deleted before the first module executes
    And the module cursors are deleted
    And a fresh plan is built with all tasks Pending
    And all module ExportAsync or ImportAsync methods are called again
