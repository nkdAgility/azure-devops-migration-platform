# Telemetry Development Guide

Audience: Contributors.

## Overview

The platform has four telemetry obligations for every module operation:

| Obligation | Mechanism | Requirement |
|---|---|---|
| O-1 Traces | `ActivitySource.StartActivity` | One span per module invocation with relevant tags |
| O-2 Metrics | `IMigrationMetrics` | Attempt, completion, error, duration, in-flight |
| O-3 Structured logging | `ILogger<T>` | Info on start/end, Warn on skips/errors, Debug per-item |
| O-4 Progress events | `IProgressSink` | Emit at start, per item (or per ≤50 batch), and completion |

## Activity Spans (O-1)

Use the module's `ActivitySource` to wrap the main operation:

```csharp
using var activity = _activitySource.StartActivity("export.workitems");
activity?.SetTag("module", "WorkItems");
using (DataClassificationScope.Begin(DataClassification.Customer))
{
    activity?.SetTag("project", projectName);
}
```

Tags that contain customer-identifiable data must NOT be exported to Application Insights. Work item IDs (integers) are not customer data. Metric dimensions exported to external telemetry backends must be low-cardinality and non-customer; if you need correlation beyond job/module scope, export only safe surrogate identifiers.

## Business Metrics (O-2)

Call `IMigrationMetrics` at the right points:

```csharp
_metrics.RecordAttempt("WorkItems");
try
{
    // work
    _metrics.RecordCompletion("WorkItems", count);
    _metrics.RecordDuration("WorkItems", elapsed);
}
catch (Exception ex)
{
    _metrics.RecordError("WorkItems", ex);
    throw;
}
```

Every counter added to `MigrationCounters` DTO must have a corresponding row in `QueueCommand.BuildProgressRenderable`.

## Structured Logging (O-3)

```csharp
_logger.LogInformation("WorkItems export started. {Project}", DataClassification.Customer(project));
_logger.LogDebug("Processing work item {WorkItemId}", workItemId);    // ID is system data
_logger.LogWarning("Work item {WorkItemId} skipped: {Reason}", workItemId, reason);
_logger.LogError(ex, "Work item {WorkItemId} failed", workItemId);
```

Never log field values, project names, org URLs, or attachment paths without a `DataClassification.Customer` scope.

## Progress Events (O-4)

Inject `IProgressSink` as optional and emit events:

```csharp
_progressSink?.Emit(new ProgressEvent
{
    Stage = "WorkItems.Export",
    Cursor = currentItemKey,
    Metrics = null    // always null for .NET 10 agents; populated only by TFS agent
});
```

## Metric Naming

Use the convention: `Metrics.Migration.{ModuleName}` as the module key.

## CLI/TUI Display Implications

- The CLI progress display reads aggregate counters from `GET /jobs/{id}/telemetry` (polling).
- The TUI Metrics panel also polls the telemetry endpoint.
- Do NOT wire progress display to an in-process `IProgressSink` from CLI or TUI code.
- `ProgressEvent.Metrics` is only populated by the TFS subprocess (net481). .NET 10 agents always emit `null` for `Metrics`.

## Application Insights Restrictions

Application Insights must not receive customer-identifiable data. When exporting telemetry:

- Filter or redact spans tagged with `DataClassification.Customer` before forwarding.
- Counters and durations are safe to export.
- Work item IDs (integers) are safe.

## Data Classification Helpers

```csharp
// Mark a value as customer data — must not appear in AI exports
DataClassification.Customer(value)

// Mark as system data — safe everywhere
DataClassification.System(value)
```

## Further Reading

- [.agents/context/telemetry-model.md](../.agents/context/telemetry-model.md) — telemetry model overview
- [.agents/guardrails/architecture-boundaries.md](../.agents/guardrails/architecture-boundaries.md) — data residency rules
- [observability.md](observability.md) — observability for operators