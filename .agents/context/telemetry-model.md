# Telemetry Architecture — Agent Context

## Hard Guardrail

- The **Three-Channel Telemetry Model is the only allowed architecture**.
- No new channels, DTOs, or flows may be introduced.
- Any proposed change MUST map to an existing section.
- If it cannot map, STOP and raise a guardrail challenge.

Canonical definitions live in:
- Abstractions/Telemetry/*
- Abstractions/ControlPlaneApi/*
Do not duplicate them.

---

## ⚠️ Critical: Two Metric Destinations

There are **two independent pipelines**:

1. Azure Monitor / App Insights  
   IPlatformMetrics → OTel → exporter → Azure  
   Used for cloud telemetry, traces, history

2. CLI / TUI display  
   ProgressEvent.Metrics → SSE → ControlPlane → JobMetricsStore → polling  
   Used for live progress display

### Rules

- OTel metrics **do not feed CLI/TUI**
- CLI/TUI metrics **must come from ProgressEvent.Metrics**
- .NET 10 agent does not populate JobMetrics via OTel exporter

### Required pattern (for CLI/TUI visibility)

Every module that exposes counters MUST emit:

```csharp
sink?.Emit(new ProgressEvent
{
    Module = ModuleName,
    Stage = "MyModule.Complete",
    Message = "...",
    Metrics = new JobMetrics
    {
        Migration = new MigrationCounters
        {
            MyCounters = new MyCounters { Exported = count }
        }
    }
});
```

### Consequence

* Calling only `_metrics.RecordXxx(...)` → visible in Azure only
* Emitting only ProgressEvent → visible in CLI/TUI only
* **Both are required for full observability**

---

## Architecture Overview

### Layers

1. Recording
   Interfaces, constants, tag builders
   net481 + net10.0
   No guards

2. Instrument
   Meter / Counter / Histogram implementations
   net481 + net10.0
   No guards

3. Pipeline
   OTel SDK, exporters, DI
   Host-specific
   May use `#if !NETFRAMEWORK`

---

## Three-Channel Model

1. Events (ProgressEvent)

   * State changes
   * SSE
   * Per event
   * May include Metrics (required for CLI/TUI)

2. Metrics (JobMetrics)

   * Aggregate counters
   * HTTP polling (~5s)
   * No high-cardinality data

3. Snapshot (JobSnapshot)

   * Per-org/project state
   * HTTP polling (~5 min)

Bootstrap:
GET /jobs/{id}/bootstrap → Snapshot + Metrics + LastEventSequence

---

## Data Flow

Agent:

* OTel metrics → Azure pipeline
* ProgressEvent → Control Plane

Control Plane:

* SSE buffer for events
* JobMetricsStore for merged counters

CLI / TUI:

* SSE → progress (events)
* Poll → counters (metrics)

---

## CLI/TUI Contract (Mandatory)

* MUST:

  * Use SSE for events
  * Use polling for metrics
* MUST NOT:

  * Depend on OTel metrics
  * Use in-process sinks

Failure mode:

* No ProgressEvent.Metrics → counters remain zero

---

## Metric Implementation Flow

1. Add constant (`WellKnownAgentMetricNames` or `WellKnownControlPlaneMetricNames`)
2. Add interface method to `IPlatformMetrics`
3. Implement instrument in `PlatformMetrics`
4. Record via TagList
5. Register meter (`WellKnownMeterNames.Agent`) in hosts
6. Map to JobMetrics if needed
7. Emit ProgressEvent.Metrics for CLI/TUI
8. Add tests

---

## Meter Name Reference

| Scope | Meter constant | Value |
|---|---|---|
| Agent (discovery + migration) | `WellKnownMeterNames.Agent` | `DevOpsMigrationPlatform.Agent` |
| Control Plane | `WellKnownMeterNames.ControlPlane` | `DevOpsMigrationPlatform.ControlPlane` |
| CLI | `WellKnownMeterNames.Cli` | `DevOpsMigrationPlatform.Cli` |

## Metric Name Reference

All agent metric strings follow `platform.<domain>.<phase>.<measure>`.

- Agent constants: `WellKnownAgentMetricNames`
- Control Plane constants: `WellKnownControlPlaneMetricNames`
- CLI constants: `WellKnownCliMetricNames`

Old prefixes `discovery.*`, `migration.*`, `controlplane.*`, `cli.*` are **removed**. Update any OTel dashboards / relabelling rules accordingly.

---

## Observability Contract

Each operation MUST provide:

Metrics:

* Throughput
* Latency
* Success
* Failure
* In-flight / queue

Traces:

* One root span
* Child spans for dependencies
* Standard tags

Logs:

* Start, Complete, Failure
* Slow dependency, Retry
* Debug detail

Progress:

* `ProgressEvent` emitted at start, per-item/batch (≤50), and completion via `IProgressSink.Emit` (O-4)
* Every `IWorkItemDiscoveryService` and `WorkItemFetchScope` call site passes a non-null `IProgress<int>` callback; the callback wraps the raw count in a `ProgressEvent` via `IProgressSink` (O-5)
* Infrastructure classes report bare `int` via `IProgress<T>`; the calling module/orchestrator converts to `ProgressEvent`



* traceId / operationId
* job.id

---

## Tag Rules

* Use WellKnownTagNames only
* No literals
* No high-cardinality metrics
* Metrics use MigrationTagList only
* High-cardinality allowed only in traces/logs

---

## Do Not

* Add new telemetry channels
* Add high-cardinality metric tags
* Duplicate constants
* Use OTel SDK outside pipeline layer
* Create meters per call
* Wire CLI to in-process sinks
* Assume OTel feeds CLI/TUI

---

## Definition of Done

1. Metric recorded via OTel
2. Metric mapped into JobMetrics (if required)
3. Metric emitted via ProgressEvent.Metrics
4. CLI shows updated counters
5. TUI shows updated counters
6. Traces + logs allow diagnosis
