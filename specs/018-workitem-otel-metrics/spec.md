# Feature Specification: Work Item OpenTelemetry Metrics

**Feature Branch**: `018-workitem-otel-metrics`
**Created**: 2026-04-18
**Status**: Draft
**Input**: User description: "Work item OpenTelemetry metrics for migration observability — execution, payload, correctness, idempotency, in-flight, and throughput metrics using Counter, Histogram, UpDownCounter, and ObservableGauge instruments"

## Architecture References

| Document | Status |
| --- | --- |
| `docs/architecture.md` | Confirmed accurate — telemetry described at Phase 1 item 14 (ServiceDefaults) and Phase 2 item 20 (CLI-level OTel) |
| `docs/validation.md` | Confirmed accurate — Tier 3 post-flight validation with `sampleRate` config covers correctness check placement |
| `docs/module-development-guide.md` | Confirmed accurate — `IModule` contract with `ExportAsync`, `ImportAsync`, `ValidateAsync` |
| `docs/migration-process-guide.md` | Confirmed accurate — Job Engine steps, progress event emission after each cursor write |
| `docs/agent-hosting.md` | Confirmed accurate — three progress sinks, heartbeat, stateless design |
| `.agents/20-guardrails/core/architecture-boundaries.md` | Confirmed accurate — `IArtefactStore`/`IStateStore` only, streaming, no in-memory sort |
| `.agents/20-guardrails/core/coding-standards.md` | Confirmed accurate — OTel packages already in `Directory.Packages.props` |
| `docs/configuration-reference.md` | Discrepancy logged — no telemetry naming convention documented |
| `.agents/30-context/domains/checkpointing-summary.md` | Confirmed accurate — cursor-based, forward-only |

## Reconciliation Snapshot (2026-05-16)

### Current status

- This spec is **partially implemented and partially superseded**.
- Newer telemetry architecture work in `specs/031-platform-metrics-unification` and `docs/adr/0011-unified-platform-metric-namespace.md` supersedes major contract assumptions in this spec.
- Task ledger is now reconciled in `tasks.md` with explicit per-task status markers and evidence.

### Remaining incomplete work (IDs)

`T010, T011, T012, T022, T023, T026, T027, T028, T030, T032, T033, T037, T047, T048, T049, T050, T052`

### Completed because superseded (IDs + source)

- Superseded by `specs/031-platform-metrics-unification/spec.md` + `docs/adr/0011-unified-platform-metric-namespace.md`:
  `T001, T002, T003, T004, T005, T006, T007, T008, T009, T013, T014, T016, T017, T018, T019, T021, T025, T038, T039, T040, T041, T042, T043, T044, T051`

### Contradictions and reconciliation

- `migration.*` and `DevOpsMigrationPlatform.Migration` in this spec conflict with current `platform.*` and `DevOpsMigrationPlatform.Agent` runtime constants.
- `IMigrationMetrics`/`MigrationMetrics` in this spec conflict with current seam `IPlatformMetrics`/`PlatformMetrics`.
- `MetricSnapshot` wording in this spec conflicts with current control-plane DTO surface (`JobMetrics` + `MigrationDiagnostics`).
- The `SnapshotMetricExporterTests` path used by this spec no longer exists; missing tests are now tracked as incomplete tasks.

### Verification evidence

- Code/constants/seam evidence:
  - `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownAgentMetricNames.cs`
  - `src/DevOpsMigrationPlatform.Abstractions/WellKnownMeterNames.cs`
  - `src/DevOpsMigrationPlatform.Abstractions.Agent/Telemetry/IPlatformMetrics.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/PlatformMetrics.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.ControlPlane/Metrics/SnapshotMetricExporter.cs`
- Wiring evidence:
  - `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/TelemetryServiceExtensions.cs`
  - `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentServiceExtensions.cs`
- Task gap evidence:
  - `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Telemetry/WorkItemExportMetrics.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Telemetry/AttachmentDownloadMetrics.cs`
  - Missing: `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/SnapshotMetricExporterTests.cs`

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Operator monitors migration throughput in real time (Priority: P1)

An operator running a work item migration needs to see how many items have been attempted, completed, failed, and retried — broken down by operation (export/import/validation) — so they can assess whether the migration is progressing normally or stalling.

**Why this priority**: Without execution metrics, operators have no visibility into migration health. This is the minimum viable observability.

**Independent Test**: Run an export of 50 work items with the Simulated source. Verify that `migration.workitems.attempted`, `migration.workitems.completed`, and `migration.workitem.duration.ms` are emitted with correct `operation=export` tags and that values match the expected counts.

**Acceptance Scenarios**:

