@platform
Feature: Job Execution Plan Bootstrap
  As a CLI or TUI client
  I want to receive the ordered execution plan from GET /jobs/{id}/bootstrap
  So that I can display expected tasks before the job runs

  Background:
    Given a job is submitted and an agent acquires the lease

  Scenario: Bootstrap_WhenAgentPushedPlan_ReturnsPlanWithOrderedTasks
    Given the agent has pushed an execution plan with 4 tasks
    When the client calls GET /jobs/{jobId}/bootstrap
    Then the response includes a Tasks list with 4 entries in ascending order

  Scenario: Bootstrap_BeforePlanPushed_ReturnNullTasks
    Given the agent has not yet pushed an execution plan
    When the client calls GET /jobs/{jobId}/bootstrap
    Then the response Tasks field is null

  Scenario: GetTasks_WhenTaskListExists_ReturnsCurrentTaskList
    Given the agent has pushed an execution plan with 3 tasks
    When the client calls GET /jobs/{jobId}/tasks
    Then the response contains a task list with 3 tasks

  Scenario: GetTasks_WhenNoTaskListPushed_Returns204
    Given no execution plan has been pushed for the job
    When the client calls GET /jobs/{jobId}/tasks
    Then the response status is 204
