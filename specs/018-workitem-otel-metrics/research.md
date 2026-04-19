# Research: Work Item OpenTelemetry Metrics

**Feature Branch**: `018-workitem-otel-metrics`
**Date**: 2026-04-19

## Research Tasks Addressed

1. Where should the consolidated metrics recording class live? (from Technical Context)
2. How to handle UpDownCounter and ObservableGauge for in-flight/queue metrics?
3. How does the SnapshotMetricExporter handle the expanded instrument set?
4. Best practices for OTel dimension tags in .NET
5. How to gate correctness metrics to Tier 3 post-flight validation only?
6. Backward compatibility strategy for renamed metrics
7. Net481 compatibility constraints for multi-targeted Abstractions

---

## 1. Metrics Class Placement

### Decision
Interface `IMigrationMetrics` in `DevOpsMigrationPlatform.Abstractions/Telemetry/`. Concrete implementation `MigrationMetrics` in `DevOpsMigrationPlatform.Infrastructure/Telemetry/`.

### Rationale
- Follows the established pattern: `IWorkItemExportMetrics` (Abstractions) → `WorkItemExportMetrics` (Infrastructure.TfsObjectModel).
- `Abstractions` is multi-targeted (`net481;net10.0`), so both .NET 10 hosts and the .NET 4.8 TFS subprocess can reference the interface.
- The concrete `MigrationMetrics` class uses `System.Diagnostics.Metrics.Meter` which is available in both runtimes (via the `System.Diagnostics.DiagnosticSource` NuGet package for net481).
- FR-036 explicitly requires the class to live in Abstractions or Infrastructure. Infrastructure is preferred because it keeps Abstractions free of OTel SDK dependencies beyond `System.Diagnostics.Metrics`.

### Alternatives Considered
- **Put everything in Abstractions**: Rejected — would add OTel SDK dependency weight to the multi-targeted project. The interface + constants in Abstractions is sufficient for the contract.
- **Keep separate metrics classes per connector**: Rejected — the spec explicitly consolidates to a single meter (FR-035). The TFS connector will delegate to the shared `IMigrationMetrics` interface.

---

## 2. UpDownCounter and ObservableGauge Instrument Types

### Decision
- `migration.workitems.in_flight`: Use `UpDownCounter<int>`. Increment when a work item starts processing; decrement on completion or failure.
- `migration.queue.workitems.depth`: Use `ObservableGauge<int>` backed by a callback that reads the current queue length from the injected queue state provider.

### Rationale
- **UpDownCounter** is the OTel-standard instrument for "currently active" counts. It is synchronous, lock-free, and already supported by OTel 1.14.0. The SnapshotMetricExporter can read it via `GetSumLong()` on MetricPoints.
- **ObservableGauge** with a callback is the OTel-standard for "snapshot of current state" values. The callback reads from the job engine's internal queue without coupling the metrics class to the queue implementation. A `Func<int>` factory is injected at registration time.

### Alternatives Considered
- **UpDownCounter for queue depth**: Rejected — queue depth is an instantaneous snapshot, not a cumulative count. UpDownCounter semantics (add/subtract) would require tracking every enqueue/dequeue, which is more error-prone than a direct observation.
- **Gauge with polling timer**: Rejected — ObservableGauge already handles periodic observation via the OTel SDK's collection cycle. A separate timer would duplicate the mechanism.

### SnapshotMetricExporter Handling
- UpDownCounter values are read via `GetSumLong()` (same as Counter) — the SDK tracks the running sum.
- ObservableGauge values are read via `GetSumLong()` for integer gauges — the SDK invokes the callback on each collection cycle and reports the latest value.

---

## 3. SnapshotMetricExporter Expansion Strategy

### Decision
Extend the existing `switch (metric.Name)` block in `SnapshotMetricExporter.Export()` to handle all new `WellKnownMetricNames` constants. Add corresponding local variables and map them to the expanded `MetricSnapshot` properties.

### Rationale
- The current pattern is a flat switch/case matching metric names to local accumulators. This is straightforward and performant.
- With 28+ instruments, the switch block grows but remains O(n) per batch — each metric in the batch is visited exactly once.
- The exporter runs on a 5-second periodic reader, so even a larger switch has negligible overhead.

### Alternatives Considered
- **Dictionary-based dispatch**: Rejected — the switch/case is more explicit and easier to verify at review time. Dictionary overhead is unnecessary for ~30 cases.
- **Reflection-based mapping**: Rejected — too fragile and violates the "no magic" principle. Explicit mapping is safer.
- **Separate exporter per metric category**: Rejected — would require multiple PeriodicExportingMetricReaders sharing the same MeterProvider, adding complexity without benefit.

### Implementation Notes
- New counter instruments → `ReadCounterSum()` (existing helper)
- New histogram instruments → `ReadHistogramMean()` (existing helper)
- UpDownCounter instruments → `ReadCounterSum()` (SDK uses the same aggregation)
- ObservableGauge instruments → new `ReadGaugeLatest()` helper (reads last reported value)

---

## 4. OTel Dimension Tags Best Practices

