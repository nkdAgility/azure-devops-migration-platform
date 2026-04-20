# Quickstart: Work Item OpenTelemetry Metrics

**Feature Branch**: `018-workitem-otel-metrics`
**Date**: 2026-04-19

---

## What This Feature Does

Expands the platform's OpenTelemetry instrumentation from 13 export-centric metrics to 24 instruments covering the full migration lifecycle: execution tracking, payload complexity, count-parity correctness verification, in-flight concurrency monitoring, and reserved idempotency signals.

## Key Changes at a Glance

| Area | Before | After |
|---|---|---|
| Meter count | 2 (`WorkItemExport`, `AttachmentDownload`) | 1 (`DevOpsMigrationPlatform.Migration`) |
| Instrument count | 13 | 24 |
| Naming convention | Underscore (`work_item_exported_total`) | Dot-separated (`migration.workitems.attempted`) |
| Dimension tags | Ad-hoc (`TeamProjectCollectionId`, `WorkItemId`) | Standardised (`job.id`, `operation`, `module`) |
| MetricSnapshot properties | 11 | ~30 |
| Correctness metrics | None | 7 instruments (Tier 3 post-flight) |
| In-flight metrics | None | 2 instruments (UpDownCounter + ObservableGauge) |

## Implementation Order (Overview — see [tasks.md](tasks.md) for the canonical task sequence)

This section provides a high-level implementation order. The detailed, dependency-ordered task list with 52 tasks across 9 phases is in [tasks.md](tasks.md).

### Phase 1: Foundation (no behavioural change)
1. **Update `WellKnownMetricNames`** — rename all constants to dot-separated names
2. **Update `WellKnownMeterNames`** — add `Migration`, mark old names `[Obsolete]`
3. **Create `IMigrationMetrics` interface** in Abstractions
4. **Create `MigrationMetrics` implementation** in Infrastructure
5. **Create `MigrationTagList` helper** in Abstractions
6. **Expand `MetricSnapshot`** with all new properties

### Phase 2: Wiring (connect new metrics to existing code paths)
7. **Update `SnapshotMetricExporter`** to handle all new instrument names
8. **Update meter registration** in `MigrationAgentServiceExtensions` and `TelemetryServiceExtensions`
9. **Migrate `WorkItemExportMetrics`** to delegate to `IMigrationMetrics`
10. **Migrate `AttachmentDownloadMetrics`** to delegate to `IMigrationMetrics`

### Phase 3: New Metric Emission
11. **Add execution metrics** (attempted/completed/failed/retried) to work item processing paths
12. **Add payload metrics** (fields/attachments/links/revisions/bytes) after work item processing
13. **Add in-flight metrics** (in_flight UpDownCounter, queue depth gauge) to orchestrator
14. **Register deferred idempotency instruments** (no emission yet)

### Phase 4: Correctness (Tier 3)
15. **Add correctness metrics** to post-flight validation pass
16. **Wire sampleRate gating** to control correctness metric emission

### Phase 5: Tests & Documentation
17. **Unit tests** for `MigrationMetrics`, `SnapshotMetricExporter`, `MetricSnapshot` serialisation
18. **Update documentation** (configuration telemetry section, architecture references)

## How to Verify

```bash
# Run a Simulated export and check metric emission
dotnet run --project src/DevOpsMigrationPlatform.CLI.Migration -- export \
  --config scenarios/queue-export-ado-workitems-single-project.json

# Run tests
dotnet test

# Verify metric names in code
grep -r "migration\." src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMetricNames.cs
```

## Files to Touch

| File | Action | Reason |
|---|---|---|
| `Abstractions/Telemetry/WellKnownMetricNames.cs` | Modify | Rename constants |
| `Abstractions/Telemetry/WellKnownMeterNames.cs` | Modify | Add consolidated meter |
| `Abstractions/Telemetry/IMigrationMetrics.cs` | Create | Unified recording interface |
| `Abstractions/Telemetry/MigrationTagList.cs` | Create | Tag construction helper |
| `Abstractions/Models/MetricSnapshot.cs` | Modify | Add ~25 properties |
| `Infrastructure/Telemetry/MigrationMetrics.cs` | Create | Concrete implementation |
| `Infrastructure/Telemetry/SnapshotMetricExporter.cs` | Modify | Handle new instruments |
| `Infrastructure/Telemetry/TelemetryServiceExtensions.cs` | Modify | Register new services |
| `Infrastructure.TfsObjectModel/Telemetry/WorkItemExportMetrics.cs` | Modify | Delegate to new interface |
| `Infrastructure.TfsObjectModel/Telemetry/AttachmentDownloadMetrics.cs` | Modify | Delegate to new interface |
| `MigrationAgent/MigrationAgentServiceExtensions.cs` | Modify | Register new meter |

## Constitution Compliance Notes

- **No new NuGet packages** — all required OTel types are in existing packages
- **No package layout changes** — metrics are purely observational
- **No module isolation violations** — interface in Abstractions, impl in Infrastructure
- **No breaking MetricSnapshot change** — additive properties with backward-compatible obsolete markers
- **Correctness metrics gated by architecture** — only called from Tier 3 validation pass
