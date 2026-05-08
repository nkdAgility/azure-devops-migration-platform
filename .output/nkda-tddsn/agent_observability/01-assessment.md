# TDD Safety Net Assessment: agent_observability

## 1. Scope

Subsystem:
agent_observability

Analysed sources:
- .agents/context/architecture/agent-observability.md
- docs/client-integration-guide.md
- docs/tui-guide.md
- src/DevOpsMigrationPlatform.ControlPlane/Controllers/ProgressController.cs
- src/DevOpsMigrationPlatform.ControlPlane/Controllers/DiagnosticsController.cs
- src/DevOpsMigrationPlatform.ControlPlane/Controllers/TelemetryController.cs
- src/DevOpsMigrationPlatform.ControlPlane/Jobs/JobProgressStore.cs
- src/DevOpsMigrationPlatform.ControlPlane/Jobs/DiagnosticLogStore.cs
- src/DevOpsMigrationPlatform.ControlPlane/Jobs/JobMetricsStore.cs
- src/DevOpsMigrationPlatform.Abstractions/Streaming/DiagnosticLogRecord.cs
- src/DevOpsMigrationPlatform.Abstractions.ControlPlane/Telemetry/ProgressEvent.cs
- src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneProgressSink.cs
- src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneTelemetryClient.cs
- src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneLoggerProvider.cs
- src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/PackageLoggerProvider.cs
- src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/PlatformMetrics.cs

Analysed tests:
- features/platform/telemetry/job-progress-store.feature
- features/platform/telemetry/progress-controller.feature
- features/platform/telemetry/progress-sink.feature
- features/platform/telemetry/metric-snapshot-relay.feature
- features/platform/observability/diagnostics-streaming.feature
- features/platform/observability/package-diagnostics-sink.feature
- features/platform/observability/package-progress-sink.feature
- tests/DevOpsMigrationPlatform.ControlPlane.Tests/Progress/JobProgressStoreSteps.cs
- tests/DevOpsMigrationPlatform.ControlPlane.Tests/Progress/ProgressControllerSteps.cs
- tests/DevOpsMigrationPlatform.Infrastructure.ControlPlane.Tests/Metrics/InMemoryJobMetricsStoreTests.cs
- tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Telemetry/ControlPlaneProgressSinkSteps.cs
- tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Telemetry/ControlPlaneTelemetryClientTests.cs
- tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Telemetry/PackageLoggerProviderRotationTests.cs
- tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Telemetry/PlatformMetricsTests.cs
- tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Telemetry/DataClassificationLogProcessorTests.cs

Partial analysis warnings:
- None. Required guardrails and docs/testing-guide.md were present.

## 2. Behaviour Model

Purpose:
The agent_observability subsystem emits, filters, stores, transports, and exposes progress events, diagnostic log records, traces, and metric snapshots so operators and clients can observe migration execution through the Control Plane API without direct package or sink access.

Primary behaviours:
B1. Agent progress events are accepted for recognised leases, retained in bounded per-job buffers, streamed through SSE, and exposed as snapshots for late clients.
B2. Diagnostic log records are accepted for recognised leases, filtered by deployment and client log levels, retained in bounded per-job buffers, and streamed/replayed to clients.
B3. Telemetry metric snapshots are posted for recognised leases, merged so module snapshots do not erase unrelated counters, and exposed through the telemetry endpoint.
B4. Package-level observability sinks persist progress and diagnostic records locally without leaking customer-classified data through exported telemetry.
B5. TUI and external clients consume Control Plane progress, diagnostics, and telemetry channels only; they must not read ProgressEvent.Metrics for .NET 10 counters.

State transitions:
S1. No job entry -> active job buffer when the first progress or diagnostic record arrives.
S2. Active job buffer -> completed/failed when CompleteJob is called; new subscribers receive immediate completion.
S3. Empty metrics snapshot -> latest snapshot when telemetry is pushed; later partial module snapshots merge into existing metrics.
S4. SSE stream open -> replay buffered records -> stream live records -> terminal job-ended/job-failed event.

External contracts:
C1. POST /agents/lease/{leaseId}/progress stores ProgressEvent for the resolved job or returns 404 for unknown leases.
C2. GET /jobs/{jobId}/progress supports snapshot and follow=true SSE with Last-Event-ID/replay semantics described in client docs.
C3. POST /agents/lease/{leaseId}/diagnostics stores DiagnosticLogRecord batches for the resolved job or returns 404 for unknown leases.
C4. GET /jobs/{jobId}/diagnostics returns JSON snapshot or text/event-stream with replay, level filtering, and terminal events.
C5. POST /agents/lease/{leaseId}/telemetry and GET /jobs/{jobId}/telemetry expose latest metrics without relying on ProgressEvent.Metrics.

