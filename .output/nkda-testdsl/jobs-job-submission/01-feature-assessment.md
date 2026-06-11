# Feature Assessment: jobs-job-submission

## Feature File
`features/platform/jobs/job-submission.feature`

## Wiring State
Unwired — no Reqnroll step bindings existed in tests/ for this feature family.

## Scenarios (4)
1. Submit an export job
2. Submit an import job
3. Submit a both-mode job
4. Dequeue a submitted job

## Source Types
- `DevOpsMigrationPlatform.ControlPlane.Jobs.JobStore` (Enqueue, DequeueAsync, GetAllRecords)
- `DevOpsMigrationPlatform.Abstractions.Jobs.Job`
- `DevOpsMigrationPlatform.Abstractions.Jobs.JobKind` (Export, Import, Migrate)

## Target Test Project
`tests/DevOpsMigrationPlatform.ControlPlane.Tests`

## Migration Risks
- "both-mode" in feature has no direct enum value; mapped to `JobKind.Migrate` which is "Export then Import in sequence" — semantically equivalent.