1. **Given** a migration job in Export mode processing 50 work items, **When** the export completes, **Then** `migration.workitems.attempted` equals 50, `migration.workitems.completed` equals 50, and `migration.workitems.failed` equals 0, all tagged with `operation=export`.
2. **Given** a migration job where 3 work items fail with transient errors and are retried, **When** the export completes, **Then** `migration.workitems.retried` equals 3 and `migration.workitems.attempted` is greater than 50.
3. **Given** a running export, **When** an operator queries the OTel metrics endpoint or inspects the MetricSnapshot from the control plane, **Then** the `migration.workitem.duration.ms` histogram shows P50 and P95 values.

---

### User Story 2 — Operator identifies slow or complex work items (Priority: P2)

An operator needs to understand why certain work items take longer than others. Payload complexity metrics (revision count, attachment count, link count, field count) let them correlate complexity with duration and identify hotspots.

**Why this priority**: Duration alone shows that something is slow. Payload metrics explain why.

**Independent Test**: Export work items that vary in complexity (some with 1 revision, some with 20; some with attachments, some without). Verify that `migration.workitem.revisions.count`, `migration.workitem.attachments.count`, and `migration.workitem.links.count` histograms reflect the actual distributions.

**Acceptance Scenarios**:

1. **Given** a work item with 15 revisions, 3 attachments, and 8 links, **When** it is exported, **Then** the corresponding histograms record values 15, 3, and 8.
2. **Given** a batch of 100 work items, **When** the MetricSnapshot is inspected, **Then** the mean values for revision, attachment, and link counts are available for dashboard rendering.

---

### User Story 3 — Operator verifies migration correctness via count parity (Priority: P2)

After an import completes, an operator needs to know whether each migrated work item has the correct number of revisions, links, and attachments on the target — without field-by-field comparison.

**Why this priority**: Count parity is the cheapest correctness signal. It catches missing revisions, dropped links, and lost attachments without the cost of a full field-level diff.

**Independent Test**: Run a Both-mode migration with 20 work items using the Simulated source and target. During post-flight validation, verify that `migration.workitem.revisions.source.count` and `migration.workitem.revisions.target.count` histograms are populated and that `migration.workitems.revisions.missing` counter is 0.

**Acceptance Scenarios**:

1. **Given** a completed import where all items have matching revision counts, **When** Tier 3 post-flight validation runs, **Then** `migration.workitems.revisions.missing` is 0 and `migration.workitem.revisions.delta` histogram shows all zeros.
2. **Given** a completed import where 2 items have fewer target revisions than source, **When** post-flight validation runs at `sampleRate=1.0`, **Then** `migration.workitems.revisions.missing` is 2 and the delta histogram records the negative differences.
3. **Given** a completed import, **When** post-flight validation runs, **Then** `migration.workitems.broken_links` records items where target link count is less than source link count.

---

### User Story 4 — Operator monitors in-flight concurrency and queue pressure (Priority: P3)

An operator tuning parallelism settings needs to see how many work items are currently in flight and what the queue depth looks like, so they can adjust concurrency to avoid saturating the target API.

**Why this priority**: These are tuning metrics. Not essential for correctness but critical for performance optimisation in large migrations.

**Independent Test**: Run an export with concurrency set to 4. Verify that `migration.workitems.in_flight` gauge never exceeds 4 and that `migration.queue.workitems.depth` reflects the backlog.

**Acceptance Scenarios**:

1. **Given** a migration job with `maxConcurrency=4`, **When** processing is active, **Then** `migration.workitems.in_flight` UpDownCounter reports a value between 0 and 4.
2. **Given** a queue with 100 pending work items, **When** processing begins, **Then** `migration.queue.workitems.depth` starts at approximately 100 and decreases toward 0.

---

### User Story 5 — Idempotency and resume metrics signal correctness failures (Priority: P3)

After a crash-and-resume scenario, an operator needs signals indicating whether items were duplicated, changed unexpectedly, or lost. These metrics depend on a SourceId → TargetId mapping store that does not yet exist.

**Why this priority**: Critical for production trust but dependent on a mapping store that is a separate architectural component. Metric names are reserved now; implementation follows.

**Independent Test**: Deferred until the work item identity mapping store is implemented. Metric instrument definitions can be validated via unit tests confirming the instruments are registered and emittable.

**Acceptance Scenarios**:

1. **Given** the metric instruments are registered, **When** the application starts, **Then** `migration.workitems.duplicated_after_resume`, `migration.workitems.changed_on_rerun`, and `migration.workitems.missing_after_resume` counters exist and accept increments.

---

### Edge Cases

