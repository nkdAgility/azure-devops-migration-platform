# Target Behavioural Test Suite: agent_lease_coordination
## 1. Source Assessment
Consumes `.output/nkda-tddsn/agent_lease_coordination/01-assessment.md`.
## 2. Target Suite Gate
| Test Class | Test Method | Type | Protected Behaviour | Expected Assertions | Decision |
|------------|-------------|------|---------------------|--------------------|----------|
| `AgentWorkerBaseLeaseCoordinationTests` | `ExecuteAsync_WhenJobDispatchThrows_ClearsActiveLeaseAndPackageState` | Unit/design | Shared base worker releases active lease and package job state even when concrete dispatch throws unexpectedly | After dispatch has thrown and the hosted worker is stopped: `ActiveLeaseState.CurrentLeaseId` is null, `ActivePackageState.CurrentJob` is null, `ActivePackageState.CurrentRunId` is null, and dispatch was invoked once with the acquired lease id/job | Add |
| `JobAgentWorkerInventoryTests` | existing `InventoryDispatch_*` methods | Unit/design | Multi-org inventory dispatch and observability | Existing call/progress/log assertions remain passing | Keep |
| `TfsJobAgentWorkerTests` | existing migration/discovery methods | Unit/design | TFS job validation, dispatch, and terminal signalling | Existing terminal request and service assertions remain passing | Keep |
| `JobAgentWorkerDispatchTests` | existing methods | Unit/design | .NET agent concrete dispatch | Not currently executable because project excludes file | Defer restore to separate follow-up |
## 3. Required Test Support
- A minimal concrete `AgentWorkerBase` test double whose `OnJobAsync` records the job and lease then throws a deterministic exception.
- A fake `IHttpClientFactory` returning an `HttpClient` backed by an in-memory `HttpMessageHandler`.
- The handler returns one serialized lease response for `GET /agents/lease?...`.
- The test starts the background service, waits for the throwing dispatch path to be observed, cancels/stops the service, then asserts released state.
## 4. Out-of-Scope Target Tests
- Full restoration of `JobAgentWorkerDispatchTests.cs` is out of scope for this autonomous pass because it may expose broader compile and dependency drift unrelated to the shared lease cleanup gap.
- Base terminal signal retry coverage remains a future medium-priority addition.
