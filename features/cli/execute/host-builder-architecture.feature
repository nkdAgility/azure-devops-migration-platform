@cli @execute @architecture
Feature: Host Builder Architecture
  As a platform developer
  I need the host builder architecture to correctly separate concerns
  So that commands can register their own services without modifying shared infrastructure

  Background:
    Given the Azure DevOps Migration Platform CLI is available

  Scenario: Shared infrastructure services are always registered
    When a CLI command creates a host via MigrationPlatformHost.CreateDefaultBuilder
    Then IOptions<EnvironmentOptions> is resolvable from the service provider
    And IAnsiConsole is resolvable from the service provider
    And OpenTelemetry tracing is configured

  Scenario: Command-specific services are isolated to their host
    Given a command registers its own IFoo service via the configureServices delegate
    When the host is built
    Then the IFoo service is resolvable
    And other commands that do not register IFoo cannot resolve it

  Scenario: Default config file is migration.json when --config is not specified
    When no --config argument is provided
    Then the configuration layers include "migration.json" from the current directory

  Scenario: Config file from --config argument overrides default
    Given the argument "--config scenarios/my-scenario.json" is provided
    When the host extracts the config file argument
    Then the config file path is resolved to an absolute path ending in "my-scenario.json"
    And the remaining arguments do not include "--config" or the file path

  Scenario: ValidateOnStart fails immediately for invalid configuration
    Given a command registers IOptions<T> with ValidateOnStart
    And the configuration contains invalid values for that options type
    When the host is started
    Then an OptionsValidationException is thrown before the command executes
