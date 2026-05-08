# Target Test Suite: agent_package_persistence

## 1. Design Intent

Protect the package persistence boundary where buffered package telemetry survives fast-job completion and shutdown. The target suite focuses on observable package paths passed to `IArtefactStore.AppendAsync`, not private sink internals.

## 2. Proposed Test Classes

- class name: `PackagePersistenceRunLogFlushTests`
- purpose: Verify buffered package progress and diagnostic logs remain in the run-scoped folder captured while the job was active, even if flushed after `ActivePackageState.Clear()`.
- test type emphasis: unit/design contract tests
- related production area: `PackageProgressSink`, `PackageLoggerProvider`, `ActivePackageState`, `PackagePaths`

## 3. Proposed Tests

- test method name: `PackageProgressSink_FlushAfterPackageStateClear_WritesToOriginalRunLogFolder`
- type: unit/design
- status: add
- protects: Progress log records emitted during an active job are appended to the original run-scoped `progress.jsonl`.
- drift risk: DR-1, DR-2, DR-3
- scenario:
  - Given: an active package state with a current job and artefact store
  - When: a progress event is emitted, the active package state is cleared, and the sink is flushed
  - Then: the append path is `<captured-run-log-folder>/progress.jsonl`
- assertions: exactly one append occurs; appended path equals the run folder captured before clear plus `progress.jsonl`; no fallback `.migration/Logs` path is used.
- notes: Uses a mock `IArtefactStore`; no private methods are tested.

- test method name: `PackageLoggerProvider_FlushAfterPackageStateClear_WritesToOriginalRunLogFolder`
- type: unit/design
- status: add
- protects: Diagnostic log records emitted during an active job are appended to the original run-scoped `agent.jsonl`.
- drift risk: DR-1, DR-3, DR-4
- scenario:
  - Given: an active package state with a current job and artefact store
  - When: a diagnostic log is written, the active package state is cleared, and the provider is flushed
  - Then: the append path is `<captured-run-log-folder>/agent.jsonl`
- assertions: exactly one append occurs; appended path equals the run folder captured before clear plus `agent.jsonl`; no fallback `.migration/Logs` path is used.
- notes: Complements existing rotation tests rather than replacing them.

## 4. Required Test Support

- fakes: none
- builders: none
- test context helpers: none
- data builders: inline `Job` instances with stable job IDs
- deterministic clocks: not required; tests capture the run folder before clearing state rather than asserting a fixed timestamp
- ID providers: not required
- storage fakes: mock `IArtefactStore` capturing `AppendAsync` paths
- schedulers: not required because tests call explicit `FlushAsync`

## 5. Explicit Non-Goals

- Do not test hosted-service timing or background loop scheduling; explicit flush is the contract under test.
- Do not test Azure Blob persistence separately; the behavioural contract is the `IArtefactStore.AppendAsync` path/content boundary.
- Do not test logger rotation here; existing rotation tests cover segment naming.
