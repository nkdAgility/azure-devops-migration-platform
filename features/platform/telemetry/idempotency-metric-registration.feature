Feature: Idempotency metric instrument registration
  As a migration operator
  I want deferred idempotency counters registered at startup
  So that they are available for recording when the identity mapping store is implemented

  Background:
    Given a migration configuration targeting the Simulated source

  @simulated
  Scenario: Deferred idempotency instruments are registered at startup
    Given the migration agent starts
    When the telemetry pipeline initialises
    Then the following counters are registered under the "DevOpsMigrationPlatform.Migration" meter:
      | Counter Name                                  |
      | migration.idempotency.duplicated              |
      | migration.idempotency.changed_on_rerun        |
      | migration.idempotency.reprocessed_after_resume |
      | migration.idempotency.duplicated_after_resume  |
      | migration.idempotency.missing_after_resume     |

  @simulated
  Scenario: Deferred instruments accept increments when mapping store is available
    Given the migration agent starts with a configured identity mapping store
    When a duplicate work item is detected during import
    Then the "migration.idempotency.duplicated" counter can be incremented
