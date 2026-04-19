Feature: Simulated Export-Import Roundtrip
  As a migration developer
  I want to run a full Both-mode migration with Simulated on both ends
  So that I can validate the end-to-end pipeline without any external credentials

  @simulated @offline @roundtrip
  Scenario: Both-mode job with Simulated source and target completes without error
    Given a roundtrip scenario config with Mode Both and Simulated source and target
    When the migration job is queued
    Then the export phase completes and writes revision folders to the package
    And the import phase reads from the package and completes without error
    And the final exit code is 0

  @simulated @offline @roundtrip
  Scenario: Package written by simulated export passes import validation
    Given the simulated export has completed
    When the import phase reads the package
    Then no validation errors are reported for any revision.json
    And all work items are created in the simulated target

  @simulated @offline @roundtrip
  Scenario: Roundtrip is fully offline — no network calls
    Given a roundtrip scenario config
    When the full Both-mode job runs
    Then no HTTP requests are made to any external host
