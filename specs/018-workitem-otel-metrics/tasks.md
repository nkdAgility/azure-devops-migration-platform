# Tasks: Work Item OpenTelemetry Metrics

**Input**: Design documents from `/specs/018-workitem-otel-metrics/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/metric-instruments.md

**Tests**: Unit tests are included — the spec requires verifiable metric emission and MetricSnapshot serialisation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Rename metric constants, create the new consolidated meter, define the unified metrics interface, expand MetricSnapshot, and create the tag helper — all without changing runtime behaviour.

- [X] T001 Update `WellKnownMetricNames` constants from underscore-separated to dot-separated naming convention in `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMetricNames.cs` (FR-001, FR-002, FR-003)
- [X] T002 [P] Update `WellKnownMeterNames` — add `Migration` constant, mark `WorkItemExport` and `AttachmentDownload` as `[Obsolete]` in `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMeterNames.cs` (FR-035)
- [X] T003 [P] Create `MigrationTagList` helper in `src/DevOpsMigrationPlatform.Abstractions/Telemetry/MigrationTagList.cs` — static factory methods for building `TagList` with mandatory `job.id`, `operation`, `module` tags plus optional `source.type` (FR-004, FR-005)
- [X] T004 Create `IMigrationMetrics` interface in `src/DevOpsMigrationPlatform.Abstractions/Telemetry/IMigrationMetrics.cs` — unified recording contract for all 28 instruments per data-model.md section 4.1 (FR-007 through FR-031)
- [X] T005 Create `MigrationMetrics` concrete implementation in `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/MigrationMetrics.cs` — registers all instruments under the `DevOpsMigrationPlatform.Migration` meter (FR-035, FR-036)
- [X] T006 Expand `MetricSnapshot` record in `src/DevOpsMigrationPlatform.Abstractions/Models/MetricSnapshot.cs` — replace legacy properties with execution counters, payload means, correctness counters/means, in-flight gauges, and nullable deferred idempotency properties per data-model.md section 4.3; remove legacy properties outright (system is pre-production, no backward-compatibility transition needed) (FR-032, FR-033)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Wire the new metrics implementation into the existing telemetry pipeline so all subsequent user story work emits through the correct path.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T007 Update `SnapshotMetricExporter` in `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/SnapshotMetricExporter.cs` — extend `switch` block to handle all new `WellKnownMetricNames` constants, add `ReadGaugeLatest()` helper for ObservableGauge, map all new instruments to expanded `MetricSnapshot` properties (FR-034)
- [X] T008 Update `TelemetryServiceExtensions.AddTelemetryServices()` in `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/TelemetryServiceExtensions.cs` — register `IMigrationMetrics`/`MigrationMetrics` as singleton, add new meter to MeterProvider
- [X] T009 Update meter registration in `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentServiceExtensions.cs` — add `WellKnownMeterNames.Migration` to `.AddMeter()` calls, retain deprecated meter names for transition
- [X] T010 [P] Update `WorkItemExportMetrics` in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Telemetry/WorkItemExportMetrics.cs` — delegate to injected `IMigrationMetrics` using new metric names and standardised tags
- [X] T011 [P] Update `AttachmentDownloadMetrics` in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Telemetry/AttachmentDownloadMetrics.cs` — delegate to injected `IMigrationMetrics` using new metric names and standardised tags
- [X] T012 [P] Update existing `SnapshotMetricExporterTests` in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/SnapshotMetricExporterTests.cs` — fix assertions for renamed metric names, add test cases for new instrument types (UpDownCounter, ObservableGauge)
- [X] T013 [P] Create `MetricSnapshotSerializationTests` in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/MetricSnapshotSerializationTests.cs` — verify JSON round-trip with all new properties populated, null deferred properties serialize correctly, camelCase property names (SC-003)
- [X] T014 [P] Create `WellKnownMetricNamesTests` in `tests/DevOpsMigrationPlatform.Abstractions.Tests/Telemetry/WellKnownMetricNamesTests.cs` — validate all constants start with `migration.`, use dot-separated hierarchy separators, no underscores as hierarchy separators (underscores are permitted within leaf segments, e.g. `in_flight`) (SC-006)

**Checkpoint**: Foundation ready — all metric constants renamed, new interface wired, SnapshotMetricExporter handles expanded instrument set, existing tests pass with new names.

---

## Phase 3: User Story 1 — Operator monitors migration throughput in real time (Priority: P1) 🎯 MVP

**Goal**: Emit execution counters (attempted, completed, failed, retried) and duration histogram during work item export/import, observable via MetricSnapshot with standardised `job.id`/`operation`/`module` tags.

**Independent Test**: Run an export of 50 work items with the Simulated source. Verify that `migration.workitems.attempted`, `migration.workitems.completed`, and `migration.workitem.duration.ms` are emitted with correct `operation=export` tags and values match expected counts.

### Gherkin Feature File for User Story 1 (mandatory)

- [X] T015 [US1] Create `features/export/work-items/export-execution-metrics.feature` — translate spec.md US1 acceptance scenarios (50 work items → attempted/completed/failed counters, retry scenario, duration histogram P50/P95) into conformant Gherkin per `.agents/guardrails/acceptance-test-format.md`

### Implementation for User Story 1

- [X] T016 [US1] Create `MigrationMetricsTests` in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/MigrationMetricsTests.cs` — unit tests for `MigrationMetrics.RecordWorkItemAttempted`, `RecordWorkItemCompleted`, `RecordWorkItemFailed`, `RecordWorkItemRetried`, `RecordWorkItemDuration` verifying instruments emit with correct names and tags
- [X] T017 [US1] Wire execution metric recording into the work item export processing path in `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs` (`ExportAsync`) — call `IMigrationMetrics.RecordWorkItemAttempted` at processing start, `RecordWorkItemCompleted`/`RecordWorkItemFailed` at end, `RecordWorkItemRetried` on retry, `RecordWorkItemDuration` with elapsed milliseconds
- [X] T018 [US1] Wire execution metric recording into the work item import processing path in `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` (`ImportAsync`) — same calls as T017 but with `operation=import` tag
- [X] T019 [US1] Verify all execution metrics carry mandatory `job.id`, `operation`, `module` dimension tags (FR-004, SC-007) — add assertion in `MigrationMetricsTests` that tags are present

