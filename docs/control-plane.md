# Control Plane

## Purpose

The control plane is an **ASP.NET Core Web API** that coordinates migration jobs without executing them. Execution always happens inside a Migration Agent. The control plane's role is to accept, validate, track, and assign work — not to perform it.

---

## Responsibilities

| Responsibility | Description |
|---|---|
| Job submission | Accept job definitions from the TUI or API clients. Validate config schema before accepting. |
| Job storage | Persist job definitions and status. |
| Lease management | Assign jobs to available Migration Agents via time-bounded leases. Reassign if a Migration Agent stops heartbeating. |
| Progress tracking | Record per-module, per-cursor, per-stage progress as reported by Migration Agents. |
| Status and logs API | Expose job status, progress, and logs to the TUI and other clients. |
| Pause / resume / cancel | Allow operators to signal state changes to Migration Agents via the job record. |
| Artefact URLs | Provide Migration Agents with the package URI (`packageUri`) for the job. |
| Secrets references | Store references to Key Vault secrets; never unwrap or proxy secrets. |

The control plane does **not** run the orchestrator, call source or target APIs, or read or write the migration package directly.

---

## API Surface

### Job Lifecycle

| Method | Path | Description |
|---|---|---|
| `POST` | `/jobs` | Submit a new job. Body is a job definition (see [docs/job-contract.md](job-contract.md)). Returns `jobId`. |
| `GET` | `/jobs/{jobId}` | Get job status and metadata. |
| `GET` | `/jobs/{jobId}/progress` | Get per-module, per-stage progress as last reported by the Migration Agent. |
| `POST` | `/jobs/{jobId}/cancel` | Cancel a running or queued job. Migration Agent will receive the signal on next heartbeat. |
| `POST` | `/jobs/{jobId}/pause` | Pause a running job. Migration Agent will checkpoint and release its lease. |
| `POST` | `/jobs/{jobId}/resume` | Resume a paused job (makes it eligible for lease pickup). |
| `GET` | `/jobs/{jobId}/logs` | Tail or fetch logs uploaded by the Migration Agent. |

### Migration Agent Protocol

| Method | Path | Description |
|---|---|---|
| `GET` | `/agents/lease` | Migration Agent polls for available work. Returns a leased job if one is available. |
| `POST` | `/agents/lease/{leaseId}/heartbeat` | Migration Agent signals it is alive. Lease expiry is extended on each heartbeat. |
| `POST` | `/agents/lease/{leaseId}/progress` | Migration Agent reports cursor position and stage for a module. |
| `POST` | `/agents/lease/{leaseId}/complete` | Migration Agent signals successful job completion. |
| `POST` | `/agents/lease/{leaseId}/fail` | Migration Agent signals non-recoverable failure with error detail. |
| `POST` | `/agents/lease/{leaseId}/release` | Migration Agent releases lease without completing (e.g. on pause). |

---

## Job States

```
Queued → Leased → Running → Completed
                          → Failed
                ↓
              Paused → Queued (resume)
                     → Cancelled
         ↑
       Cancelled (from Queued)
```

| State | Description |
|---|---|
| `Queued` | Waiting for an agent to pick up. |
| `Leased` | Assigned to an agent but not yet executing. |
| `Running` | Agent is actively executing. |
| `Paused` | Agent has checkpointed and released the lease. Job is resumable. |
| `Completed` | All modules completed successfully. |
| `Failed` | A non-recoverable error occurred. Cursor state is preserved for investigation. |
| `Cancelled` | Operator cancelled the job. |

---

## Lease Protocol

1. Migration Agent calls `GET /agents/lease` (long-poll or short-poll).
2. Control plane returns a lease containing the job definition and a `leaseId`.
3. Migration Agent sends `POST /agents/lease/{leaseId}/heartbeat` on a configurable interval (default: every 30 seconds).
4. If the control plane does not receive a heartbeat within `leaseExpiry` (default: 2× heartbeat interval), the job is returned to `Queued` and another Migration Agent may pick it up.
5. The cursor in the package ensures the new Migration Agent resumes from where the previous one stopped.

---

## Progress Reporting

Migration Agents push module progress after each cursor write:

```json
{
  "module": "WorkItems",
  "lastProcessed": "WorkItems/2026-02-25/638760123456789012-12345-17",
  "stage": "AppliedFields",
  "updatedAt": "2026-02-25T18:12:34Z"
}
```

This mirrors the cursor schema ([docs/checkpointing.md](checkpointing.md)). The control plane stores the latest value per module for status display. The cursor in the package remains the authoritative resume state.

---

## Isolation Rule

The control plane must not:

- Call source or target Azure DevOps APIs.
- Read or write the migration package.
- Execute orchestrator logic.
- Unwrap or cache secrets from Key Vault.

Violating any of these rules breaks the Migration Agent / control-plane separation and couples execution to coordination.

Violating any of these rules breaks the agent/control-plane separation and couples execution to coordination.

---

## Data Store

The control plane persists:

- Job definitions (serialised job contract)
- Job states and state transitions
- Latest progress per module (for display; not authoritative for resume)
- Lease records
- Log references (URIs into blob storage; logs themselves are stored in the package's `Logs/` folder by the Migration Agent)

### Technology

**PostgreSQL is the only permitted data store for the control plane — in all environments.**

| Environment | PostgreSQL provider |
|---|---|
| Local development | Docker container (spawned by Aspire AppHost via `AddAzurePostgresFlexibleServer().RunAsContainer()`) |
| Cloud (Azure) | Azure PostgreSQL Flexible Server (provisioned by `azd` from the same AppHost declaration) |

There is no SQLite fallback, no in-memory substitute, and no other database provider. The same `Npgsql` / EF Core stack runs in both environments, exercising identical code paths.

### ORM and Migrations

The control plane uses **EF Core 9+** with the **Npgsql.EntityFrameworkCore.PostgreSQL** provider.

- Migrations are managed with `dotnet ef migrations`.
- At startup, `dbContext.Database.MigrateAsync()` is called to apply any pending migrations before the API begins accepting requests.

### Connection String

Aspire injects the connection string under the key `ConnectionStrings__controlplane-db` in both local and cloud environments. The control plane reads it from `IConfiguration` via the standard Aspire / Npgsql integration:

```csharp
builder.AddNpgsqlDbContext<ControlPlaneDbContext>("controlplane-db");
```

The connection string value is:

| Environment | Source |
|---|---|
| Local | Aspire generates it from the Docker container endpoint and injects it automatically |
| Cloud | Azure Container Apps reads it from Key Vault via a managed identity secret reference (see [docs/aspire-integration.md](aspire-integration.md)) |

### Table Schema

```sql
-- Persists the full MigrationJob definition and tracks its lifecycle state.
CREATE TABLE jobs (
    job_id         UUID         PRIMARY KEY,
    config_version TEXT         NOT NULL,
    mode           TEXT         NOT NULL CHECK (mode IN ('Export', 'Import', 'Both')),
    state          TEXT         NOT NULL DEFAULT 'Queued'
                                CHECK (state IN ('Queued', 'Leased', 'Running', 'Paused', 'Completed', 'Failed', 'Cancelled')),
    job_json       JSONB        NOT NULL,     -- full serialised MigrationJob
    created_at     TIMESTAMPTZ  NOT NULL DEFAULT now(),
    updated_at     TIMESTAMPTZ  NOT NULL DEFAULT now()
);

-- Tracks active and historical lease assignments.
-- A job may have multiple lease rows if it is reassigned after a heartbeat timeout.
CREATE TABLE leases (
    lease_id      UUID         PRIMARY KEY,
    job_id        UUID         NOT NULL REFERENCES jobs(job_id),
    agent_id      TEXT         NOT NULL,
    acquired_at   TIMESTAMPTZ  NOT NULL DEFAULT now(),
    expires_at    TIMESTAMPTZ  NOT NULL,
    released_at   TIMESTAMPTZ              -- NULL means the lease is still active
);

-- Mirrors the latest cursor position per module, as reported by Migration Agents.
-- This is a display-only snapshot. The cursor in the package is the authoritative resume state.
CREATE TABLE progress_snapshots (
    job_id         UUID         NOT NULL REFERENCES jobs(job_id),
    module         TEXT         NOT NULL,
    last_processed TEXT         NOT NULL,   -- relative path of last processed artefact
    stage          TEXT         NOT NULL,   -- canonical stage label
    updated_at     TIMESTAMPTZ  NOT NULL,
    PRIMARY KEY (job_id, module)
);

CREATE INDEX ix_jobs_state ON jobs(state);
CREATE INDEX ix_leases_job_id ON leases(job_id);
CREATE INDEX ix_leases_expires_at ON leases(expires_at) WHERE released_at IS NULL;
```

### Lease Expiry Query

The control plane uses the `leases` table to detect stale leases:

```sql
-- Jobs whose active lease has expired (heartbeat missed)
SELECT j.job_id
FROM   jobs j
JOIN   leases l ON l.job_id = j.job_id
WHERE  j.state = 'Running'
  AND  l.released_at IS NULL
  AND  l.expires_at < now();
```

A background service runs this query on a configurable interval (default: every 10 seconds) and returns matching jobs to `Queued`.

### What Is Not Stored

The control plane deliberately does **not** store:

- The migration package contents (revision files, cursors, attachments) — those live in `IArtefactStore` (filesystem or Azure Blob).
- Log file contents — logs are written into the package's `Logs/` folder by the Migration Agent; the control plane stores only the URI prefix for the TUI to tail.
- Source or target credentials — only Key Vault references (opaque strings) are stored in `job_json`; the actual secret is never held in the database.

---

## Multi-Tenant Considerations (Phase 3)

- Each tenant's jobs are isolated by a `tenantId` claim on the JWT.
- Migration Agents may be scoped to a tenant or shared across tenants with RBAC controls.
- Rate limits are applied per tenant to prevent one tenant starving others.
- Artefact retention policies are configurable per tenant.

See [docs/architecture.md](architecture.md) for the overall system context.
