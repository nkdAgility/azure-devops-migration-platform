# Conversion Summary: telemetry-idempotency-metric-registration

## Scenarios Converted

### Scenario 1: Deferred idempotency instruments are registered at startup
- Mapped to: `PlatformMetricsTests.IdempotencyCounters_AreRegisteredAtStartup` (NEW)
- File: `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Telemetry/PlatformMetricsTests.cs`
- Uses a dedicated MeterListener to capture InstrumentPublished events at construction time.

### Scenario 2: Deferred instruments accept increments when mapping store is available
- Mapped to pre-existing tests:
  - `PlatformMetricsTests.RecordDuplicated_EmitsCounter`
  - `PlatformMetricsTests.RecordChangedOnRerun_EmitsCounter`
  - `PlatformMetricsTests.RecordReprocessedAfterResume_EmitsCounter`
  - `PlatformMetricsTests.RecordDuplicatedAfterResume_EmitsCounter`
  - `PlatformMetricsTests.RecordMissingAfterResume_EmitsCounter`

## Test Hygiene
Added `[TestCategory("UnitTest")]` to all 39 [TestMethod] entries in the class.