**Checkpoint**: User Story 1 fully functional — execution counters and duration histogram emit correctly for both export and import operations.

---

## Phase 4: User Story 2 — Operator identifies slow or complex work items (Priority: P2)

**Goal**: Emit payload complexity histograms (field count, attachment count, link count, revision count, payload bytes) after each work item is processed, observable via MetricSnapshot means.

**Independent Test**: Export work items varying in complexity. Verify that histograms for revisions, attachments, and links reflect actual distributions.

### Gherkin Feature File for User Story 2 (mandatory)

- [X] T020 [US2] Create `features/export/work-items/export-payload-metrics.feature` — translate spec.md US2 acceptance scenarios (15 revisions/3 attachments/8 links → histogram values, batch mean values in MetricSnapshot) into conformant Gherkin

### Implementation for User Story 2

- [X] T021 [P] [US2] Add unit tests for payload metrics in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/MigrationMetricsTests.cs` — test `RecordFieldCount`, `RecordAttachmentCount`, `RecordLinkCount`, `RecordRevisionCount`, `RecordPayloadBytes` with varying values
- [X] T022 [US2] Wire payload metric recording into work item processing paths in `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs` and `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` — after each work item is processed, call `IMigrationMetrics.RecordFieldCount`, `RecordAttachmentCount`, `RecordLinkCount`, `RecordRevisionCount`, `RecordPayloadBytes` with values extracted from the work item data
- [X] T023 [US2] Add `SnapshotMetricExporter` test cases for payload histogram means in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/SnapshotMetricExporterTests.cs` — verify `FieldCountMean`, `AttachmentCountMean`, `LinkCountMean`, `RevisionCountMean`, `PayloadBytesMean` populate correctly

**Checkpoint**: User Stories 1 AND 2 work independently — execution tracking plus payload complexity analysis.

---

## Phase 5: User Story 3 — Operator verifies migration correctness via count parity (Priority: P2)

**Goal**: Emit correctness counters and histograms during Tier 3 post-flight validation — revision source/target counts, delta, missing revisions, broken links, missing work items — gated by `sampleRate` configuration.

**Independent Test**: Run a Both-mode migration with 20 Simulated work items. During post-flight validation, verify revision source/target count histograms are populated and `revisions.missing` counter is 0.

### Gherkin Feature File for User Story 3 (mandatory)

- [X] T024 [US3] Create `features/platform/validation/post-flight-correctness-metrics.feature` — translate spec.md US3 acceptance scenarios (matching revision counts → missing=0 and delta=0; 2 items with fewer target revisions → missing=2 and negative deltas; broken links detection) and sampleRate=0 edge case into conformant Gherkin

### Implementation for User Story 3

