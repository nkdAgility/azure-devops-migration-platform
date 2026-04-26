# Telemetry Architecture — Agent Context

This file defines the layered telemetry architecture, the mandatory observability contract, the cross-runtime strategy, and the step-by-step process for adding new signals. It applies to both AI agents and human contributors.

> **Canonical sources of truth — read these files, do not duplicate their content here:**
> - Meter names: `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMeterNames.cs`
> - Migration metric names: `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMetricNames.cs`
> - Discovery metric names: `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownDiscoveryMetricNames.cs`
> - Tag/attribute names: `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownTagNames.cs`
> - ActivitySource names: `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownActivitySourceNames.cs`
>
> Any list of names that appears outside those files is a secondary reference that **will go stale**. Always read the source files directly.

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

Counter and snapshot records live in `Abstractions/ControlPlaneApi/`. Read the source files directly for current properties — the list below is for orientation only:

`JobScopeCounters`, `WorkItemCounters`, `AttachmentCounters`, `MigrationCounters`, `MigrationDiagnostics`, `InventoryCounters`, `DependencyCounters`, `DiscoveryCounters`, `JobMetrics`, `JobSnapshot`, `OrgSnapshot`, `ProjectSnapshot`, `JobBootstrap`.

---

## File Placement Rules

| File type | Project | Guard? | Example |
|---|---|---|---|
| Metric interface (`I*Metrics`) | `Abstractions.Agent/Telemetry/` | No | `IMigrationMetrics.cs`, `IDiscoveryMetrics.cs` |
| Metric name constants | `Abstractions/Telemetry/` | No | `WellKnownMetricNames.cs`, `WellKnownDiscoveryMetricNames.cs` |
| Meter name constants | `Abstractions/Telemetry/` | No | `WellKnownMeterNames.cs` |
| Tag builder helpers | `Abstractions/Telemetry/` | No | `MigrationTagList.cs` |
| Counter record types | `Abstractions/ControlPlaneApi/` | No | `WorkItemCounters.cs`, `MigrationCounters.cs`, `DiscoveryCounters.cs`, `JobMetrics.cs` |
| Snapshot record types | `Abstractions/ControlPlaneApi/` | No | `JobSnapshot.cs`, `OrgSnapshot.cs`, `ProjectSnapshot.cs`, `JobBootstrap.cs` |
| Tag name constants | `Abstractions/Telemetry/` | No | `WellKnownTagNames.cs` |
| Store interfaces | `Abstractions/Telemetry/` | No | `IMetricSnapshotStore.cs` (contains `IJobMetricsStore`) |
| Concrete metrics class | `Infrastructure.Agent/Telemetry/` | No | `DiscoveryMetrics.cs`, `MigrationMetrics.cs` |
| OTel SDK exporter | `Infrastructure.ControlPlane/Metrics/` | `#if !NETFRAMEWORK` | `SnapshotMetricExporter.cs` |
| DI registration extensions | `Infrastructure.Agent/Telemetry/` | `#if !NETFRAMEWORK` | `TelemetryServiceExtensions.cs` |
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

## Distributed Tracing (ActivitySource)

Distributed tracing uses `System.Diagnostics.ActivitySource` to create spans that propagate across process boundaries. This enables end-to-end trace correlation from CLI → ControlPlane → MigrationAgent → module code.

> **Canonical source of truth — read this file, do not duplicate names here:**
> - ActivitySource names: `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownActivitySourceNames.cs`

### ActivitySource Names

Three ActivitySources cover the entire system. Each is registered in `ServiceDefaults/Extensions.cs` via `.AddSource()` in `.WithTracing()`:

| Name constant | Value | Scope |
|---|---|---|
| `WellKnownActivitySourceNames.Migration` | `DevOpsMigrationPlatform.Migration` | Export, import, and validation operations |
| `WellKnownActivitySourceNames.Discovery` | `DevOpsMigrationPlatform.Discovery` | Inventory and dependency discovery |
| `WellKnownActivitySourceNames.ControlPlane` | `DevOpsMigrationPlatform.ControlPlane` | Job lifecycle (enqueue, dequeue, state transitions) |

