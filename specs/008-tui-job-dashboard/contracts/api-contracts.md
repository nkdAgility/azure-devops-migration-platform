# Contract: GET /jobs

**Owner**: `JobsController` in `DevOpsMigrationPlatform.ControlPlane`  
**Status**: Missing — must be added

---

## Request

```
GET /jobs
Authorization: Bearer <token>   |   Negotiate (Windows Auth)
Accept: application/json
```

Optional query parameters (future; not in scope for this feature):

| Parameter | Type | Description |
|-----------|------|-------------|
| `state` | string | Filter by job state (e.g., `Running`, `Queued`) |

---

## Response — 200 OK

```json
[
  {
    "jobId": "550e8400-e29b-41d4-a716-446655440000",
    "mode": "Export",
    "state": "Running",
    "submittedByUpn": "martin@nkdagility.com",
    "submittedAt": "2026-04-09T08:30:00Z"
  },
  {
    "jobId": "7c9e6679-7425-40de-944b-e07fc1f90ae7",
    "mode": "Both",
    "state": "Completed",
    "submittedByUpn": "martin@nkdagility.com",
    "submittedAt": "2026-04-08T14:15:00Z"
  }
]
```

Returns an empty array `[]` when no jobs are visible to the caller.

---

## Auth rules (same as `GET /jobs/{jobId}`)

- A regular user sees their own jobs plus any `Tenant`-visibility jobs in their tenant.
- A Control Plane Admin sees all jobs.

---

## Implementation notes

`IJobStore.GetAll()` already exists and returns `IReadOnlyList<MigrationJob>`. The controller projects each into `JobSummary`. `State`, `SubmittedAt`, `SubmittedByUpn` require the `JobRecord` wrapper described in `data-model.md` Gap 1.

---

# Contract: GET /jobs/{jobId}/telemetry (documentation gap only)

**Owner**: `TelemetryController` in `DevOpsMigrationPlatform.ControlPlane`  
**Status**: Implemented — missing from `docs/control-plane.md` API table

---

## Request

```
GET /jobs/{jobId}/telemetry
Authorization: Bearer <token>   |   Negotiate
```

---

## Response — 200 OK

```json
{
  "workItemsExported": 312,
  "revisionsExported": 874,
  "linksExported": 103,
  "attachmentsAttempted": 45,
  "attachmentsSucceeded": 44,
  "revisionErrors": 0,
  "linkErrors": 1,
  "attachmentsFailed": 1,
  "workItemDurationMeanMs": 142.5,
  "revisionDurationMeanMs": 38.2
}
```

## Response — 204 No Content

When no snapshot has been pushed yet (job queued or just leased, agent has not started processing).

---

## Notes

No code change required. Only `docs/control-plane.md` needs updating to add this endpoint to the Job Lifecycle table.

---

# Contract: CLI Job Submission Output

**Owner**: All migration commands that call `SubmitAsync` (`MigrationExportCommand`, `MigrationImportCommand`, `MigrationMigrateCommand`, `MigrationPrepareCommand`)  
**Status**: Must be added

---

## Output format (printed to Spectre.Console after successful `SubmitAsync`)

```
Job ID  : 550e8400-e29b-41d4-a716-446655440000
Control : http://localhost:5100
```

- Printed *before* any progress output.
- Each field on its own line with a fixed-width label (FR-013).
- Uses `console.MarkupLine` (Spectre.Console) consistent with existing command output style.
- The control plane URL is the `HttpClient.BaseAddress` used by `ControlPlaneClient`.

---

# Contract: TUI Command Flags

**Owner**: `TuiCommand` / `TuiCommandSettings`  
**Status**: `--url` inherited; `--job` must be added

## CLI signature

```
devopsmigration tui [--url <control-plane-url>] [--job <jobId>]
```

| Flag | Type | Default | Description |
|------|------|---------|-------------|
| `--url` | string | `MIGRATION_API_URL` or `http://localhost:5100` | Control plane URL |
| `--job` | string (GUID) | — | Skip job list; open detail view for this job ID |

Validation: if `--job` is provided it must parse as a valid `Guid`. Invalid value → exit code 1 with a clear error message.
