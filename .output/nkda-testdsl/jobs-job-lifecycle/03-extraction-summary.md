# Extraction Summary: jobs-job-lifecycle

## Scenarios mapped
| Scenario | Test method |
|---|---|
| Job transitions from Queued to Running | `SetState_QueuedToRunning_RaisesJobStartedMetric` |
| Job transitions from Running to Completed | `SetState_RunningToCompleted_RaisesJobCompletedMetricAndRecordsDuration` |
| Job transitions from Running to Failed | `SetState_RunningToFailed_RaisesJobFailedMetricAndRecordsReason` |
| Multiple state updates during processing | `SetState_MultipleRunningUpdates_PreservesRunningStateAndRaisesJobStartedOnce` |

## Pre-existing coverage
`JobStoreStateTests` covered state transitions but not metric/event assertions. New DSL tests complement rather than duplicate them.