### Span Inventory

| Component | Span Name | Tags | Parent |
|---|---|---|---|
| `WorkItemExportOrchestrator` | `workitems.export` | job.id, operation, module, source.type | Root |
| `WorkItemExportOrchestrator` | `workitem.export` | workitem.id, wi.type | `workitems.export` |
| `WorkItemExportOrchestrator` | `attachment.download` | workitem.id, attachment.name | `workitem.export` |
| `WorkItemImportOrchestrator` | `workitems.import` | job.id, operation, module | Root |
| `WorkItemImportOrchestrator` | `workitem.import` | workitem.id | `workitems.import` |
| `RevisionFolderProcessor` | `revision.import` | workitem.id, revision.index | `workitem.import` |
| `FieldTransformTool` | `fieldtransform.apply` | job.id, wi.id, wi.type, operation, module, revision.index | Caller |
| `FieldTransformPipeline` | `fieldtransform.group` | wi.id, group.name | `fieldtransform.apply` |
| `FieldTransformValidator` | `fieldtransform.validate` | module, transform_count, is_valid, error_count | Caller |
| `InventoryDiscoveryModule` | `discovery.inventory` | job.id, module | Root |
| `DependencyDiscoveryModule` | `discovery.dependencies` | job.id, module | Root |
| `JobStore` | `job.enqueue` | job.id, job.type | Caller |
| `JobStore` | `job.dequeue` | job.id, job.type | Caller |
| `JobStore` | `job.setState` | job.id, job.state | Caller |

### Pattern for Adding Spans

Each component owns a `private static readonly ActivitySource` field:

```csharp
private static readonly ActivitySource s_activitySource =
    new(WellKnownActivitySourceNames.Migration);
```

Create spans with `StartActivity`:

```csharp
using var activity = s_activitySource.StartActivity("workitem.export");
activity?.SetTag("wi.id", workItemId);
```

Spans are hierarchical — a span started while a parent is active automatically becomes a child. No explicit parent passing is needed.

### Registration

All three sources are registered in `ServiceDefaults/Extensions.cs`:

```csharp
.WithTracing(tracing =>
{
    tracing.AddSource(WellKnownActivitySourceNames.Migration);
    tracing.AddSource(WellKnownActivitySourceNames.Discovery);
    tracing.AddSource(WellKnownActivitySourceNames.ControlPlane);
});
```

The TFS subprocess (net481) has its own `MigrationPlatformActivitySources` — separate from the .NET 10 sources.

---

## Structured Logging and DataClassification

All components use `ILogger<T>` for structured logging. Log levels follow standard conventions:

| Level | Usage |
|---|---|
| `Debug` | Per-item detail (revision fields/counts, dequeue events, state transitions) |
| `Information` | Boundary events (job enqueued, WI export start/complete, job completed) |
| `Warning` | Recoverable issues (comment fetch failures, attachment errors) |
| `Error` | Unrecoverable failures |

### DataClassification Scoping

Logs containing customer-identifiable content MUST be wrapped in a `DataClassification.Customer` scope. This ensures they are filtered from Azure Monitor but still streamed to CLI/TUI.

**Customer data** = field values, project names, org URLs, attachment paths.

**NOT customer data** = work item IDs (integer identifiers), counts, durations, job IDs, module names.

```csharp
// CORRECT — field values are customer data
using (DataClassificationScope.Begin(DataClassification.Customer))
    _logger.LogDebug("Field {Name} = {Value}", fieldName, fieldValue);

// CORRECT — no customer data, no scope needed
_logger.LogDebug("WI {WorkItemId}: fields={Count}, attachments={AttachmentCount}", wiId, count, attCount);
```

---

## Obsolete Interfaces — Do Not Use

Two legacy interfaces remain in `Abstractions.Agent/Telemetry/` marked `[Obsolete]`. They exist only to keep call sites in `Infrastructure.TfsObjectModel` compiling during the transition. **Do NOT inject or implement these in new code.**