- [X] T025 [P] [US3] Add unit tests for correctness metrics in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/MigrationMetricsTests.cs` — test `RecordRevisionSourceCount`, `RecordRevisionTargetCount`, `RecordRevisionDelta`, `RecordRevisionsMissing`, `RecordRevisionOrderError`, `RecordBrokenLink`, `RecordMissingWorkItem`
- [X] T026 [US3] Wire correctness metric recording into Tier 3 post-flight validation pass in `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` (`ValidateAsync`) — for each sampled work item, call source/target count recording, compute and record delta, increment missing/broken counters as applicable (FR-017 through FR-024)
- [X] T027 [US3] Verify correctness metrics respect `sampleRate` gating — at `sampleRate=0` no correctness metrics are emitted, at `sampleRate=1.0` all items are checked (FR-024, SC-005)
- [X] T028 [US3] Add `SnapshotMetricExporter` test cases for correctness metrics in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/SnapshotMetricExporterTests.cs` — verify `RevisionSourceCountMean`, `RevisionTargetCountMean`, `RevisionDeltaMean`, `RevisionsMissing`, `RevisionOrderErrors`, `BrokenLinks`, `MissingWorkItems` populate correctly

**Checkpoint**: User Stories 1, 2, AND 3 all work — full execution, payload, and correctness observability.

---

## Phase 6: User Story 4 — Operator monitors in-flight concurrency and queue pressure (Priority: P3)

**Goal**: Emit `migration.workitems.in_flight` UpDownCounter and `migration.queue.workitems.depth` ObservableGauge, observable via MetricSnapshot.

**Independent Test**: Run an export with concurrency set to 4. Verify that `in_flight` never exceeds 4 and `queue depth` reflects the backlog.

### Gherkin Feature File for User Story 4 (mandatory)

- [X] T029 [US4] Create `features/platform/telemetry/in-flight-concurrency-metrics.feature` — translate spec.md US4 acceptance scenarios (maxConcurrency=4 → in_flight between 0 and 4; 100 pending items → queue depth starts ~100 and decreases) into conformant Gherkin

### Implementation for User Story 4

- [X] T030 [P] [US4] Add unit tests for in-flight metrics in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/MigrationMetricsTests.cs` — test `IncrementInFlight`, `DecrementInFlight` UpDownCounter semantics; test ObservableGauge callback for queue depth
- [X] T031 [US4] Wire `IncrementInFlight`/`DecrementInFlight` calls into work item processing orchestration in `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs` and `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` — increment at processing start, decrement at completion or failure (FR-030)
- [X] T032 [US4] Register `ObservableGauge` for queue depth with a `Func<int>` callback that reads the current pending count from the job engine's internal queue (FR-031)
- [X] T033 [US4] Add `SnapshotMetricExporter` test cases for in-flight metrics in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/SnapshotMetricExporterTests.cs` — verify `WorkItemsInFlight` and `QueueDepth` properties populate correctly from UpDownCounter and ObservableGauge

**Checkpoint**: All operational metrics complete — execution, payload, correctness, in-flight, and queue pressure.

---

## Phase 7: User Story 5 — Idempotency and resume metrics signal correctness failures (Priority: P3)

**Goal**: Register all 5 deferred idempotency counters at application startup. Instruments are created but not incremented until the work item identity mapping store is implemented.

**Independent Test**: Verify via unit test that all 5 deferred instruments are registered, accept increments, and map to nullable `MetricSnapshot` properties.

### Gherkin Feature File for User Story 5 (mandatory)

- [X] T034 [US5] Create `features/platform/telemetry/idempotency-metric-registration.feature` — translate spec.md US5 acceptance scenario (instruments registered at startup, accept increments) into conformant Gherkin

### Implementation for User Story 5

- [X] T035 [P] [US5] Add unit tests for deferred idempotency instruments in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/MigrationMetricsTests.cs` — verify `RecordDuplicated`, `RecordChangedOnRerun`, `RecordReprocessedAfterResume`, `RecordDuplicatedAfterResume`, `RecordMissingAfterResume` are callable and instruments are registered under the Migration meter (SC-002)
- [X] T036 [US5] Verify deferred instruments are registered at startup — confirm `MigrationMetrics` constructor creates all 5 deferred Counter instruments (FR-025 through FR-029)
- [X] T037 [US5] Add `SnapshotMetricExporter` test cases for deferred metrics in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/SnapshotMetricExporterTests.cs` — verify nullable `Duplicated`, `ChangedOnRerun`, `ReprocessedAfterResume`, `DuplicatedAfterResume`, `MissingAfterResume` properties default to `null` when no measurements recorded

**Checkpoint**: All 28 instruments registered and tested. Deferred instruments ready for future mapping store.

---

## Phase 8: Documentation Sync (MANDATORY — cannot be skipped)