Failure and rejection behaviours:
F1. Unknown lease posts are rejected with 404 and must not create orphan observability buffers.
F2. Unauthorized clients receive 403 for protected job views.
F3. Diagnostic records below the configured deployment minimum log level are discarded before buffering.
F4. Follow streams complete cleanly for jobs already completed before subscription.
F5. ControlPlaneProgressSink failures are best-effort: events may be dropped, but job execution must not throw.

Boundary conditions:
E1. Ring buffers evict oldest retained records at configured capacity.
E2. Late subscribers replay bounded recent state and then receive live records.
E3. Invalid or lower-than-filter diagnostic records do not leak into filtered snapshots.
E4. Metrics merge preserves existing module counters when incoming partial snapshots omit them.
E5. Customer-classified log records are filtered from exported telemetry.

Drift risks:
D1. DiagnosticLogStore behaviour had feature-level documentation but no fast unit safety net for capacity, minimum-level filtering, level-filtered snapshots, live subscriber delivery, or late-completion semantics.
D2. Some existing Reqnroll step definitions contain structural/no-op assertions in older telemetry scenarios, which can mask drift if retained as the only protection.
D3. Control Plane endpoint tests cover progress more thoroughly than diagnostics, so diagnostics streaming could drift while progress tests continue to pass.
D4. Client documentation warns against ProgressEvent.Metrics for counters; tests must continue to protect telemetry endpoint usage rather than progress event counters.

## 3. Current Test Inventory

| Test | Type | Behaviour Protected | Score | Classification | Action |
|------|------|---------------------|-------|----------------|--------|
| JobProgressStore feature scenarios | Feature test | Progress ring buffer capacity and late-complete subscriber completion | 31/36 | GOOD TDD | Keep |
| ProgressController feature scenarios | Feature test | Progress post/get happy path, unknown lease, unauthorized read | 27/36 | ACCEPTABLE WITH ISSUES | Keep |
| ControlPlaneProgressSink feature steps | Feature test | Best-effort post and failure swallowing | 20/36 | WEAK / MIXED | Rewrite later; not in this rebuild |
| ControlPlaneTelemetryClientTests | Unit/design test | Telemetry client transport and payload shape | 29/36 | GOOD TDD | Keep |
| PackageLoggerProviderRotationTests | Unit/design test | Package diagnostic file rotation | 30/36 | GOOD TDD | Keep |
| PlatformMetricsTests | Unit/design test | Metric instruments and snapshots | 30/36 | GOOD TDD | Keep |
| DataClassificationLogProcessorTests | Unit/design test | Customer data filtering and allowed classifications | 32/36 | GOOD TDD | Keep |
| InMemoryJobMetricsStoreTests | Unit/design test | Latest metric snapshot preservation | 31/36 | GOOD TDD | Keep |
| DiagnosticLogStore behavioural tests | Unit/design test | Missing diagnostics buffer/filter/stream completion behaviours | 0/36 | Missing | Add |

## 4. Detailed Scoring

For each test:
- test name: JobProgressStore feature scenarios; test type: feature; behaviour protected: progress buffer capacity and completion state; dimension scores: behaviour focus 3, small/focused 3, readable 3, fails right reason 3, deterministic 3, fast 3, independent 3, clear name 3, meaningful example 2, minimises mocking 3, design pressure 2, asserts contracts 3; total 34 adjusted to 31 for narrow coverage; gated classification GOOD TDD; recommended action Keep.
- test name: ProgressController feature scenarios; test type: feature; behaviour protected: Control Plane progress endpoint contract; dimension scores: behaviour focus 3, small/focused 2, readable 2, fails right reason 2, deterministic 3, fast 2, independent 3, clear name 2, meaningful example 2, minimises mocking 2, design pressure 2, asserts contracts 2; total 27; gated classification ACCEPTABLE WITH ISSUES; recommended action Keep.
- test name: ControlPlaneProgressSink feature steps; test type: feature; behaviour protected: progress sink best-effort transport; dimension scores: behaviour focus 2, small/focused 2, readable 2, fails right reason 1, deterministic 2, fast 2, independent 2, clear name 2, meaningful example 2, minimises mocking 1, design pressure 1, asserts contracts 1; total 20; gated classification WEAK / MIXED due no-op assertions; recommended action Rewrite later.
- test name: ControlPlaneTelemetryClientTests; test type: unit/design; behaviour protected: telemetry client sends snapshots to Control Plane; dimension scores: behaviour focus 3, small/focused 2, readable 2, fails right reason 3, deterministic 3, fast 3, independent 3, clear name 3, meaningful example 2, minimises mocking 2, design pressure 3, asserts contracts 3; total 32 adjusted to 29 for mock boundary reliance; recommended action Keep.
- test name: PackageLoggerProviderRotationTests; test type: unit/design; behaviour protected: diagnostic package log rotation; dimension scores total 30/36; classification GOOD TDD; recommended action Keep.
- test name: PlatformMetricsTests; test type: unit/design; behaviour protected: metric counter semantics; dimension scores total 30/36; classification GOOD TDD; recommended action Keep.
- test name: DataClassificationLogProcessorTests; test type: unit/design; behaviour protected: classified log filtering; dimension scores total 32/36; classification GOOD TDD; recommended action Keep.
- test name: InMemoryJobMetricsStoreTests; test type: unit/design; behaviour protected: metric snapshot latest-state behaviour; dimension scores total 31/36; classification GOOD TDD; recommended action Keep.
- test name: DiagnosticLogStore behavioural tests; test type: unit/design; behaviour protected: diagnostic ring buffer/filter/subscriber/completion contracts; score 0/36 because tests were missing before this rebuild; recommended action Add.

