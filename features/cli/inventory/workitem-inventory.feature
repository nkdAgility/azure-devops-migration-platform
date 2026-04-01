Feature: Discovery Inventory CLI Command
  As a migration operator
  I want to run "devopsmigration discovery inventory" against an Azure DevOps organisation
  So that I can see a live-updating table of work item and revision counts per project before planning a migration

  Background:
    Given the devopsmigration CLI is installed and on the PATH
    And the operator provides a valid organisation URL and Personal Access Token

  @azure-devops-rest @cli
  Scenario: Live table appears immediately and updates as each project is counted
    Given the organisation contains projects "Alpha", "Beta", and "Gamma"
    When the operator runs "devopsmigration discovery inventory --organisation https://dev.azure.com/myorg --token <pat>"
    Then a table is rendered in the terminal before any counting begins
    And the table gains a row for each project as its row is first populated
    And the Work Items and Revisions columns update in place for each project as counting progresses

  @azure-devops-rest @cli
  Scenario: Table shows all expected columns
    Given any organisation with at least one project
    When the discovery inventory command runs
    Then the rendered table has columns: "Project", "Work Items", "Revisions", "Repos", "Pipelines", and "Updated"

  @azure-devops-rest @cli
  Scenario: Updated column reflects the time of the last count update for each project
    Given the discovery inventory command is streaming results for "Alpha"
    When a new work item count update arrives
    Then the "Updated" cell for "Alpha" shows a time value in HH:mm:ss format reflecting when that update was received

  @cli
  Scenario: On completion a CSV summary file is saved to the working directory
    Given the discovery inventory command has finished counting all projects
    Then a file named "discovery-summary.csv" is created in the current working directory
    And the CSV contains one row per project with columns for name, work item count, revision count, repo count, and pipeline count
    And the terminal displays a success message confirming the file path

  @azure-devops-rest @cli
  Scenario: An organisation with zero projects completes without error
    Given the Azure DevOps organisation has no projects
    When the operator runs the discovery inventory command
    Then an empty table is displayed
    And the command exits with exit code 0
    And an empty "discovery-summary.csv" file is created with only the CSV header row

  @azure-devops-rest @cli
  Scenario: An invalid PAT causes the command to exit with a non-zero code
    Given the supplied Personal Access Token is not valid
    When the discovery inventory command is run
    Then the command exits with a non-zero exit code
    And the terminal displays an error message describing the authentication failure

  @azure-devops-rest @cli
  Scenario: Projects are counted sequentially and the table reflects each update in real time
    Given the organisation has 5 projects
    When the discovery inventory command is running
    Then each project is counted to completion before the next project counting begins
    And the live table shows the final counts for earlier projects while later projects are still being counted
