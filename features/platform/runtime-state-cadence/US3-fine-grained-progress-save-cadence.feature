@platform
Feature: Fine-grained progress and durable save cadence
  Ensure long-running processing reports steady progress and persists near-latest checkpoints.

  @runtime-state-us3
  Scenario: Processing_ProgressAndCheckpointCadence_RemainsNearLatestOnResume
    Given a long-running work item operation emits incremental progress
    When interruption occurs between durable checkpoint boundaries
    Then replay after resume remains within the defined replay threshold
    And progress output continues with steady forward movement
