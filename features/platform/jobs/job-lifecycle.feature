Feature: Job Lifecycle
  As a migration operator
  I want jobs to transition through well-defined states
  So that I can monitor progress and detect failures

  Background:
    Given a running control plane
    And a submitted export job

  Scenario: Job transitions from Queued to Running
    When the migration agent starts processing the job
    Then the job state should transition to "Running"
    And a JobStarted event should be raised

  Scenario: Job transitions from Running to Completed
    Given the job is in "Running" state
    When the migration agent completes the job successfully
    Then the job state should transition to "Completed"
    And a JobCompleted event should be raised
    And the job duration should be recorded

  Scenario: Job transitions from Running to Failed
    Given the job is in "Running" state
    When the migration agent encounters an unrecoverable error
    Then the job state should transition to "Failed"
    And a JobFailed event should be raised
    And the failure reason should be recorded

  Scenario: Multiple state updates during processing
    Given the job is in "Running" state
    When the migration agent reports progress updates
    Then each update should preserve the "Running" state
    And progress events should be forwarded to subscribers
