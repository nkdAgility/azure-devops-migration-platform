@cli @execute @configuration
Feature: Configuration Flow
  As a platform operator
  I need the --config parameter to correctly pass configuration data from the command line through to internal services
  So that my migration jobs use the correct settings

  Background:
    Given the Azure DevOps Migration Platform CLI is available

  Scenario: Custom config file with source URLs flows to internal services
    Given I have a config file "custom-test.json" with specific source URLs
      """
      {
        "Version": "2.0",
        "Mode": "Export",
        "Source": {
          "Type": "AzureDevOpsServices", 
          "Url": "https://dev.azure.com/custom-org",
          "Authentication": { "Type": "AccessToken", "PersonalAccessToken": "test-token" },
          "Project": { "Name": "TestProject" }
        },
        "Package": { "WorkingDirectory": "./test-output" }
      }
      """
    When I run "devopsmigration discovery inventory --config custom-test.json" 
    Then the command executes successfully
    And the internal services receive the source URL "https://dev.azure.com/custom-org"
    And the command logs show configuration was loaded from "custom-test.json"

  Scenario: Authentication settings flow correctly to connection services
    Given I have a config file "auth-test.json" with authentication settings
      """
      {
        "Version": "2.0", 
        "Mode": "Export",
        "Source": {
          "Type": "AzureDevOpsServices",
          "Url": "https://dev.azure.com/test-org", 
          "Authentication": { "Type": "AccessToken", "PersonalAccessToken": "secure-token-123" },
          "Project": { "Name": "TestProject" }
        },
        "Package": { "WorkingDirectory": "./test-output" }
      }
      """
    When I run "devopsmigration discovery inventory --config auth-test.json"
    Then the command executes successfully 
    And the authentication service receives PAT token "secure-token-123"
    And the connection service uses the provided authentication parameters

  Scenario: Telemetry configuration flows to telemetry system
    Given I have a config file "telemetry-test.json" with telemetry settings
      """
      {
        "Version": "2.0",
        "Mode": "Export", 
        "Source": {
          "Type": "AzureDevOpsServices",
          "Url": "https://dev.azure.com/test-org",
          "Authentication": { "Type": "AccessToken", "PersonalAccessToken": "test-token" },
          "Project": { "Name": "TestProject" }
        },
        "Package": { "WorkingDirectory": "./test-output" },
        "Telemetry": {
          "Enabled": true,
          "LogLevel": "Verbose",
          "EnableTracing": true
        }
      }
      """
    When I run "devopsmigration discovery inventory --config telemetry-test.json"
    Then the command executes successfully
    And the telemetry system is configured with log level "Verbose" 
    And OpenTelemetry tracing is enabled according to the config file

  Scenario: Default config resolution when no config specified
    Given no migration.json file exists in the current directory
    When I run "devopsmigration discovery inventory" without specifying --config
    Then the command shows an appropriate error message about missing configuration
    And the exit code is non-zero

  Scenario: Default config file is used when present
    Given I have a default config file "migration.json" in the current directory
      """
      {
        "Version": "2.0",
        "Mode": "Export",
        "Source": {
          "Type": "AzureDevOpsServices",
          "Url": "https://dev.azure.com/default-org", 
          "Authentication": { "Type": "AccessToken", "PersonalAccessToken": "default-token" },
          "Project": { "Name": "DefaultProject" }
        },
        "Package": { "WorkingDirectory": "./default-output" }
      }
      """
    When I run "devopsmigration discovery inventory" without specifying --config
    Then the command executes successfully
    And the configuration is loaded from the default "migration.json" file
    And the internal services receive the source URL "https://dev.azure.com/default-org"