- What happens when a work item has zero revisions on the target after import? The `revisions.delta` histogram records a negative value equal to the source count, and `revisions.missing` counter increments.
- How does the system handle metrics when the Simulated source is used? All metrics emit identically regardless of source type. The `source.type` dimension tag distinguishes runs.
- What happens to in-flight gauge when an agent crashes? The gauge is per-process; a replacement agent starts with in-flight at 0. The control plane does not aggregate in-flight across agents.
- What if `sampleRate=0`? Tier 3 correctness metrics are not emitted — no items are sampled.

## Requirements *(mandatory)*

### Functional Requirements

#### Metric Naming and Conventions

- **FR-001**: All metric instrument names MUST use dot-separated namespacing with the `migration.` prefix (e.g., `migration.workitems.attempted`).
- **FR-002**: Existing underscore-separated metric names (`work_item_exported_total`, `revision_exported_total`, etc.) MUST be renamed to the new dot-separated convention. No backwards-compatibility upgrader is required.
- **FR-003**: All `WellKnownMetricNames` constants and `WellKnownMeterNames` constants MUST be updated to reflect the new naming convention.

#### Dimension Tags (Attributes)

- **FR-004**: Every instrument MUST carry these mandatory dimension tags: `job.id` (from `MigrationJob.JobId`), `operation` (`export` | `import` | `validation`), `module` (module name, e.g., `WorkItems`).
- **FR-005**: The optional dimension tag `source.type` (`AzureDevOps` | `Tfs` | `Simulated`) MAY be attached to instruments where the source connector is known.
- **FR-006**: High-cardinality values (raw work item IDs, user names) MUST NOT be attached as metric dimension tags. These belong in traces or logs.

#### Execution Metrics (Counters)

- **FR-007**: System MUST emit `migration.workitems.attempted` (Counter) — incremented every time the system starts processing a work item.
- **FR-008**: System MUST emit `migration.workitems.completed` (Counter) — incremented on successful processing.
- **FR-009**: System MUST emit `migration.workitems.failed` (Counter) — incremented on terminal failure.
- **FR-010**: System MUST emit `migration.workitems.retried` (Counter) — incremented on each retry attempt.

#### Execution Metrics (Histograms)

- **FR-011**: System MUST emit `migration.workitem.duration.ms` (Histogram) — recorded once per completed or failed work item attempt.

#### Payload and Complexity Metrics (Histograms)

- **FR-012**: System MUST emit `migration.workitem.fields.count` (Histogram) — field count per work item.
- **FR-013**: System MUST emit `migration.workitem.attachments.count` (Histogram) — attachment count per work item.
- **FR-014**: System MUST emit `migration.workitem.links.count` (Histogram) — link count per work item.
- **FR-015**: System MUST emit `migration.workitem.revisions.count` (Histogram) — revision count per work item.
- **FR-016**: System MUST emit `migration.workitem.payload.bytes` (Histogram) — serialised payload size per work item.

#### Correctness Metrics — Count Parity (emitted during Tier 3 post-flight validation)

- **FR-017**: System MUST emit `migration.workitem.revisions.source.count` (Histogram) — revision count from source/package per sampled work item.
- **FR-018**: System MUST emit `migration.workitem.revisions.target.count` (Histogram) — revision count from target per sampled work item.
- **FR-019**: System MUST emit `migration.workitem.revisions.delta` (Histogram) — `target count − source count` per sampled work item.
- **FR-020**: System MUST emit `migration.workitems.revisions.missing` (Counter) — incremented when a sampled work item has fewer target revisions than source.
- **FR-021**: System MUST emit `migration.workitems.revision_order_errors` (Counter) — incremented when target revision ordering violates chronological sequence.
- **FR-022**: System MUST emit `migration.workitems.broken_links` (Counter) — incremented when a sampled work item has fewer target links than source.
- **FR-023**: System MUST emit `migration.workitems.missing` (Counter) — incremented when a work item present in the package is absent from the target.
- **FR-024**: Correctness metrics MUST only be emitted during the Tier 3 post-flight validation pass and MUST respect the configured `sampleRate`.

#### Idempotency and Resume Metrics (defined, implementation deferred)

- **FR-025**: System MUST register `migration.workitems.duplicated` (Counter) — to be incremented when a second TargetId is observed for the same SourceId. **Deferred**: requires SourceId → TargetId mapping store.
- **FR-026**: System MUST register `migration.workitems.changed_on_rerun` (Counter) — to be incremented when a re-run modifies a previously completed target item. **Deferred**: requires mapping store.
- **FR-027**: System MUST register `migration.workitems.reprocessed_after_resume` (Counter) — to be incremented when a work item is processed again after a resume. **Deferred**: requires mapping store.
- **FR-028**: System MUST register `migration.workitems.duplicated_after_resume` (Counter) — to be incremented when resume creates a second target item. **Deferred**: requires mapping store.
- **FR-029**: System MUST register `migration.workitems.missing_after_resume` (Counter) — to be incremented when a mapped item is absent from the target after resume. **Deferred**: requires mapping store.

