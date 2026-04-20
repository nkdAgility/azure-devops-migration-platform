Feature: Export execution metrics
  As a migration operator
  I want to see real-time counters for attempted, completed, failed, and retried work items
  So that I can assess whether an export is progressing normally or stalling

  Background:
    Given a migration configuration targeting the Simulated source
    And the configuration specifies operation "export" for module "workitems"

  @simulated
  Scenario: Successful export emits matching attempted and completed counters
    Given a migration job in Export mode processing 50 work items
    When the export completes
    Then the "migration.workitems.attempted" counter equals 50
    And the "migration.workitems.completed" counter equals 50
    And the "migration.workitems.failed" counter equals 0
    And every metric carries the tag "operation" with value "export"
    And every metric carries the tag "module" with value "workitems"
    And every metric carries a non-empty "job.id" tag

  @simulated
  Scenario: Transient failures increment the retried counter
    Given a migration job in Export mode processing 50 work items
    And 3 work items are configured to fail with a transient error on first attempt
    When the export completes
    Then the "migration.workitems.retried" counter equals 3
    And the "migration.workitems.attempted" counter is greater than 50

  @simulated
  Scenario: Duration histogram records per-work-item processing time
    Given a migration job in Export mode processing 50 work items
    When the export completes
    Then the "migration.workitem.duration.ms" histogram has recorded 50 measurements
    And the MetricSnapshot property "WorkItemDurationMeanMs" is greater than 0