| Interface | Replace with |
|---|---|
| `IWorkItemExportMetrics` | `IMigrationMetrics` |
| `IAttachmentDownloadMetrics` | `IMigrationMetrics` |

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

Tags MUST NOT include customer-identifiable data (project names, user emails, field values). Work item IDs are integer identifiers safe for tags. Use traces or `DataClassification.Customer`-scoped logs for customer content.

---

## Attribute (Tag) Conventions

> **Canonical source:** `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownTagNames.cs`

All **dimension tag** names MUST use constants from `WellKnownTagNames`. Hardcoded string literals for dimension tags are prohibited in new code.

### Dimension Tags vs. Span Result Attributes

| Category | Purpose | Centralised? | Example |
|---|---|---|---|
| **Dimension tags** | Filtering, grouping, correlation across spans | Yes — `WellKnownTagNames` | `job.id`, `operation`, `workitem.id` |
| **Span result attributes** | Contextual data local to a single span | No — inline strings co-located with the span | `"group_count"`, `"is_valid"`, `"workitems.count"` |

Dimension tags answer "which entity?" or "what kind?" and are reused across multiple components. Span result attributes answer "what happened inside this span?" and are unique to the span that sets them. Do not add result attributes to `WellKnownTagNames`.

### Naming Rules

- **Format:** lowercase, dot-separated segments: `<entity>.<property>` (e.g. `job.id`, `workitem.id`, `revision.index`)
- **Consistency:** One canonical name per concept. Do not create aliases (e.g. use `workitem.id` everywhere, never `wi.id` alongside it).
- **New tags:** Add the constant to `WellKnownTagNames.cs` first, then use the constant in code.

### Tag Cardinality Classification

Tags are classified by cardinality to determine where they may be used:

| Cardinality | Definition | Allowed on Metrics | Allowed on Traces | Allowed on Logs |
|---|---|---|---|---|
| **Low** | < 10 distinct values (e.g. `operation`, `module`) | ✅ | ✅ | ✅ |
| **Medium** | 10–1000 distinct values (e.g. `wi.type`, `group.name`) | ⚠️ Use with caution | ✅ | ✅ |
| **High** | Unbounded (e.g. `job.id`, `workitem.id`) | ❌ Never on metrics | ✅ | ✅ |

### Channel Separation

| Channel | Tags from | Purpose |
|---|---|---|
| **Metrics** | `MigrationTagList.Create()` only | Aggregate counters — low-cardinality dimensions only |
| **Traces** | `Activity.SetTag()` using `WellKnownTagNames` constants | Per-request detail — high-cardinality entity IDs allowed |
| **Logs** | Structured message templates `{Parameter}` | Per-event detail — customer data requires `DataClassification.Customer` scope |

Metrics tags and trace attributes serve different purposes and MUST NOT be mixed. `MigrationTagList` is the only approved factory for metric tags. Trace attributes are set directly on `Activity` via `SetTag()`.

---

## Mandatory Observability Contract

Every operation in the platform MUST meet the minimum observability requirements defined below. These standards apply to specifications (enforced by the `observability-contract` skill) and to implemented code.

### Operator Decision Model

Every telemetry signal (metric, span, log event) MUST map to at least one operator decision. Signals that do not support a decision are noise and MUST be rejected.

| Decision | Question it answers |
|---|---|
| **Is it working?** | Are requests succeeding at an acceptable rate? |
| **Is it fast enough?** | Is latency within SLO bounds? |
| **Is it overloaded?** | Is concurrency or queue depth exceeding capacity? |
| **What failed?** | Which specific operation failed and why? |
| **Where is it slow?** | Which dependency or step is the bottleneck? |
| **Is it correct?** | Do output counts match input counts? Are invariants maintained? |

Every operation MUST support at minimum: `Is it working?`, `Is it fast enough?`, `Is it overloaded?`, and `What failed?`.

### Mandatory Metrics Per Operation