**Purpose**: Ensure all canonical docs reflect what was implemented in this spec. This phase is a blocking gate — no spec is complete without it.

- [X] T038 Add a "Telemetry" or "Observability" section to `docs/configuration-reference.md` — document the `migration.` dot-separated naming convention and mandatory dimension tags (`job.id`, `operation`, `module`) per `discrepancies.md` item 1
- [X] T039 [P] Update `docs/architecture.md` Phase 2 item 20 — add reference to `migration.*` dot-separated metric convention defined in `WellKnownMetricNames` per `discrepancies.md` item 2
- [X] T040 [P] Update `docs/control-plane.md` — add note to `GET /jobs/{jobId}/telemetry` endpoint that `MetricSnapshot` is a versioned DTO whose fields correspond to registered OTel instruments per `discrepancies.md` item 3
- [X] T041 [P] Update `docs/validation.md` Tier 3 Post-Flight Validation section — add paragraph noting OTel metric emission (count parity histograms and error counters) alongside `validation-report.json`, respecting `sampleRate` per `discrepancies.md` item 4
- [X] T042 [P] Update `docs/development-setup.md` ConfigureOpenTelemetry sample — register `DevOpsMigrationPlatform.Migration` meter instead of the two separate meter names per `discrepancies.md` item 5
- [X] T043 [P] Update `docs/agent-hosting.md` Responsibilities table — add "Record metrics" row for `IMigrationMetrics` during job execution per `discrepancies.md` item 6
- [X] T044 [P] Update `docs/migration-process-guide.md` Job Engine Steps — amend step 6 or add step 6a for OTel metric recording alongside progress event emission per `discrepancies.md` item 7
- [X] T045 Mark all items in `specs/018-workitem-otel-metrics/discrepancies.md` as `Resolved` or `N/A`
- [X] T046 Review `analysis/pending-actions.md` and remove any items resolved by this spec
- [X] T047 Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [X] T048 Run `dotnet test` — ALL tests MUST pass
- [X] T049 Run at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `.vscode/launch.json` debug profile and verify observable output

---

## Phase 9: Polish & Cross-Cutting Concerns (OPTIONAL)

**Purpose**: Clean up and verify end-to-end telemetry pipeline.

- [X] T050 [P] Verify no references to removed legacy `MetricSnapshot` properties remain in TUI rendering or control plane code (grep for `WorkItemsExported`, `RevisionsExported`, `RevisionErrors`, `LinksExported`, `LinkErrors`, `AttachmentsAttempted`, `AttachmentsSucceeded`, `AttachmentsFailed`, `RevisionDurationMeanMs`, `TotalExportDurationMs`)
- [X] T051 [P] Remove deprecated `WorkItemExport` and `AttachmentDownload` meter registrations if no OTLP/Azure Monitor collectors reference the old meter names
- [X] T052 Verify all `IWorkItemExportMetrics` and `IAttachmentDownloadMetrics` usages have been migrated to `IMigrationMetrics` — remove old interfaces if fully replaced

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 completion — BLOCKS all user stories
- **US1 (Phase 3)**: Depends on Phase 2 — MVP target
- **US2 (Phase 4)**: Depends on Phase 2 — can run in parallel with US1
- **US3 (Phase 5)**: Depends on Phase 2 — can run in parallel with US1 and US2
- **US4 (Phase 6)**: Depends on Phase 2 — can run in parallel with US1–US3
- **US5 (Phase 7)**: Depends on Phase 2 — can run in parallel with US1–US4
- **Documentation Sync (Phase 8)**: Depends on all user stories being complete
- **Polish (Phase 9)**: Depends on Phase 8

### Parallel Execution Map

```
Phase 1: T001 ─┐
               ├─ sequential (T001 updates constants used by T004+T005)
T002 ──────────┤  (parallel with T001)
T003 ──────────┘  (parallel with T001)

Phase 2: T007─T008─T009 sequential
         T010, T011, T012, T013, T014 parallel (after T007)

Phase 3–7: All user stories can proceed in parallel after Phase 2
           Within each story: feature file → tests → implementation → verification

Phase 8: All doc tasks (T038–T044) parallel
         T045–T049 sequential (validation gate)
```

### Implementation Strategy

- **MVP scope**: Phase 1 + Phase 2 + Phase 3 (US1) delivers the minimum viable metric instrumentation — execution counters, duration histogram, and MetricSnapshot expansion.
- **Incremental delivery**: Each subsequent user story adds an independent capability (payload → correctness → in-flight → idempotency).
- **Parallel opportunities**: After Phase 2, all 5 user stories can proceed independently on different files with no inter-story dependencies.
