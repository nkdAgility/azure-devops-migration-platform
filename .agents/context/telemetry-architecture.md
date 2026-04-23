# Telemetry Architecture — Agent Context

This file explains the layered telemetry architecture, the cross-runtime strategy, and the step-by-step process for adding new metrics. It applies to both AI agents and human contributors.

---

## Three-Layer Model

Telemetry is split into three layers. Each layer has different cross-runtime requirements:

| Layer | Purpose | Projects | net481 | net10.0 | `#if !NETFRAMEWORK` |
|---|---|---|---|---|---|
| **Recording** | Interfaces, constants, tag builders | `Abstractions`, `Infrastructure` | ✅ | ✅ | **No** — compiles for both |
| **Instrument** | Concrete `Meter`/`Counter`/`Histogram` classes | `Infrastructure` | ✅ | ✅ | **No** — compiles for both |
| **Pipeline** | OTel SDK exporters, readers, DI registration | `Infrastructure`, `ServiceDefaults`, host projects | ❌ or host-specific | ✅ | **Yes** — SDK types differ per host |

### Why three layers?

- **Recording layer** (`IMigrationMetrics`, `IDiscoveryMetrics`, `WellKnownMetricNames`, `MigrationTagList`) uses only `System.Diagnostics.DiagnosticSource` types (`TagList`, `Meter`, etc.) which are available on both net481 and net10.0. These types MUST NOT have `#if !NETFRAMEWORK` guards.

- **Instrument layer** (`MigrationMetrics`, `DiscoveryMetrics`) creates concrete OTel instruments (`Counter<T>`, `Histogram<T>`, `UpDownCounter<T>`) using `System.Diagnostics.Metrics.Meter`. These are also available on both runtimes via the `System.Diagnostics.DiagnosticSource` NuGet package. These MUST NOT have `#if !NETFRAMEWORK` guards.

- **Pipeline layer** (`TelemetryServiceExtensions`, `SnapshotMetricExporter`, `InMemoryJobMetricsStore`, `ControlPlaneTelemetryClient`) references OTel SDK types like `BaseExporter<Metric>`, `PeriodicExportingMetricReader`, and `MeterProviderBuilder`. These types have different registration patterns per host and may use net10.0-only APIs. These files MUST retain `#if !NETFRAMEWORK` guards.

### Exception: TFS host pipeline

`MigrationPlatformHost.cs` (in `Infrastructure.TfsObjectModel`, net481-only) registers the OTel pipeline directly using `services.AddOpenTelemetry().WithMetrics(...)`. This works because `Infrastructure.TfsObjectModel` directly references the `OpenTelemetry` NuGet packages for net481. It does NOT use `TelemetryServiceExtensions` (which is guarded for net10.0 hosts that use `ServiceDefaults`).

---

## Three-Channel Telemetry Model

Telemetry flows through three distinct channels, each optimised for a different consumer pattern:

| Channel | Type | Purpose | Frequency | Transport |
|---------|------|---------|-----------|-----------|
| 1 — Events | `ProgressEvent` (slim) | Real-time state-change notifications | Every state change | SSE fan-out |
| 2 — Metrics | `JobMetrics` | Aggregate counters for dashboards | Every few seconds | HTTP POST + polling |
| 3 — Snapshot | `JobSnapshot` | Per-org/project state for late-join | Every 5 min or project boundary | HTTP POST + polling |

### Channel 1: ProgressEvent (JobEvent)

A pure envelope — carries no counter fields. Maps to an OTel Event: something happened at a point in time.

```
ProgressEvent { Module, Stage, Message, Timestamp, EventSequence, LastCheckpointAt, NextCheckpointDueAt, Metrics? }
```

- `EventSequence` is a monotonic long assigned by the agent, scoped per job. Used as SSE `id:` for `Last-Event-ID` reconnect.
- `Metrics` is only populated by the TFS subprocess (net481) every N revisions. Null when emitted by the .NET 10 Migration Agent (which pushes metrics via Channel 2).

### Channel 2: JobMetrics

Aggregate counters, pushed by the agent on a fast timer (configurable, default 5s):

```
JobMetrics {
    Timestamp, Scope: JobScopeCounters,
    Migration?: MigrationCounters { WorkItems, Diagnostics? },
    Discovery?: DiscoveryCounters { Inventory?, Dependencies? }
}
```

**Cardinality guardrail:** `JobMetrics` is aggregate-only — no per-entity, per-project, or per-work-item dimensions. All high-cardinality breakdowns live exclusively in `JobSnapshot`.