Every operation MUST emit at least these five metric types:

| Metric | Instrument | Unit | Decision |
|---|---|---|---|
| Throughput | `Counter<long>` | `{operation}` | Is it working? |
| Latency | `Histogram<double>` | `ms` | Is it fast enough? |
| Outcome (success) | `Counter<long>` | `{operation}` | Is it working? |
| Outcome (failure) | `Counter<long>` | `{operation}` | What failed? |
| In-flight / queue depth | `UpDownCounter<long>` or `ObservableGauge<int>` | `{operation}` | Is it overloaded? |

If the operation processes batches, add a batch-size `Histogram` metric.

Metrics MUST represent business activity, not infrastructure (CPU, memory, GC are provided by the runtime).

### Metric Naming Convention

All metric names MUST use four dot-separated segments:

```
<domain>.<capability>.<operation>.<measure>
```

| Segment | Values | Example |
|---|---|---|
| `domain` | `migration`, `discovery` (matching `WellKnownMeterNames`) | `migration` |
| `capability` | Module or subsystem name | `export`, `fieldtransform` |
| `operation` | Specific action | `workitem`, `attachment`, `apply` |
| `measure` | What is measured | `count`, `duration_ms`, `errors`, `in_flight` |

Check `WellKnownMetricNames` before inventing new names — reuse where semantics match.

### Mandatory Trace Coverage

- Every operation MUST have exactly one root span.
- Every dependency (store call, SDK call, HTTP call) MUST have a child span.
- Context propagation method MUST be stated: automatic via `Activity` hierarchy or explicit `W3C TraceContext` headers.
- Root span tags MUST include: `job.id`, `operation`, `module` (using `WellKnownTagNames` constants).
- Child span tags MUST include the entity identifier (e.g. `workitem.id`, `revision.index`).
- Span names MUST use lowercase dot-separated segments matching the metric naming domain.

### Mandatory Structured Log Events Per Operation

Every operation MUST emit at least these log events:

| Event | Level | Required Fields |
|---|---|---|
| Operation started | `Information` | operationId, operation, input summary |
| Operation completed | `Information` | operationId, operation, outcome, durationMs, output summary |
| Operation failed | `Error` | operationId, operation, errorType, errorMessage, durationMs |
| Dependency call slow | `Warning` | operationId, dependency, durationMs, threshold |
| Retry attempt | `Warning` | operationId, operation, attempt, maxAttempts, delay |
| Step detail | `Debug` | operationId, step, detail |
| Wire-level detail | `Trace` | operationId, payload summary |

`Debug` and `Trace` levels MUST be disabled by default. No unstructured string concatenation in log templates. Every log event MUST include `operationId` for correlation.

### Mandatory Correlation Model

All telemetry (metrics, traces, logs) for a given operation MUST be correlated via these fields:

| Field | Source | Scope |
|---|---|---|
| `operationId` / `traceId` | `Activity.Current.TraceId` or generated GUID | All telemetry |
| `parentId` | `Activity.Current.ParentSpanId` | Spans and logs within a parent context |
| `job.id` | Job context | All telemetry within a job |
| Domain identifiers | Feature-specific (e.g. `workitem.id`, `project.name`) | Where applicable |

If correlation cannot be established for an operation, the observability contract is not met.

### Validation Query Categories

Observability is considered complete only when all five query categories can be answered using the defined signals:

| Category | Proves | Signal source |
|---|---|---|
| Failure identification | Failures can be identified by operation and cause | Metrics (outcome.failure) + Logs (Error) |
| Latency analysis | P50/P95/P99 latency can be computed per operation | Metrics (latency histogram) |
| Load observation | In-flight concurrency or queue depth is visible | Metrics (in_flight / queue_depth) |
| End-to-end trace | A single request can be traced from entry to all dependencies | Traces (root + child spans) |
| Error diagnosis | Root cause can be determined from logs + traces | Logs (Error) joined with Traces |

If any category cannot be expressed using the available signals, the observability contract is incomplete.
