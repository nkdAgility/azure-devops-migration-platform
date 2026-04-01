Feature: TFS Export CLI Command
  As a migration operator
  I want to invoke the tfsexport command from the devopsmigration CLI
  So that work item data from a TFS server is exported to the canonical package layout

  Background:
    Given the devopsmigration CLI is installed and on the PATH
    And TFS export is available and configured

  @tfs-object-model @cli
  Scenario: Successful export streams live progress to the terminal
    Given the TFS server at "https://mytfs:8080/tfs" is reachable
    And the project "Contoso" exists on that server
    When the operator runs "devopsmigration tfsexport --tfsserver https://mytfs:8080/tfs --project Contoso"
    Then the terminal displays a live status showing total work items, processed work items, and processed revisions
    And the status updates as each work item is processed
    And on completion the terminal shows a success confirmation

  @tfs-object-model @cli
  Scenario: Export validates TFS server URL before starting
    When the operator runs "devopsmigration tfsexport --tfsserver not-a-url --project Contoso"
    Then the command exits with a non-zero exit code
    And the terminal displays a validation error indicating the TFS server must be a valid HTTP or HTTPS URL

  @tfs-object-model @cli
  Scenario: Export requires a non-empty project name
    When the operator runs "devopsmigration tfsexport --tfsserver https://mytfs:8080/tfs --project ''"
    Then the command exits with a non-zero exit code
    And the terminal displays a validation error indicating a project name must be provided

  @tfs-object-model @cli
  Scenario: Export requires a non-empty output folder
    When the operator runs "devopsmigration tfsexport --tfsserver https://mytfs:8080/tfs --project Contoso --output ''"
    Then the command exits with a non-zero exit code
    And the terminal displays a validation error indicating the output folder must be provided

  @tfs-object-model @cli
  Scenario: TFS export output is streamed to the operator in real time
    Given TFS export is available
    When the operator runs the tfsexport command
    Then output lines appear in the terminal as the export progresses
    And error output is visually distinguished from standard output in the terminal

  @tfs-object-model @cli
  Scenario: A non-zero subprocess exit code is propagated as the CLI exit code
    Given the tfsexport subprocess exits with code 2
    When the tfsexport command is invoked
    Then the devopsmigration CLI exits with code 2
    And the terminal displays an error message indicating the TFS export failed with that exit code

  @tfs-object-model @cli
  Scenario: TFS export being unavailable produces a clear error before any export begins
    Given TFS export is not available
    When the operator runs the tfsexport command
    Then the command exits with a non-zero exit code
    And the terminal displays an error message explaining that TFS export could not be started

  @tfs-object-model @cli
  Scenario: Chunk progress is shown including date range and work item counts within the chunk
    Given a project with work items spread across multiple date chunks
    When the tfsexport command is running
    Then the live status shows the current chunk start date, chunk end date, and the number of work items within that chunk