### Channel 3: JobSnapshot

Per-org/project state, pushed on a slow timer (5 min) or at project boundaries:

```
JobSnapshot { Timestamp, Organisations: OrgSnapshot[] }
OrgSnapshot { Url, Name, Projects: ProjectSnapshot[] }
ProjectSnapshot { Name, Status, Migration?, Discovery? }
```

### Unified Bootstrap Endpoint

`GET /jobs/{id}/bootstrap` returns `JobBootstrap { Snapshot?, Metrics?, LastEventSequence }` — an atomic payload for late-joining clients to catch up without race conditions.

### Counter Record Types

| Record | Scope | Properties |
|--------|-------|------------|
| `JobScopeCounters` | All job types | OrganisationsTotal/Completed/Failed, ProjectsTotal/Completed/Failed, WorkItemsTotal |
| `WorkItemCounters` | Migration | Attempted, Completed, Failed, Skipped, RevisionsProcessed, Attachments? |
| `AttachmentCounters` | Migration | Processed, Failed, TotalBytes |
| `MigrationDiagnostics` | Aggregate only | OTel-derived means, correctness counters, in-flight gauges |
| `InventoryCounters` | Discovery | RevisionsTotal, RepositoriesTotal, CheckpointsSaved |
| `DependencyCounters` | Discovery | WorkItemsAnalysed, ExternalLinksFound, CrossProjectLinks, CrossOrgLinks |

---

## Package Dependencies by Layer

| Package | Used by | net481 | net10.0 |
|---|---|---|---|
| `System.Diagnostics.DiagnosticSource` | Recording + Instrument layers | ✅ (NuGet) | ✅ (inbox) |
| `OpenTelemetry` | Pipeline layer | ✅ (only in `Infrastructure.TfsObjectModel`) | ✅ |
| `OpenTelemetry.Extensions.Hosting` | Pipeline layer | ✅ (only in `Infrastructure.TfsObjectModel`) | ✅ |
| `OpenTelemetry.Exporter.OpenTelemetryProtocol` | Pipeline layer | ✅ (only in `Infrastructure.TfsObjectModel`) | ✅ |
| `Azure.Monitor.OpenTelemetry.Exporter` | Pipeline layer | ✅ (only in `Infrastructure.TfsObjectModel`) | ✅ |

---

## File Placement Rules

| File type | Project | Guard? | Example |
|---|---|---|---|
| Metric interface (`I*Metrics`) | `Abstractions/Telemetry/` | No | `IDiscoveryMetrics.cs` |
| Metric name constants | `Abstractions/Telemetry/` | No | `WellKnownMetricNames.cs` |
| Meter name constants | `Abstractions/Telemetry/` | No | `WellKnownMeterNames.cs` |
| Tag builder helpers | `Abstractions/Telemetry/` | No | `MigrationTagList.cs` |
| Counter record types | `Abstractions/Models/` | No | `WorkItemCounters.cs`, `JobMetrics.cs` |
| Snapshot record types | `Abstractions/Models/` | No | `JobSnapshot.cs`, `OrgSnapshot.cs` |
| Store interfaces | `Abstractions/Telemetry/` | No | `IJobMetricsStore.cs` |
| Concrete metrics class | `Infrastructure/Telemetry/` | No | `DiscoveryMetrics.cs`, `MigrationMetrics.cs` |
| OTel SDK exporter | `Infrastructure/Telemetry/` | `#if !NETFRAMEWORK` | `SnapshotMetricExporter.cs` |
| DI registration extensions | `Infrastructure/Telemetry/` | `#if !NETFRAMEWORK` | `TelemetryServiceExtensions.cs` |
| TFS host composition root | `Infrastructure.TfsObjectModel/` | No (net481-only project) | `MigrationPlatformHost.cs` |

---

## How to Add a New Metric

### Step 1 — Define the constant

Add the instrument name to `WellKnownMetricNames.cs` (or `WellKnownDiscoveryMetricNames.cs` for discovery):

```csharp
// In Abstractions/Telemetry/WellKnownMetricNames.cs
public const string MyNewCounter = "migration.category.my_new_counter";
```

### Step 2 — Add to the recording interface

Add the recording method to the appropriate interface (`IMigrationMetrics` or `IDiscoveryMetrics`):

```csharp
// In Abstractions/Telemetry/IMigrationMetrics.cs
void RecordMyNewEvent(in TagList tags);
```

### Step 3 — Implement in the concrete class

Add the instrument field and implement the method in the concrete class (`MigrationMetrics` or `DiscoveryMetrics`):

