# Conversion Summary: telemetry-otel-cloud-export

## Scenarios Converted (5/5)

| Scenario | Test Method | Result |
|---|---|---|
| OTLP exporter is registered when OTEL_EXPORTER_OTLP_ENDPOINT is set | OtlpExporter_IsRegistered_WhenEndpointEnvVarIsSet | PASS |
| Azure Monitor exporter is registered when AzureMonitorConnectionString is configured | AzureMonitorExporter_IsRegistered_WhenConnectionStringIsConfigured | PASS |
| No cloud exporter is registered when neither is configured | NoOtlpExporter_WhenEndpointEnvVarIsAbsent + NoAzureMonitorExporter_WhenConnectionStringIsAbsent | PASS |
| SnapshotMetricExporter is always registered regardless of cloud configuration | SnapshotMetricExporter_IsRegistered_WhenControlPlaneTelemetryServicesAdded + IJobMetricsStore_IsResolvable_FromDiContainer | PASS |
| Both OTLP and Azure Monitor exporters coexist when both are configured | BothExporters_AreRegistered_WhenBothAreConfigured | PASS |

Total: 7 test methods covering 5 scenarios. All pass.

## Commit
`719d9157` — test: telemetry-otel-cloud-export — all 5 scenarios mapped to DSL
`8bc56493` — migrate: telemetry-otel-cloud-export feature → DSL
