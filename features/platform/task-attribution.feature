@platform
Feature: Task Attribution via ProgressEvent
  As a Control Plane consumer
  I want task status to be derived from ProgressEvent.TaskId and TaskStatus fields
  So that the task list reflects live execution state without a separate push

  Background:
    Given a job with a pushed execution plan containing tasks "export.identities" and "export.workitems"

  Scenario: TaskStatus_WhenRunningEventReceived_TransitionsTaskToRunning
    When a ProgressEvent with TaskId "export.identities" and TaskStatus Running arrives
    Then the task "export.identities" in the store has status Running

  Scenario: TaskStatus_WhenCompletedEventReceived_TransitionsTaskToCompleted
    Given a ProgressEvent with TaskId "export.identities" and TaskStatus Running has been applied
    When a ProgressEvent with TaskId "export.identities" and TaskStatus Completed arrives
    Then the task "export.identities" in the store has status Completed
    And the task "export.identities" CompletedAt is set

  Scenario: TaskStatus_WhenFailedEventReceived_TransitionsTaskToFailed
    Given a ProgressEvent with TaskId "export.workitems" and TaskStatus Running has been applied
    When a ProgressEvent with TaskId "export.workitems" and TaskStatus Failed arrives
    Then the task "export.workitems" in the store has status Failed

  Scenario: TaskStatus_WhenEventHasNoTaskId_OtherTasksUnchanged
    When a ProgressEvent with no TaskId arrives
    Then all tasks in the store retain their Pending status
