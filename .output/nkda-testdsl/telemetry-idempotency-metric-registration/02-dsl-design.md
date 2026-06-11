# DSL Design: telemetry-idempotency-metric-registration

## Target Test Class
`PlatformMetricsTests` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Telemetry/PlatformMetricsTests.cs`

## DSL Patterns Used

### Registration verification pattern
```csharp
var publishedNames = new List<string>();
using var registrationListener = new MeterListener();
registrationListener.InstrumentPublished = (instrument, _) =>
{
    if (instrument.Meter.Name == WellKnownMeterNames.Agent)
        publishedNames.Add(instrument.Name);
};
registrationListener.Start();
using var sut = new PlatformMetrics();
Assert.IsTrue(publishedNames.Contains(WellKnownAgentMetricNames.X));
```

### Increment verification pattern (pre-existing)
```csharp
using var sut = new PlatformMetrics();
sut.RecordDuplicated(CreateExecutionTags());
var entry = _recorded.Single(r => r.Name == WellKnownAgentMetricNames.Duplicated);
Assert.AreEqual(1L, entry.Value);
```
