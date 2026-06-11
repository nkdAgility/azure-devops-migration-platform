# DSL Design: jobs-job-submission

## Test Class
`JobSubmissionDslTests` in `tests/DevOpsMigrationPlatform.ControlPlane.Tests/Jobs/`

## Method Mapping
| Scenario | Method |
|---|---|
| Submit an export job | Enqueue_ExportJob_IsInQueuedState |
| Submit an import job | Enqueue_ImportJob_IsInQueuedState |
| Submit a both-mode job | Enqueue_MigrateJob_IsInQueuedState |
| Dequeue a submitted job | DequeueAsync_AfterSubmittingExportJob_ReturnsMatchingJob |

## Design Notes
- All tests use `JobStore` directly (unit level, no HTTP layer).
- `[TestCategory("UnitTest")]` applied to all methods.
- "both-mode" mapped to `JobKind.Migrate` (Export+Import sequence).