### Decision
Create a shared `MigrationTagList` helper in `DevOpsMigrationPlatform.Abstractions/Telemetry/` that builds a `TagList` with the three mandatory tags (`job.id`, `operation`, `module`) plus optional `source.type`. All recording methods on `IMigrationMetrics` accept a pre-built `TagList` parameter.

### Rationale
- OTel .NET SDK `TagList` is a `struct` — zero allocation when ≤8 tags (uses inline storage).
- Centralising tag construction ensures every instrument carries the mandatory tags (FR-004).
- Pre-building the `TagList` once per operation context (e.g., at the start of an export module run) avoids repeated allocation.

### Tag Cardinality Analysis

| Tag | Cardinality | Risk |
|-----|-------------|------|
| `job.id` | 1 per run | None |
| `operation` | 3 values (`export`, `import`, `validation`) | None |
| `module` | ~6 values (`WorkItems`, `Identities`, `Teams`, etc.) | None |
| `source.type` | 3 values (`AzureDevOps`, `Tfs`, `Simulated`) | None |

Total unique combinations: ≤54 per metric. Well within OTel's cardinality safety zone.

### Forbidden Tags (FR-006)
- Work item IDs → use traces (span attributes) or structured logs instead
- User names/emails → use traces or logs
- Revision paths → use traces or logs

---

## 5. Correctness Metrics Tier 3 Gating

### Decision
Correctness metric recording methods (`RecordRevisionSourceCount`, `RecordRevisionTargetCount`, `RecordRevisionDelta`, `RecordBrokenLink`, `RecordMissingWorkItem`, etc.) are called only from within the Tier 3 post-flight validation pass in the orchestrator.

### Rationale
- FR-024 requires correctness metrics to be emitted only during Tier 3 post-flight validation.
- The Tier 3 validation pass already iterates a sample of work items (controlled by `sampleRate`).
- At `sampleRate=0`, no items are sampled → no correctness metric recordings occur.
- At `sampleRate=1.0`, every item is sampled → full correctness metrics.
- No conditional logic is needed inside the metrics class itself — the gating is architectural (only the validation code calls these methods).

### Alternatives Considered
- **Flag on IMigrationMetrics to enable/disable correctness metrics**: Rejected — adds mutable state to the metrics interface. Architectural gating is cleaner.
- **Separate ICorrectnessMetrics interface**: Rejected — FR-035 consolidates all work item metrics under one meter. A separate interface for the same meter adds unnecessary indirection.

---

## 6. Backward Compatibility for Renamed Metrics

### Decision
No backward-compatibility upgrader is required (FR-002 explicitly states this). All existing metric names are replaced in a single atomic change.

### Rationale
- The system is pre-production — no historical dashboards reference the old metric names.
- The old names (`work_item_exported_total`, etc.) are only consumed by `SnapshotMetricExporter` and potentially by any connected OTLP/Azure Monitor collector.
- Both `WellKnownMetricNames` constants and the `WorkItemExportMetrics`/`AttachmentDownloadMetrics` implementations are updated in the same commit.
- The `SnapshotMetricExporter` is updated to match the new names in the same change.

### Migration Path
1. Update `WellKnownMetricNames` constants → new dot-separated names
2. Update `WellKnownMeterNames` → add `Migration`, deprecate old names
3. Update all emitting classes to use new names
4. Update `SnapshotMetricExporter` to match new names
5. Update `MetricSnapshot` to carry new properties
6. Update tests to verify new names

---

## 7. Net481 Compatibility Constraints

### Decision
`IMigrationMetrics` interface uses only types available in both `net481` and `net10.0`: `System.TimeSpan`, `System.Int32`, `System.Int64`, `System.String`, and the `TagList` struct from `System.Diagnostics.DiagnosticSource`.

### Rationale
- `System.Diagnostics.Metrics` (Counter, Histogram, etc.) is available in net481 via the `System.Diagnostics.DiagnosticSource` NuGet package (already in `Directory.Packages.props`).
- The `TagList` struct is part of `System.Diagnostics.DiagnosticSource` and works on both runtimes.
- The `Meter` class is also available on both runtimes via the same package.

### What Must Stay in net10.0 Only
- `SnapshotMetricExporter` (references `OpenTelemetry.BaseExporter<Metric>`) — already guarded by `#if !NETFRAMEWORK`
- `PeriodicExportingMetricReader` — same guard
- `InMemoryMetricSnapshotStore` — same guard
- `TelemetryServiceExtensions` — same guard

### What Can Be Multi-Targeted
- `WellKnownMetricNames` (static string constants) — already in Abstractions
- `WellKnownMeterNames` (static string constants) — already in Abstractions
- `IMigrationMetrics` (interface) — new, in Abstractions
- `MigrationMetrics` (concrete class using `System.Diagnostics.Metrics.Meter`) — can live in Infrastructure (multi-targeted) or in a `#if !NETFRAMEWORK` block if ObservableGauge callback registration is net10.0-only

**Refinement**: The `ObservableGauge` constructor that takes a `Func<T>` callback is available in `System.Diagnostics.DiagnosticSource` for both runtimes. Therefore `MigrationMetrics` can be fully multi-targeted in `DevOpsMigrationPlatform.Infrastructure`. However, if the queue depth callback requires access to net10.0-only types, that specific registration can be conditional.
