Feature: Job Submission
  As a migration operator
  I want to submit migration jobs to the control plane
  So that they are queued for processing by the migration agent

  Background:
    Given a running control plane

  Scenario: Submit an export job
    When I submit an export job for organisation "https://dev.azure.com/contoso" project "MyProject"
    Then the job should be in "Queued" state
    And the job should have a unique job ID

  Scenario: Submit an import job
    When I submit an import job for organisation "https://dev.azure.com/contoso" project "TargetProject"
    Then the job should be in "Queued" state
    And the job should have a unique job ID

  Scenario: Submit a both-mode job
    When I submit a both-mode job with source "https://dev.azure.com/source" and target "https://dev.azure.com/target"
    Then the job should be in "Queued" state

  Scenario: Dequeue a submitted job
    Given I have submitted an export job
    When the migration agent dequeues the next job
    Then the dequeued job should match the submitted job
