Feature: CLI OTel Observability
  As a platform operator
  I want the CLI to emit traces and metrics to Azure Monitor
  So that I can observe command execution in Application Insights without a Control Plane

  @us1 @p1 @cli @telemetry
  Scenario: Span appears in Azure Monitor when connection string is configured
    Given a valid Azure Monitor connection string is configured under "Telemetry:AzureMonitorConnectionString"
    When I run a CLI command to completion
    Then a trace span for that command is exported to the telemetry pipeline

  @us1 @p1 @cli @telemetry
  Scenario: Span is marked failed when the command exits with a non-zero code
    Given a valid Azure Monitor connection string is configured under "Telemetry:AzureMonitorConnectionString"
    When I run a CLI command that throws an unhandled exception
    Then the trace span status is Error with the exception message attached

  @us1 @p1 @cli @telemetry
  Scenario: Command runs normally when no connection string is configured
    Given no Azure Monitor connection string is configured
    When I run a CLI command to completion
    Then the command exits with code 0
    And no external telemetry exporter is registered

  @us1 @p1 @cli @telemetry
  Scenario: Root ActivitySource is created on CLI startup
    Given the CLI process initialises Program.cs
    When the DI container is built
    Then an ActivitySource named "DevOpsMigrationPlatform.CLI" is registered as a singleton
