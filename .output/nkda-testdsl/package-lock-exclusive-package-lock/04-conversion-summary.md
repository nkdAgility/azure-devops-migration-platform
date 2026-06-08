# Conversion Summary

## Mapped scenarios

| Scenario | TestMethod |
|---|---|
| Second agent is hard-bounced when live lock exists | ExclusivePackageLockTests.AcquireLockAsync_WhenLiveLockExists_SecondAgentReceivesPackageLockConflictException |
| Stale lock is replaced and agent proceeds normally | ExclusivePackageLockTests.AcquireLockAsync_WhenStaleLockExists_StaleLockReplacedAndNewAgentAcquires |
| Lock is released when job completes | ExclusivePackageLockTests.AcquireLockAsync_WhenDisposed_LockFileNoLongerExists |

All 3 scenarios: built-from-intent (new MSTest methods created).
