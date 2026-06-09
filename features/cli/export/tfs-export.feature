Feature: TFS Export via Export Command
  As a migration operator
  I want to invoke the export command with a TFS configuration file
  So that work item data from a TFS server is exported to the canonical package layout

  Background:
    Given the devopsmigration CLI is installed and on the PATH
    And TFS export is available and configured

  @tfs-object-model @cli
  Scenario: Successful TFS export streams live progress to the terminal
    Given a valid TFS export config file at "scenarios/export-tfs-workitems.json"
    And the TFS server referenced in the config is reachable
    When the operator runs "devopsmigration export --config scenarios/export-tfs-workitems.json"
    Then the terminal displays a live status showing total work items, processed work items, and processed revisions
    And the status updates as each work item is processed
    And on completion the terminal shows a success confirmation

  @tfs-object-model @cli
  Scenario: Export validates TFS server URL before starting
    Given a TFS export config file with an invalid server URL "not-a-url"
    When the operator runs "devopsmigration export --config scenarios/export-tfs-invalid-url.json"
    Then the command exits with a non-zero exit code
    And the terminal displays a validation error indicating the TFS server must be a valid HTTP or HTTPS URL

  @tfs-object-model @cli
  Scenario: TFS export output is streamed to the operator in real time
    Given TFS export is available
    And a valid TFS export config file at "scenarios/export-tfs-workitems.json"
    When the operator runs "devopsmigration export --config scenarios/export-tfs-workitems.json"
    Then output lines appear in the terminal as the export progresses
    And error output is visually distinguished from standard output in the terminal

  @tfs-object-model @cli
  Scenario: A non-zero subprocess exit code is propagated as the CLI exit code
    Given the tfsexport subprocess exits with code 2
    And a valid TFS export config file at "scenarios/export-tfs-workitems.json"
    When the operator runs "devopsmigration export --config scenarios/export-tfs-workitems.json"
    Then the devopsmigration CLI exits with code 2
    And the terminal displays an error message indicating the TFS export failed with that exit code

  @tfs-object-model @cli
  Scenario: TFS export being unavailable produces a clear error before any export begins
    Given TFS export is not available
    And a valid TFS export config file at "scenarios/export-tfs-workitems.json"
    When the operator runs "devopsmigration export --config scenarios/export-tfs-workitems.json"
    Then the command exits with a non-zero exit code
    And the terminal displays an error message explaining that TFS export could not be started

  @tfs-object-model @cli
  Scenario: Chunk progress is shown including date range and work item counts within the chunk
    Given a project with work items spread across multiple date chunks
    And a valid TFS export config file at "scenarios/export-tfs-workitems.json"
    When the operator runs "devopsmigration export --config scenarios/export-tfs-workitems.json"
    Then the live status shows the current chunk start date, chunk end date, and the number of work items within that chunk
