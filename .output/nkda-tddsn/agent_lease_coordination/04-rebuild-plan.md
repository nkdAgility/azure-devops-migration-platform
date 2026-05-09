# Rebuild Plan: agent_lease_coordination

## 1. Goal

Close the high-priority behavioural drift risk where active lease/package state can remain set after unexpected job dispatch failure.

## 2. Sequenced Work

1. Add `AgentWorkerBaseLeaseCoordinationTests.ExecuteAsync_WhenJobDispatchThrows_ClearsActiveLeaseAndPackageState`.
   - Expected initial result: fail because `AgentWorkerBase.PollAndExecuteAsync` currently clears state only after `OnJobAsync` returns normally.
2. Make the minimal production change in `AgentWorkerBase`.
   - Add `try/finally` around `OnJobAsync`, `_leaseState.CurrentLeaseId = null`, `OnPostJobFlushAsync`, and `_packageState.Clear()`.
   - Preserve success ordering: dispatch, clear lease id, flush, clear package state.
3. Run the focused test class.
4. Run the relevant infrastructure agent test project.
5. Run the TFS migration agent tests if the environment supports the target framework/runtime.
6. Record results in `05-implementation-summary.md` and `06-verification.md`.

## 3. Stopping Points

- Stop if the new test cannot deterministically observe dispatch without sleeps.
- Stop if the production change requires altering public contracts or concrete worker behaviour.
- Stop if verification cannot produce executable evidence.

## 4. Required Seams

- Test-only `ThrowingAgentWorker` subclass.
- Test-only `SingleLeaseResponseHandler`.
- Test-only `TestHttpClientFactory`.

## 5. Minimal Production Change Statement

Required by target test: `ExecuteAsync_WhenJobDispatchThrows_ClearsActiveLeaseAndPackageState`.
Behaviour corrected: active lease/package state is released after unexpected dispatch failure.
Why minimal: one lifecycle `try/finally`, no public API changes, no terminal signalling changes, no concrete worker dispatch changes.
Architecture docs: no source docs changed; this artifact records the strengthened invariant.