## 5. Drift Risk Map

For each risk:
- behaviour: Diagnostic ring buffer capacity and eviction; current protection: none; why drift can occur: diagnostics feature docs exist but no fast unit test fails if retention changes; proposed protection: Add_WhenRingBufferExceedsCapacity_EvictsOldestRetainedRecord; priority: high
- behaviour: Deployment-level diagnostic minimum level; current protection: none; why drift can occur: lower-level records could leak into buffers and clients; proposed protection: Add_WhenRecordIsBelowDeploymentMinimumLevel_DiscardsRecordBeforeBuffering; priority: high
- behaviour: Client level filter snapshots; current protection: none; why drift can occur: UI filters could show records below selected level; proposed protection: GetSnapshot_WhenLevelFilterIsProvided_ReturnsRecordsAtOrAboveRequestedLevel; priority: medium
- behaviour: Live diagnostic subscriber delivery; current protection: partial through docs only; why drift can occur: appended records could remain in snapshots but not reach SSE subscribers; proposed protection: Subscribe_WhenRecordIsAdded_NotifiesLiveSubscriberWithoutPollingSnapshot; priority: high
- behaviour: Completed/failed diagnostic streams; current protection: none; why drift can occur: late subscribers could hang after a completed job; proposed protection: Subscribe_WhenJobAlreadyCompleted_CompletesSubscriberImmediately; priority: high

## 6. Gap Map

| Behaviour / Risk | Existing Protection | Missing Tests | Priority |
|------------------|--------------------|---------------|----------|
| Diagnostic buffer capacity | none | Add_WhenRingBufferExceedsCapacity_EvictsOldestRetainedRecord | high |
| Deployment minimum log level | none | Add_WhenRecordIsBelowDeploymentMinimumLevel_DiscardsRecordBeforeBuffering | high |
| Snapshot level filtering | none | GetSnapshot_WhenLevelFilterIsProvided_ReturnsRecordsAtOrAboveRequestedLevel | medium |
| Live diagnostics subscriber delivery | partial | Subscribe_WhenRecordIsAdded_NotifiesLiveSubscriberWithoutPollingSnapshot | high |
| Late completion/failed diagnostic subscriber semantics | none | Subscribe_WhenJobAlreadyCompleted_CompletesSubscriberImmediately | high |

## 7. Summary

Keep:
- Existing progress, telemetry, package logger, platform metrics, and data-classification tests.

Rewrite:
- Older ControlPlaneProgressSink structural/no-op assertions should be rewritten in a later focused rebuild.

Delete:
- None in this autonomous pass.

Add:
- DiagnosticLogStoreTests.Add_WhenRingBufferExceedsCapacity_EvictsOldestRetainedRecord
- DiagnosticLogStoreTests.Add_WhenRecordIsBelowDeploymentMinimumLevel_DiscardsRecordBeforeBuffering
- DiagnosticLogStoreTests.GetSnapshot_WhenLevelFilterIsProvided_ReturnsRecordsAtOrAboveRequestedLevel
- DiagnosticLogStoreTests.Subscribe_WhenRecordIsAdded_NotifiesLiveSubscriberWithoutPollingSnapshot
- DiagnosticLogStoreTests.Subscribe_WhenJobAlreadyCompleted_CompletesSubscriberImmediately

Highest risk missing protection:
- Diagnostic log records could leak below the configured minimum level or fail to stream/terminate correctly without any fast unit failure.

Next best action:
Implement the DiagnosticLogStore unit suite before touching production code; only adjust production if those target tests expose a failing behaviour.
