# Target Test Suite: agent_observability

## 1. Design Intent

Strengthen the agent_observability safety net at the fastest useful level by adding direct unit/design tests for DiagnosticLogStore. The target suite protects documented diagnostics-streaming behaviours that were previously described in architecture and client-facing docs but not guarded by fast tests. Existing progress, metrics, package logger, and classification tests remain in place.

## 2. Proposed Test Classes

For each proposed class:
- class name: DiagnosticLogStoreTests
- purpose: protect bounded diagnostic retention, minimum-level filtering, filtered snapshots, live subscriber notification, and completed/failed stream state.
- test type emphasis: unit/design tests using in-memory store and Options.Create; no network, filesystem, or controller host.
- related production area: src/DevOpsMigrationPlatform.ControlPlane/Jobs/DiagnosticLogStore.cs and DiagnosticLogStoreOptions.cs.

## 3. Proposed Tests

For each proposed test:
- test method name: Add_WhenRingBufferExceedsCapacity_EvictsOldestRetainedRecord
- type: unit/design test
- status: add
- protects: bounded per-job diagnostic retention.
- drift risk: an unbounded or wrong-order buffer could overload memory or replay stale diagnostics.
- scenario:
  - Given: a store capacity of 2.
  - When: three retained records are added for one job.
  - Then: the oldest retained record is evicted and the two newest messages remain in order.
- assertions: snapshot count equals 2; messages equal second and third.
- notes: uses real DiagnosticLogStore.

For each proposed test:
- test method name: Add_WhenRecordIsBelowDeploymentMinimumLevel_DiscardsRecordBeforeBuffering
- type: unit/design test
- status: add
- protects: deployment-level minimum diagnostic level filtering.
- drift risk: Information/Debug records could leak into Warning-only deployments.
- scenario:
  - Given: a store configured with Warning minimum level.
  - When: Information, Warning, and Error records are added.
  - Then: only Warning and Error records are retained.
- assertions: snapshot count equals 2; messages equal retained warning and retained error.
- notes: no mocks required.

For each proposed test:
- test method name: GetSnapshot_WhenLevelFilterIsProvided_ReturnsRecordsAtOrAboveRequestedLevel
- type: unit/design test
- status: add
- protects: client-requested diagnostics level filtering.
- drift risk: a TUI level toggle could display lower-severity records than selected.
- scenario:
  - Given: retained Information, Warning, and Error records.
  - When: a Warning snapshot filter is requested.
  - Then: only Warning and Error are returned.
- assertions: snapshot count and ordered messages.
- notes: verifies store-level behaviour used by controller replay.

For each proposed test:
- test method name: Subscribe_WhenRecordIsAdded_NotifiesLiveSubscriberWithoutPollingSnapshot
- type: unit/design test
- status: add
- protects: live diagnostics delivery to SSE subscribers.
- drift risk: records could be buffered but not pushed live.
- scenario:
  - Given: a subscriber is registered.
  - When: a Warning record is added.
  - Then: the subscriber channel receives that record.
- assertions: TryRead succeeds and message matches.
- notes: unsubscribes at end of test.

For each proposed test:
- test method name: Subscribe_WhenJobAlreadyCompleted_CompletesSubscriberImmediately
- type: unit/design test
- status: add
- protects: late subscriber completion and failed-state reporting.
- drift risk: late clients could hang after terminal jobs.
- scenario:
  - Given: a job has completed with failed=true.
  - When: a subscriber connects.
  - Then: the reader completion is already completed and store reports completed/failed.
- assertions: reader.Completion.IsCompleted, IsCompleted true, WasFailed true.
- notes: mirrors progress-store late-completion protection for diagnostics.

## 4. Required Test Support

List required:
- fakes: none.
- builders: a private MakeRecord helper for DiagnosticLogRecord values.
- test context helpers: a private CreateStore helper using Options.Create.
- data builders: none beyond MakeRecord.
- deterministic clocks: not required because tests assert message/level, not timestamps.
- ID providers: fixed Guid constant.
- storage fakes: none.
- schedulers: none.

## 5. Explicit Non-Goals

List what will not be tested and why.
- Do not add hosted DiagnosticsController SSE integration tests in this pass; store-level tests cover the missing logic faster and existing docs/features cover endpoint intent.
- Do not rewrite older ControlPlaneProgressSink no-op assertions in this pass; that is a separate drift risk outside the diagnostic-store gap.
- Do not change production code unless the approved DiagnosticLogStore tests fail against documented behaviour.
- Do not test private methods or implementation-specific fields.
