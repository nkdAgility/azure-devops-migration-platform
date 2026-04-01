Feature: TFS Export CLI Command
  As a migration operator
  I want to invoke the tfsexport command from the devopsmigration CLI
  So that work item data from a TFS server is exported to the canonical package layout via the TFS Object Model subprocess

  Background:
    Given the devopsmigration CLI is installed and on the PATH
    And the tfsexport subprocess executable is present at the expected relative path

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
  Scenario: Export uses the tfsexport subprocess and streams its stdout to the console
    Given the tfsexport subprocess is available
    When the operator runs the tfsexport command
    Then the devopsmigration CLI spawns the tfsexport subprocess
    And each line written to the subprocess stdout appears prefixed with "[tool]" in the parent terminal
    And each line written to the subprocess stderr appears prefixed with "[error]" in the parent terminal

  @tfs-object-model @cli
  Scenario: A non-zero subprocess exit code is propagated as the CLI exit code
    Given the tfsexport subprocess exits with code 2
    When the tfsexport command is invoked
    Then the devopsmigration CLI exits with code 2
    And the terminal displays an error message indicating the TFS export failed with that exit code

  @tfs-object-model @cli
  Scenario: Missing subprocess executable produces a clear error before any export begins
    Given the tfsexport subprocess executable does not exist at the expected path
    When the operator runs the tfsexport command
    Then the command exits with a non-zero exit code
    And the terminal displays an error message identifying the missing executable path

  @tfs-object-model @cli
  Scenario: Chunk progress is shown including date range and work item counts within the chunk
    Given a project with work items spread across multiple date chunks
    When the tfsexport command is running
    Then the live status shows the current chunk start date, chunk end date, and the number of work items within that chunk
