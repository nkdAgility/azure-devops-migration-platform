# TDD Safety Net Assessment: agent_package_persistence

## 1. Behaviour Model

### Subsystem purpose
`agent_package_persistence` owns durable package writes performed by the migration agent through `IArtefactStore` and `IStateStore`. It protects the package as the source of truth for artefacts, migration configuration, execution plans, cursor/state files, and run-scoped operational logs.

### Primary behaviours
- Resolve a package store for the active job and expose it through `ActivePackageState`.
- Persist artefacts and state through abstractions only; module code must not bypass `IArtefactStore`/`IStateStore`.
- Persist diagnostic and progress logs into the package using append-only NDJSON files.
- Keep fast-job and shutdown log drains from losing buffered package telemetry.
- Place active-job logs under the run-scoped folder returned by `ActivePackageState.CurrentLogFolder`.

### State transitions
- No active job: `ActivePackageState.CurrentStore` and `CurrentJob` are null; fallback logs use `.migration/Logs`.
- Lease acquired: worker sets `CurrentJob`, then job-specific code sets/uses `CurrentStore`; run-scoped log folder becomes `.migration/runs/<runId>/logs`.
- Event/log emitted: sinks capture the current store so buffered records can be flushed later.
- Job completion/shutdown: buffered records are drained before or after state clear; records emitted during the job must still land in the job's original run folder.
- State clear: current job/store are removed and future records without an active store are dropped or use fallback behaviour.

### External contracts
- `IArtefactStore.AppendAsync(path, content, ct)` is the persistence contract for package logs.
- Progress logs use `<run-log-folder>/progress.jsonl`.
- Diagnostic logs use `<run-log-folder>/agent.jsonl` with rotation suffixes when enabled.
- Package paths use forward slashes and `.migration` system folders.

### Failure and rejection behaviours
- If no store has ever been observed, buffered records are counted as dropped and are not written.
- Append failures are best-effort failures: records are counted as dropped and the sink does not fail the migration.
- Cancellation during flushing must not silently cancel append writes for already-buffered records.

### Boundary conditions
- Fast jobs may emit records and complete before the background drain loop wakes.
- Host shutdown may stop hosted sink services after the active package state has already been cleared.
- Records emitted while a job was active must not drift from run-scoped logs to fallback `.migration/Logs` during delayed flush.
- Rotation decisions must keep diagnostic log records in the same captured run folder.

### Drift risks
- DR-1: Buffered records flushed after `ActivePackageState.Clear()` can be written to `.migration/Logs` instead of the job run folder.
- DR-2: Tests cover logger rotation but not package progress run-folder preservation.
- DR-3: The fallback-store design can preserve the store while losing associated path context.
- DR-4: Existing tests favour structural package operations and under-protect fast-job/shutdown telemetry persistence.

## 2. Current Test Inventory

| Test/File | Type | Protected Behaviour | Assessment |
| --- | --- | --- | --- |
| `FileSystemArtefactStoreTests` | unit/design | Basic read/write/exists and lexicographic enumeration | Valuable for store behaviour, not run-scoped telemetry. |
| `FileSystemStateStoreTests` | unit/design | State-store persistence and cursor backing behaviour | Valuable for state durability, not log flush path drift. |
| `PackageConfigStoreTests` | unit/design | Config persistence and observability around config reads/writes | Valuable for config contracts, not active-job log folder preservation. |
| `PackageLoggerProviderRotationTests` | unit/design | Log path rotation under fallback `.migration/Logs` | Useful but misses active-job clear-after-write case. |
| `JobAgentWorkerDispatchTests` | unit/integration | Worker dispatch and flush registration in some paths | Test file is excluded from project compilation; does not protect runtime behaviour. |

## 3. Scored Tests

| Test Area | Behaviour Focus | Small/Focused | Readable Example | Fails Right Reason | Deterministic | Fast | Independent | Clear Name | Meaningful Example | Minimises Mocking | Design Pressure | Outcome/Contract Assertions | Classification |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| FileSystem artefact store | 3 | 3 | 3 | 2 | 3 | 3 | 3 | 3 | 2 | 3 | 2 | 3 | GOOD TDD |
| State store/checkpointing | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 2 | 2 | 2 | 2 | 3 | GOOD TDD |
| Package config store | 3 | 2 | 2 | 2 | 3 | 2 | 2 | 2 | 2 | 2 | 2 | 3 | GOOD TDD |
| Package logger rotation | 2 | 2 | 2 | 2 | 2 | 2 | 2 | 2 | 2 | 2 | 2 | 2 | MIXED |
| Package progress run flush | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | 0 | MISSING |

## 4. Suite-Level Gaps

- No behavioural test proves that `PackageProgressSink` preserves the active run log folder when flushing after state clear.
- No behavioural test proves that `PackageLoggerProvider` preserves the active run log folder when flushing after state clear.
- No test couples the fallback store reference with the associated path context, leaving a realistic fast-job/shutdown regression unprotected.

## 5. Recommendations

- Add focused unit/design tests for progress and diagnostic log sinks that emit while a job is active, clear package state, then flush.
- Keep existing store/config tests.
- Do not broaden into full worker integration unless a later regression shows worker sequencing is unclear; the current drift risk is isolated to sink path selection.