```csharp
// In Infrastructure/Telemetry/MigrationMetrics.cs
private readonly Counter<long> _myNewCounter;

// In constructor:
_myNewCounter = _meter.CreateCounter<long>(WellKnownMetricNames.MyNewCounter, unit: "{event}");

// Method:
public void RecordMyNewEvent(in TagList tags) => _myNewCounter.Add(1, tags);
```

### Step 4 — Call from module code

Record the metric from the module, passing a pre-built `TagList`:

```csharp
var tags = MigrationTagList.Create(job.JobId, "export", Name);
_metrics?.RecordMyNewEvent(tags);
```

The `?.` null-conditional call handles the case where metrics are not registered (e.g., in test harnesses).

### Step 5 — Register the meter in each host

The meter is already registered in the OTel pipeline for each host:

- **.NET 10 hosts** (MigrationAgent): `MigrationAgentServiceExtensions.cs` calls `.AddMeter(WellKnownMeterNames.Migration)`.
- **TFS host** (net481): `MigrationPlatformHost.cs` calls `.AddMeter(WellKnownMeterNames.Migration)` (or `.Discovery` for discovery metrics).
- **New meter?** If you create a new meter (not `Migration` or `Discovery`), you MUST add `.AddMeter(WellKnownMeterNames.YourNewMeter)` in BOTH host registration sites.

### Step 6 — Update JobMetrics (if applicable)

If the new metric should appear in the `JobMetrics` DTO (for Control Plane polling and TUI display):

1. Add a property to the appropriate counter record in Abstractions (`WorkItemCounters`, `MigrationDiagnostics`, `DiscoveryCounters`, etc.).
2. Add the extraction case to `SnapshotMetricExporter.cs` in Infrastructure, mapping the OTel metric into the nested `JobMetrics` structure.

### Step 7 — Add tests

Add a unit test to the existing test class (`MigrationMetricsTests` or `DiscoveryMetricsTests`) verifying the instrument is recorded with correct name and value.

---

## What NOT to do

- **Do NOT** add `#if !NETFRAMEWORK` to metric interfaces, constants, tag helpers, or concrete metric classes. These compile for both runtimes.
- **Do NOT** reference `OpenTelemetry.*` NuGet packages from `Abstractions` or multi-targeted `Infrastructure` (except for the net10.0 target). The recording and instrument layers use only `System.Diagnostics.DiagnosticSource`.
- **Do NOT** create a new `Meter` instance per metric recording call. Meters are created once in the constructor.
- **Do NOT** add high-cardinality tags (work item IDs, user emails, file paths) to metrics. Use traces or structured logs for those.
- **Do NOT** register the same meter name in multiple `AddMeter()` calls within the same host — it causes duplicate instrument registration.

---

## Export Paths by Host

| Host | Azure Monitor | OTLP | Snapshot Store | NDJSON stdout |
|---|---|---|---|---|
| MigrationAgent (net10.0) | Via `ServiceDefaults` | Via `OTEL_EXPORTER_OTLP_ENDPOINT` | `SnapshotMetricExporter` → `InMemoryJobMetricsStore` | No |
| ControlPlaneHost (net10.0) | Via `ServiceDefaults` | Via `OTEL_EXPORTER_OTLP_ENDPOINT` | No | No |
| TFS subprocess (net481) | Via `appsettings.json` `AzureMonitorConnectionString` | Via `OTEL_EXPORTER_OTLP_ENDPOINT` | No | `JobMetrics` in `ProgressEvent.Metrics` |

When no exporter is configured, OTel instruments are recorded but silently discarded — zero runtime overhead.

---

## Dimension Tag Rules

All metric recordings MUST include mandatory dimension tags built via `MigrationTagList.Create()` or a `TagList` with at minimum:

| Tag | Required | Source | Example |
|---|---|---|---|
| `job.id` | Yes | `MigrationJob.JobId` | `"a1b2c3d4"` |
| `operation` | Yes (migration metrics) | `"export"`, `"import"`, `"validation"` | `"export"` |
| `module` | Yes | Module `Name` property | `"WorkItems"` |
| `organisation.url` | Yes (discovery metrics) | Organisation URL | `"https://dev.azure.com/org"` |
| `source.type` | Optional | Source type enum | `"AzureDevOps"` |

Discovery metrics use `job.id` + `module` + `organisation.url` as their mandatory tags (no `operation` tag — discovery is not export/import/validation).

Tags MUST NOT include customer-identifiable data (work item IDs, project names, user emails). Use traces or `DataClassification.Customer`-scoped logs for those.
