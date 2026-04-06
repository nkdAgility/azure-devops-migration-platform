Feature: CLI Commands Execute Successfully Without Errors
    As a migration platform operator
    I want all CLI commands to execute successfully without runtime errors
    So that I can confidently use the platform for migrations

    Background:
        Given the DevOps migration CLI is available
        And the platform services are accessible

    Scenario: Discovery inventory command executes with valid config
        Given a valid migration config file exists at "migration.json"
        When I run "devopsmigration discovery inventory --config migration.json"
        Then the command should complete with exit code 0
        And the output should contain inventory results
        And no runtime exceptions should occur

    Scenario: Discovery inventory command fails gracefully with invalid config
        Given an invalid config file path "invalid-path.json"
        When I run "devopsmigration discovery inventory --config invalid-path.json"
        Then the command should display a clear error message
        And the command should return a non-zero exit code
        And no unhandled exceptions should occur

    Scenario: Commands work without explicit config file
        Given no config file is specified
        And a default "migration.json" exists in the current directory
        When I run "devopsmigration discovery inventory"
        Then the system should use the default "migration.json"
        And the command should execute successfully with exit code 0

    Scenario: Help text displays correctly for all commands
        When I pass "--help" to the "discovery inventory" command
        Then the command should display comprehensive help text
        And the command should exit with code 0
        And no errors should be displayed

    Scenario: TFS export command executes with valid parameters
        Given valid TFS connection parameters are provided
        When I run "devopsmigration tfsexport --collection http://tfs:8080/tfs/DefaultCollection --project MyProject --output ./package"
        Then the command should execute successfully with exit code 0
        And the TFS export process should begin
        And no runtime exceptions should occur

    Scenario: Logs command executes with valid job ID
        Given a valid job ID "00000000-0000-0000-0000-000000000001" exists
        When I run "devopsmigration logs --job 00000000-0000-0000-0000-000000000001"
        Then the command should execute successfully with exit code 0
        And log output should be displayed
        And no runtime exceptions should occur

    Scenario: Commands handle missing required parameters gracefully
        Given required parameters are not provided
        When I run a command with missing required parameters
        Then the command should display appropriate error messages
        And the command should return a non-zero exit code
        And help information should be suggested
        And no unhandled exceptions should occur