# TDD Safety Net Assessment: agent_lease_coordination

## 1. Scope

Subsystem:
agent_lease_coordination

Analysed sources:
- `.agents/30-context/architecture/agent-lease-coordination.md`
- `.agents/30-context/domains/control-plane-concept.md`
- `.agents/30-context/domains/entitlements-model.md`
- `docs/agent-hosting.md`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/AgentWorkerBase.cs`
- `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs`
- `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs`
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Lease/ActiveLeaseState.cs`
- `src/DevOpsMigrationPlatform.Abstractions.Agent/Lease/ActivePackageState.cs`

Analysed tests:
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobAgentWorkerDispatchTests.cs` (present but excluded from compile by project file)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobAgentWorkerInventoryTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs`

Partial analysis warnings:
- `JobAgentWorkerDispatchTests.cs` is named in subsystem docs but removed from compilation in `DevOpsMigrationPlatform.Infrastructure.Agent.Tests.csproj`, so its apparent coverage is not executable evidence.
- No direct existing executable unit test was found for the shared `AgentWorkerBase` lease acquire/release lifecycle.

## 2. Behaviour Model

Purpose:
The subsystem coordinates agent work leases by polling the control plane for jobs matching agent capabilities, recording active lease/job/package state for collaborators, dispatching the leased job to the concrete worker, signalling terminal state, flushing job sinks, and clearing active state before the worker polls again.

Primary behaviours:
B1. Poll the control plane lease endpoint with the agent connector capabilities.
B2. Treat HTTP 204 as no work and delay before the next poll.
B3. Deserialize a lease response into a lease id and job.
B4. Publish the active lease id and job before dispatch so telemetry, progress, and package services can observe job context.
B5. Dispatch the job to the concrete worker with the control-plane client, lease id, and cancellation token.
B6. Clear active lease and package state after dispatch completes.
B7. Flush buffered post-job sinks before clearing package state.
B8. Retry terminal complete/fail signalling with exponential backoff.

State transitions:
S1. Idle -> Leased when a lease response is received.
S2. Leased -> Dispatching after active state is set and `OnJobAsync` starts.
S3. Dispatching -> Released after `OnJobAsync` completes and post-job flush runs.
S4. Dispatching -> Released even when `OnJobAsync` fails unexpectedly; active state must not leak into the next poll.
S5. Released -> Idle after `ActivePackageState.Clear()` completes.

External contracts:
C1. GET `/agents/lease?capabilities=...` requests work for advertised connector capabilities.
C2. POST `/agents/lease/{leaseId}/complete` and `/fail` are used by concrete workers to signal terminal states.
C3. `ActiveLeaseState.CurrentLeaseId` is nullable and represents exactly one currently held lease.
C4. `ActivePackageState.CurrentJob`, `CurrentStore`, and run id cache are per-job and must be cleared on lease release.

Failure and rejection behaviours:
F1. No-content lease response does not set active state.
F2. Lease polling HTTP failure propagates to the background loop and is logged before retry.
F3. Job dispatch failure must not leave stale active lease or package state.
F4. Terminal signalling failures are retried and eventually logged without throwing after all attempts fail.

Boundary conditions:
E1. Empty capability list produces an empty capabilities query value.
E2. Null lease response results in no dispatch and no active state.
E3. Cancellation during polling or delay stops the background loop.
E4. Dispatch failure before concrete worker cleanup still requires base lease/package cleanup.

Drift risks:
D1. Stale `ActiveLeaseState` can misattribute telemetry/progress to an expired lease after dispatch failure.
D2. Stale `ActivePackageState.CurrentJob` or run id can send logs/artifacts to the wrong run folder.
D3. Compile-excluded dispatch tests can give false confidence that job dispatch and cleanup contracts are protected.
D4. TFS and .NET 10 agent workers share the base lease lifecycle, so base cleanup drift affects both runtimes.

## 3. Current Test Inventory

