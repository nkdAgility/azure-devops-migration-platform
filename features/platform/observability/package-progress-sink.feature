Feature: Package progress sink persistence
  As a migration operator
  I want progress events persisted to the migration package
  So that the package is a complete audit trail even without access to the original control plane

  Background:
    Given a Migration Agent is executing an export job

  Scenario: Progress sink writes are non-blocking
    When a progress event is emitted via the progress sink
    Then the write does not block the export pipeline
    And the event is buffered internally before being flushed to the package
