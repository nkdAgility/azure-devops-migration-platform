# DSL Design: telemetry-progress-sink

## Test Class
`ControlPlaneProgressSinkTests` in `DevOpsMigrationPlatform.Infrastructure.Tests.Telemetry`

## Shared Context
`ControlPlaneProgressSinkContext` — provides `IHttpClientFactory` mock, `ActiveLeaseState`, and request capture.

## Test Methods
- `Emit_PostsProgressEventToControlPlane_WithinOneSecond`
- `Emit_AfterControlPlaneRestart_CreatesNewRingBufferAndStoresEvent`
- `Emit_WhenHttpEndpointUnreachable_DropsEventWithoutThrowingAndContinues`

## Pattern
Direct instantiation of `ControlPlaneProgressSink`, started as a `BackgroundService`, with a short `Task.Delay(300)` to allow the channel drain loop to process.
