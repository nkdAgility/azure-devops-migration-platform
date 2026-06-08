# DSL Design: telemetry-otel-cloud-export

## Approach
Direct MSTest [TestMethod] tests using `Host.CreateApplicationBuilder()` with in-memory configuration.
Tests call `builder.ConfigureOpenTelemetry()` then inspect `IServiceCollection` for registered descriptors matching OTel exporter type names.

## Test Class
`OtelCloudExportTests` in `DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry`

## Assertion Strategy
- OTLP: `IServiceCollection.Any(sd => sd.ServiceType.FullName.Contains("Otlp"))`
- Azure Monitor: `IServiceCollection.Any(sd => sd.ServiceType.FullName.Contains("AzureMonitor"))`
- IJobMetricsStore: Direct DI resolution via `ServiceCollection`

## Package Added
`Microsoft.Extensions.Hosting` added to test project csproj (already in Directory.Packages.props at v10.0.8).