#### In-Flight State Metrics

- **FR-030**: System MUST emit `migration.workitems.in_flight` (UpDownCounter) — current number of work items being processed. Incremented on processing start, decremented on completion or failure.
- **FR-031**: System MUST emit `migration.queue.workitems.depth` (ObservableGauge) — current number of work items pending in the agent's internal queue.

#### MetricSnapshot Expansion

- **FR-032**: The `MetricSnapshot` record MUST be expanded with new properties corresponding to all new counters and histogram means defined in this spec. The record MUST remain a flat DTO.
- **FR-033**: New `MetricSnapshot` properties for deferred metrics MUST be nullable (`long?` / `double?`) so they serialise as `null` until the mapping store is available.
- **FR-034**: The `SnapshotMetricExporter` MUST be updated to extract new instrument values into the expanded `MetricSnapshot`.

#### Meter Organisation

- **FR-035**: All work item metrics MUST be registered under a single meter named `DevOpsMigrationPlatform.Migration` (replacing the current `WorkItemExport` and `AttachmentDownload` split). Attachment metrics move under this meter.
- **FR-036**: The `IMigrationMetrics` interface MUST be defined in `DevOpsMigrationPlatform.Abstractions`. The concrete `MigrationMetrics` implementation MUST live in `DevOpsMigrationPlatform.Infrastructure`, referencing `Abstractions` constants, so that both .NET 10 and .NET 4.8 projects can reference the interface.

### Key Entities

- **Metric Instrument**: An OpenTelemetry measurement instrument (Counter, Histogram, UpDownCounter, ObservableGauge) identified by a well-known name constant.
- **MetricSnapshot**: A flat record DTO capturing point-in-time aggregate values for all registered instruments, serialised for control plane and TUI consumption.
- **Dimension Tag**: A key-value pair attached to every metric measurement for filtering and grouping (e.g., `operation=export`).
- **Work Item Identity Map**: A future persistent store mapping SourceId → TargetId, required for idempotency metrics. Not implemented in this spec.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 19 immediately-implementable instruments (FR-007 through FR-023, FR-030, FR-031) emit correct values during a Simulated end-to-end migration run.
- **SC-002**: The 5 deferred instruments (FR-025 through FR-029) are registered at application startup and accept increments, verified by unit test.
- **SC-003**: MetricSnapshot round-trips through JSON serialisation with all new properties populated and is consumable by the TUI telemetry endpoint.
- **SC-004**: Existing telemetry pipelines (ControlPlaneTelemetryClient, SnapshotMetricExporter, InMemoryMetricSnapshotStore) continue to function with the expanded MetricSnapshot without breaking changes.
- **SC-005**: Correctness metrics are emitted only during Tier 3 post-flight validation and respect the `sampleRate` setting — at `sampleRate=0` no correctness metrics are emitted; at `sampleRate=1.0` all items are checked.
- **SC-006**: All metric names follow the `migration.` dot-separated convention with no remaining underscore-separated names in the codebase.
- **SC-007**: Every instrument measurement carries `job.id`, `operation`, and `module` dimension tags.

## Assumptions

- The existing OpenTelemetry packages in `Directory.Packages.props` (v1.14.0) support all required instrument types (Counter, Histogram, UpDownCounter, ObservableGauge). No new NuGet packages are needed.
- Renaming existing metric names is safe because the system is pre-production — no historical dashboards need migration.
- The Simulated source/target connector provides deterministic data sufficient for verifying all immediately-implementable metrics.
- The Tier 3 post-flight validation pass already runs in the Job Engine orchestrator for Both and Import modes — the correctness metrics hook into the existing `ValidateAsync` path.
- The work item identity mapping store (SourceId → TargetId) is a separate future spec. The deferred metric instruments will be wired to actual emission logic in that spec.
- `migration.workitem.payload.bytes` measures the serialised `revision.json` size, not the raw API response size.
- Queue depth (`migration.queue.workitems.depth`) reflects the agent's internal processing queue, not the control plane job queue.
- Architecture docs read: `docs/architecture.md`, `docs/validation.md`, `docs/module-development-guide.md`, `docs/migration-process-guide.md`, `docs/agent-hosting.md`, `.agents/20-guardrails/core/architecture-boundaries.md`, `.agents/20-guardrails/core/coding-standards.md`, `.agents/30-context/domains/checkpointing-summary.md`.

