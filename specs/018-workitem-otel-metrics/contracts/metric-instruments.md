# Metric Instruments Contract

**Feature Branch**: `018-workitem-otel-metrics`
**Date**: 2026-04-19

This document defines the public contract for OpenTelemetry metric instruments exposed by the migration platform. These instruments are the observable outputs consumed by dashboards, OTLP collectors, Azure Monitor, and the TUI via MetricSnapshot.

---

## Contract Stability

- Metric names defined here are the public contract. Renaming a metric name is a breaking change requiring a version increment.
- The `migration.` prefix and dot-separated convention are permanent.
- Dimension tag keys (`job.id`, `operation`, `module`, `source.type`) are permanent.
- New instruments may be added without a breaking version change.
- Removing an instrument requires deprecation (one version) then removal.

---

## Instrument Registry

### Execution Instruments

| Name | Type | Unit | When Emitted |
|---|---|---|---|
| `migration.workitems.attempted` | Counter | `{work_item}` | Start of work item processing |
| `migration.workitems.completed` | Counter | `{work_item}` | Successful processing completion |
| `migration.workitems.failed` | Counter | `{work_item}` | Terminal processing failure |
| `migration.workitems.retried` | Counter | `{work_item}` | Each retry attempt |
| `migration.workitem.duration.ms` | Histogram | `ms` | Per-item completion or failure |

### Payload Instruments

| Name | Type | Unit | When Emitted |
|---|---|---|---|
| `migration.workitem.fields.count` | Histogram | `{field}` | After work item processing |
| `migration.workitem.attachments.count` | Histogram | `{attachment}` | After work item processing |
| `migration.workitem.links.count` | Histogram | `{link}` | After work item processing |
| `migration.workitem.revisions.count` | Histogram | `{revision}` | After work item processing |
| `migration.workitem.payload.bytes` | Histogram | `By` | After work item processing |

### Correctness Instruments (Tier 3 only)

| Name | Type | Unit | When Emitted |
|---|---|---|---|
| `migration.workitem.revisions.source.count` | Histogram | `{revision}` | Post-flight validation sample |
| `migration.workitem.revisions.target.count` | Histogram | `{revision}` | Post-flight validation sample |
| `migration.workitem.revisions.delta` | Histogram | `{revision}` | Post-flight validation sample |
| `migration.workitems.revisions.missing` | Counter | `{work_item}` | Post-flight: target < source revisions |
| `migration.workitems.revision_order_errors` | Counter | `{work_item}` | Post-flight: non-chronological target |
| `migration.workitems.broken_links` | Counter | `{work_item}` | Post-flight: target < source links |
| `migration.workitems.missing` | Counter | `{work_item}` | Post-flight: item absent from target |

### In-Flight Instruments

| Name | Type | Unit | When Emitted |
|---|---|---|---|
| `migration.workitems.in_flight` | UpDownCounter | `{work_item}` | Increment on start, decrement on end |
| `migration.queue.workitems.depth` | ObservableGauge | `{work_item}` | Observed each collection cycle |

### Idempotency Instruments (Deferred)

| Name | Type | Unit | Status |
|---|---|---|---|
| `migration.workitems.duplicated` | Counter | `{work_item}` | Registered; not incremented |
| `migration.workitems.changed_on_rerun` | Counter | `{work_item}` | Registered; not incremented |
| `migration.workitems.reprocessed_after_resume` | Counter | `{work_item}` | Registered; not incremented |
| `migration.workitems.duplicated_after_resume` | Counter | `{work_item}` | Registered; not incremented |
| `migration.workitems.missing_after_resume` | Counter | `{work_item}` | Registered; not incremented |

---

## Mandatory Dimension Tags

Every measurement recorded on any instrument above MUST carry these tags:

| Tag Key | Type | Example | Source |
|---|---|---|---|
| `job.id` | string | `"a1b2c3d4-..."` | `MigrationJob.JobId` |
| `operation` | string | `"export"` | Module execution mode |
| `module` | string | `"WorkItems"` | `IModule.Name` |

Optional tag:

| Tag Key | Type | Example | Source |
|---|---|---|---|
| `source.type` | string | `"AzureDevOps"` | Source connector |

---

## MetricSnapshot DTO Contract

The `MetricSnapshot` record is the serialised payload for the control plane telemetry endpoint (`GET /jobs/{jobId}/telemetry`). It is a flat DTO with one property per instrument aggregate.

**Rules:**
- All counter properties are `long` (cumulative sum).
- All histogram properties are `double?` (weighted mean, null if no measurements).
- In-flight/gauge properties are `int` (latest observed value).
- Deferred metric properties are `long?` (null until mapping store available).
- `Timestamp` is `DateTimeOffset` (UTC).
- The record uses `init`-only setters and is immutable after construction.
- JSON serialisation MUST produce camelCase property names.

---

## Meter Registration

The `DevOpsMigrationPlatform.Migration` meter (version `2.0`) MUST be registered in the OTel MeterProvider. The two deprecated meters (`WorkItemExport`, `AttachmentDownload`) SHOULD be retained temporarily for any external collectors that have not updated their meter subscriptions.

```csharp
// In MigrationAgentServiceExtensions or TelemetryServiceExtensions:
.AddMeter(WellKnownMeterNames.Migration)
.AddMeter(WellKnownMeterNames.WorkItemExport)     // deprecated, retain for transition
.AddMeter(WellKnownMeterNames.AttachmentDownload)  // deprecated, retain for transition
```
