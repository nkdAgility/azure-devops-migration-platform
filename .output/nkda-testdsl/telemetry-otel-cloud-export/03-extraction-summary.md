# Extraction Summary: telemetry-otel-cloud-export

No shared DSL infrastructure was extracted. The test helpers used are:
- `Host.CreateApplicationBuilder()` from `Microsoft.Extensions.Hosting`
- `ConfigurationBuilder` + `AddInMemoryCollection` for config setup
- Standard `IServiceCollection` descriptor inspection via LINQ

The `Microsoft.Extensions.Hosting` package reference was added to the test project.
