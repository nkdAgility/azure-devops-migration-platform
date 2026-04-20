Feature: In-flight concurrency and queue depth metrics
  As a migration operator
  I want to see how many work items are being processed concurrently and the queue backlog
  So that I can monitor resource utilisation and detect bottlenecks

  Background:
    Given a migration configuration targeting the Simulated source
    And the configuration specifies operation "export" for module "workitems"

  @simulated
  Scenario: In-flight counter reflects concurrent processing
    Given a migration job with max concurrency set to 4
    And 100 work items are queued for export
    When the export is running
    Then the "migration.workitems.in_flight" counter is between 0 and 4

  @simulated
  Scenario: Queue depth starts high and decreases as items are processed
    Given a migration job with 100 work items queued for export
    When the export begins processing
    Then the "migration.queue.workitems.depth" gauge starts near 100
    And the gauge value decreases as work items are completed
