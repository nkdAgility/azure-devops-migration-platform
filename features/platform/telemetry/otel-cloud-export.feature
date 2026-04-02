Feature: OTel Cloud Provider Export
  As a migration operator
  I want metrics and traces to flow to OTLP and/or Azure Monitor when configured
  So that I can observe running jobs in my chosen observability platform

  Background:
    Given the migration platform is initialised with a fresh DI container

  Scenario: OTLP exporter is registered when OTEL_EXPORTER_OTLP_ENDPOINT is set
    Given the environment variable "OTEL_EXPORTER_OTLP_ENDPOINT" is set to "http://localhost:4317"
    When the platform services are built
    Then an OTLP metric exporter is registered in the OpenTelemetry MeterProvider

  Scenario: Azure Monitor exporter is registered when AzureMonitorConnectionString is configured
    Given appsettings contain "Telemetry:AzureMonitorConnectionString" with a valid connection string
    When the platform services are built
    Then an Azure Monitor metric exporter is registered in the OpenTelemetry MeterProvider

  Scenario: No cloud exporter is registered when neither is configured
    Given the environment variable "OTEL_EXPORTER_OTLP_ENDPOINT" is not set
    And appsettings do not contain "Telemetry:AzureMonitorConnectionString"
    When the platform services are built
    Then no OTLP exporter is registered in the OpenTelemetry MeterProvider
    And no Azure Monitor exporter is registered in the OpenTelemetry MeterProvider

  Scenario: SnapshotMetricExporter is always registered regardless of cloud configuration
    Given appsettings do not contain "Telemetry:AzureMonitorConnectionString"
    And the environment variable "OTEL_EXPORTER_OTLP_ENDPOINT" is not set
    When the platform services are built
    Then a SnapshotMetricExporter is registered in the OpenTelemetry MeterProvider
    And IMetricSnapshotStore is resolvable from the DI container

  Scenario: Both OTLP and Azure Monitor exporters coexist when both are configured
    Given the environment variable "OTEL_EXPORTER_OTLP_ENDPOINT" is set to "http://localhost:4317"
    And appsettings contain "Telemetry:AzureMonitorConnectionString" with a valid connection string
    When the platform services are built
    Then an OTLP metric exporter is registered in the OpenTelemetry MeterProvider
    And an Azure Monitor metric exporter is registered in the OpenTelemetry MeterProvider
