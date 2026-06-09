# Feature Assessment: telemetry-otel-cloud-export

## Feature File
`features/platform/telemetry/otel-cloud-export.feature`

## Wiring State
**Unwired** — no Reqnroll step bindings existed for these scenarios in the tests/ tree.

## Scenarios (5)

1. OTLP exporter is registered when OTEL_EXPORTER_OTLP_ENDPOINT is set
2. Azure Monitor exporter is registered when AzureMonitorConnectionString is configured
3. No cloud exporter is registered when neither is configured
4. SnapshotMetricExporter is always registered regardless of cloud configuration
5. Both OTLP and Azure Monitor exporters coexist when both are configured

## Source Under Test
- `src/DevOpsMigrationPlatform.ServiceDefaults/Extensions.cs` — `AddOpenTelemetryExporters` (private), called by `ConfigureOpenTelemetry`
- `src/DevOpsMigrationPlatform.Infrastructure.ControlPlane/Metrics/TelemetryServiceExtensions.cs` — `AddControlPlaneTelemetryServices`
- `src/DevOpsMigrationPlatform.Infrastructure.ControlPlane/Metrics/SnapshotMetricExporter.cs`

## Target Test Project
`tests/DevOpsMigrationPlatform.Infrastructure.Tests` — already references ServiceDefaults and has OpenTelemetry packages.

## Migration Risk
Low. The logic is a pure conditional service registration based on configuration values. Tests can verify via `IServiceCollection` descriptor inspection.