| Test | Type | Behaviour Protected | Score | Classification | Action |
|------|------|---------------------|-------|----------------|--------|
| `JobAgentWorkerDispatchTests.*` | Unit/design | Concrete .NET agent job dispatch, config context, completion marker behavior | 0/36 | POOR TDD | Rewrite/restore separately; excluded from compile |
| `JobAgentWorkerInventoryTests.InventoryDispatch_*` | Unit/design | Multi-org inventory dispatch and progress/logging | 33/36 | GOOD TDD | Keep |
| `TfsJobAgentWorkerTests.OnMigrationJob_*` | Unit/design | TFS migration job validation, service creation, terminal signalling | 31/36 | GOOD TDD | Keep |
| `TfsJobAgentWorkerTests.OnDiscoveryJob_*` | Unit/design | TFS discovery dispatch and failure handling | 31/36 | GOOD TDD | Keep |
| Missing `AgentWorkerBase` cleanup-on-dispatch-failure test | Unit/design | Shared lease/package cleanup under unexpected dispatch failure | 0/36 | POOR TDD | Add |

## 4. Detailed Scoring

### `JobAgentWorkerDispatchTests.*`
- test type: unit/design
- behaviour protected: concrete JobAgentWorker dispatch decisions and context cleanup
- dimension scores: behaviour focus 0, focused 2, readable 2, right reason 0, deterministic 2, fast 2, independent 2, name 2, meaningful example 2, minimises mocking 1, design pressure 2, outcome/contract assertions 0
- total: 17/36 before hard gates; gated classification `POOR TDD` because the file is excluded from compilation and therefore cannot fail.
- action: restore executable coverage in a follow-up; do not count it as current safety net evidence.

### `JobAgentWorkerInventoryTests.InventoryDispatch_*`
- test type: unit/design
- behaviour protected: multi-org inventory dispatch remains observable through calls, progress, metrics, and logs.
- scores: 3,2,3,3,3,3,3,3,3,2,2,3 = 33/36
- classification: GOOD TDD
- action: keep.

### `TfsJobAgentWorkerTests.OnMigrationJob_*` and `OnDiscoveryJob_*`
- test type: unit/design
- behaviour protected: TFS worker rejects invalid jobs, creates services for valid exports/discovery, streams counts, and signals terminal states.
- scores: 3,2,2,3,3,3,3,3,2,2,2,3 = 31/36
- classification: GOOD TDD
- action: keep.

### Missing `AgentWorkerBase` cleanup-on-dispatch-failure test
- test type: unit/design
- behaviour protected: shared base worker clears active lease/package state on unexpected dispatch failure.
- scores: 0 across all dimensions because no executable test exists.
- classification: POOR TDD
- action: add a focused unit/design test.

## 5. Drift Risk Map

| Risk | Current Protection | Gap | Priority |
|------|--------------------|-----|----------|
| D1 stale lease after dispatch failure | None in executable shared-base tests | Add `AgentWorkerBase` failure cleanup test | High |
| D2 stale package job/run after dispatch failure | None in executable shared-base tests | Assert `ActivePackageState.CurrentJob` and run cache clear | High |
| D3 false confidence from excluded dispatch tests | Project file excludes the main dispatch test file | Document warning; follow-up restore | Medium |
| D4 terminal signal retry drift | Indirect concrete-worker tests only | Future base-level retry tests | Medium |

## 6. Suite-Level Gap Map

- Add a compiled `AgentWorkerBase` unit/design test for lease/package cleanup on dispatch failure.
- Add future base-level tests for no-content lease response and terminal signal retry exhaustion.
- Restore or replace compile-excluded `JobAgentWorkerDispatchTests.cs` so concrete dispatch coverage is executable.

## 7. Recommendations

1. Add a new focused MSTest class under `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/AgentWorkerBaseLeaseCoordinationTests.cs`.
2. First test: `ExecuteAsync_WhenJobDispatchThrows_ClearsActiveLeaseAndPackageState`.
3. Implement the minimal production change in `AgentWorkerBase.PollAndExecuteAsync` using `try/finally` around dispatch, post-job flush, and package cleanup.
4. Keep existing TFS and inventory tests unchanged.

