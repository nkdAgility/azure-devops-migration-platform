# DSL Design: Post-Flight Correctness Metrics

## Approach
Tests are written directly against `PlatformMetrics` using the existing `MeterListener` harness established in `PlatformMetricsTests.cs`. No new DSL surface was required — the existing metric recording API is expressive enough.

## Test class
`PostFlightCorrectnessMetricsTests` in `DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Telemetry/`

## Helper method
`SimulatePostFlightValidationWithSampleRate(PlatformMetrics, MetricsTagList, double)` gates metric recording on sampleRate > 0, matching the orchestrator's expected behavior.
