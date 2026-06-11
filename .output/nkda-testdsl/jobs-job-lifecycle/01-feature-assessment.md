# Feature Assessment: jobs-job-lifecycle

## Feature file
`features/platform/jobs/job-lifecycle.feature`

## Scenarios (4)
1. Job transitions from Queued to Running
2. Job transitions from Running to Completed
3. Job transitions from Running to Failed
4. Multiple state updates during processing

## Wiring state
Unwired — no Reqnroll step bindings exist in tests/ for this feature family.

## Domain
`DevOpsMigrationPlatform.ControlPlane.Jobs.JobStore` (src) — manages job state via `SetState()` and fires `IJobLifecycleMetrics` events (JobStarted, JobCompleted, JobFailed, RecordJobDuration).

## Migration risk
Low — `JobStore.SetState` is already fully implemented and has existing state-transition tests in `JobStoreStateTests`. The feature adds metric/event verification on top.
