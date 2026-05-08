# Architecture Update Proposal: agent_package_persistence

## 1. Established Architecture

`ActivePackageState` carries the current package store and job for services that cannot receive `IArtefactStore` through construction because the package is resolved only after lease acquisition. Package diagnostic and progress sinks buffer records and append NDJSON lines through `IArtefactStore`. `PackagePaths` defines fallback `.migration/Logs` and run-scoped `.migration/runs/<runId>/logs` paths.

## 2. Proposed Clarifications

Document that any package sink that caches a store reference for delayed shutdown flushing must cache the matching log folder at the same time. A cached store without cached path context is insufficient because `ActivePackageState.Clear()` resets the run identity.

## 3. Behavioural Contracts

- Records emitted while a job is active belong to that job's run log folder.
- Delayed flush after state clear must use the run log folder captured at emit/drain time.
- Records emitted without any observed package store may be dropped according to best-effort logging semantics.

## 4. State Transitions

- Active job observed: sink captures both `CurrentStore` and `CurrentLogFolder`.
- Flush with active state: sink uses live `CurrentLogFolder`.
- Flush after state clear: sink uses cached log folder paired with the cached store.
- No cached store: sink drops records rather than inventing package state.

## 5. Failure and Rejection Behaviour

Append failure remains best-effort: the sinks count dropped records and do not fail the migration. The change does not alter append failure handling.

## 6. Observability Behaviour

Progress logs remain at `<run-log-folder>/progress.jsonl`; diagnostic logs remain at `<run-log-folder>/agent.jsonl` with existing rotation semantics. This supports run-scoped auditability after process shutdown.

## 7. Testability Seams

The existing `IArtefactStore.AppendAsync` boundary is sufficient. Tests can capture append paths through a mock store and drive delayed flush with `IFlushable.FlushAsync`.

## 8. Open Questions

- Whether `ActivePackageState` should expose a single immutable active package snapshot in the future to prevent store/path divergence more broadly. This was not required for the minimal fix.
