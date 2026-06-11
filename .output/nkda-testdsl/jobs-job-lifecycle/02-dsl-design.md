# DSL Design: jobs-job-lifecycle

## Target test class
`DevOpsMigrationPlatform.ControlPlane.Tests.Jobs.JobLifecycleDslTests`
File: `tests/DevOpsMigrationPlatform.ControlPlane.Tests/Jobs/JobLifecycleDslTests.cs`

## Pattern
- Arrange: create `MetricsStub` (counting stub implementing `IJobLifecycleMetrics`), create `JobStore(metrics)`, enqueue a job
- Act: call `store.SetState(jobId, state)`
- Assert: verify state via `GetAllRecords()` and metric call counts on the stub

## Why a stub over Moq
`TagList` is a `System.Diagnostics.TagList` (InlineArray struct). Moq's `It.IsAny<TagList>()` matcher throws `NotSupportedException` at verification time due to the InlineArray equality constraint. A hand-rolled counting stub avoids this completely.
