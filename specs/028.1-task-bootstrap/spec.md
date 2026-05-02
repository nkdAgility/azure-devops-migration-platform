# Feature Specification: Job Execution Plan Bootstrap

**Feature Branch**: `028.1-task-bootstrap`
**Created**: 2026-05-01
**Status**: Draft
**Input**: When a client (CLI or TUI) connects to the Control Plane, the bootstrap response must include a list of tasks the agent is going to execute, with their current status and any totals already known. The agent computes this plan immediately after reading `migration-config.json` and pushes it to the Control Plane before executing any module. Status updates reference the task they apply to.

---

## Architecture References

| Document | Status |
| -------- | ------ |
| `docs/architecture.md` | Confirmed — no conflicts |
| `docs/control-plane.md` | **Has gap** — does not describe task-list push endpoint or `JobBootstrap.Tasks` field |
| `docs/orchestration.md` | **Has gap** — does not describe `IJobExecutionPlanBuilder` or task-attribution in `ProgressEvent` |
| `.agents/context/job-contract.md` | Confirmed — `ConfigPayload` carries all config; `JobKind` enum defines the six job types |
| `.agents/guardrails/system-architecture.md` | Confirmed — rules 21 (mandatory reuse), 25 (observability) apply |
| `agents.md` | Confirmed — binding entry point read |

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Operator Sees the Execution Plan Before Any Work Begins (Priority: P1)

A platform operator submits a job with `devopsmigration queue` and immediately connects the TUI. Before any module has executed, the bootstrap response shows a table of tasks the agent has planned — for example, `Inventory`, `Identities/Export`, `Nodes/Export`, `Teams/Export`, `WorkItems/Export` — each with a `Pending` status and, where the agent can determine it without extra work, the total count to be processed.

**Why this priority**: Without a task plan, the TUI progress display is blank until the first module starts emitting events. Operators have no way to verify the job is configured correctly or understand its scope. An upfront task list provides immediate confirmation that the correct modules are enabled and gives the operator a structural view of what will happen.

**Independent Test**: Submit a Simulated Export job and call `GET /jobs/{id}/bootstrap` before the agent begins any module. The response `tasks` array must contain at least one entry per enabled module with `status: Pending`.

**Acceptance Scenarios**:

1. **Given** a job is queued and the agent has leased it but not yet executed any module, **When** a client calls `GET /jobs/{jobId}/bootstrap`, **Then** the response includes a `tasks` array with one entry per enabled module/phase, all with `status: Pending` and correct `order` values.
2. **Given** a job with `kind: Migrate` and WorkItems enabled, **When** the bootstrap is retrieved, **Then** the `tasks` array contains separate entries for `WorkItems/Export` and `WorkItems/Import` — not a single `WorkItems` entry.
3. **Given** a job with `kind: Export`, **When** the bootstrap is retrieved, **Then** the `tasks` array contains `Inventory` as the first task (order 0), followed by one Export-phase task per enabled module.
4. **Given** an inventory artefact already exists in the package from a prior run, **When** the agent builds the execution plan, **Then** the `Inventory` task has `status: Skipped`, `skipReason: "Completed in prior run"`, and `knownTotal` populated from the existing inventory data.
5. **Given** a Migrate job where the Export phase completed in a prior run, **When** the agent builds the execution plan on resume, **Then** all Export-phase tasks have `status: Skipped` with `skipReason: "Completed in prior run"` and only Import-phase tasks have `status: Pending`.

---

### User Story 2 — Status Updates Are Attributed to Specific Tasks (Priority: P1)

A TUI user watching a live migration run can see, for each progress event, which task it belongs to. The Metrics panel and Progress panel both reference the same task identifiers, so the user can see "WorkItems/Export — 1,450 / 3,200 revisions" and know exactly where in the execution plan the agent is.

**Why this priority**: The current `ProgressEvent.Module` + `Stage` fields identify what the agent is doing, but they do not link back to the task plan. Without a `TaskId`, the TUI cannot drive task-state transitions (Pending → Running → Completed) from the event stream — it must use fragile string matching against `Module`.

**Independent Test**: Run a Simulated Export job and collect `ProgressEvent` records via `GET /jobs/{id}/progress`. Every event whose `module` is non-empty must carry a `taskId` that matches a `JobTask.id` in the task list returned by bootstrap.

**Acceptance Scenarios**:

1. **Given** a module begins execution, **When** the agent emits a `ProgressEvent` for that module's start stage, **Then** the event carries `taskId` matching the corresponding `JobTask.id` and `taskStatus: Running`.
2. **Given** a module completes successfully, **When** the agent emits the completion `ProgressEvent`, **Then** the event carries `taskId`, `taskStatus: Completed`, and `completedCount` with the actual items processed.
3. **Given** a module fails, **When** the agent emits the failure `ProgressEvent`, **Then** the event carries `taskId` and `taskStatus: Failed`.
4. **Given** a client subscribes to `GET /jobs/{id}/progress?follow=true` mid-stream, **When** a `ProgressEvent` with `taskId` and `taskStatus: Running` arrives, **Then** the client can update its local task table to show that task as Running without polling bootstrap again.
5. **Given** the Control Plane receives a `ProgressEvent` with `taskId` and `taskStatus: Completed`, **When** a client calls `GET /jobs/{id}/bootstrap` afterwards, **Then** the corresponding `JobTask` has `status: Completed` and `completedAt` set.

---

### User Story 3 — Late-Joining Clients Receive Complete Task State (Priority: P2)

A developer attaches the TUI to a job that is already 60% complete. The bootstrap response reflects the current state of every task — some `Completed`, one `Running`, the rest `Pending` — rather than showing everything as `Pending` as it was at job start. The developer immediately understands the job's progress without waiting for the next event.

**Why this priority**: The atomic bootstrap pattern (snapshot + metrics + lastEventSequence) already solves the late-join problem for counters and org/project state. This story extends the same guarantee to the task plan.

**Acceptance Scenarios**:

1. **Given** a job where two of five tasks are `Completed` and one is `Running`, **When** a new client calls `GET /jobs/{id}/bootstrap`, **Then** the `tasks` array reflects the current live state — two `Completed`, one `Running`, two `Pending`.
2. **Given** `GET /jobs/{id}/bootstrap` returns tasks in `Completed` state with `completedAt` timestamps, **When** a client renders the task table, **Then** it can show "Completed at HH:mm:ss" without querying any additional endpoint.

---

## Edge Cases

- **No enabled modules**: If a job's config has all modules disabled, the task list contains only the `Inventory` task (for Export/Migrate) or is empty (for Import-only). The agent must emit a `Warning` log for each disabled-but-expected module.
- **Inventory already exists but is stale**: The agent uses the existing inventory totals as `knownTotal` regardless of staleness — the task is still skipped. Freshness checks are out of scope.
- **Task list pushed after module starts**: If the agent crashes and restarts between pushing the task list and starting the first module, the task list is re-pushed on restart. The control plane upserts (replaces) the task list — idempotent.
- **Unknown task ID in ProgressEvent**: The control plane must tolerate a `taskId` in a `ProgressEvent` that does not match any stored `JobTask` — log a `Warning` and skip the state update; do not fail the event ingestion.
- **TFS subprocess (net481)**: The TFS export agent emits `ProgressEvent` records via stdout NDJSON. `taskId` and `taskStatus` must be included in these events. The `IJobExecutionPlanBuilder` interface is defined in `Abstractions.Agent` and is multi-targeted, so it is accessible from net481.

---

## Observability

This feature introduces two new observable operations: **execution plan push** (agent → control plane at job start) and **task state derivation** (control plane on every `ProgressEvent` ingestion).

### Operations

| Name | Type | Entry Point | Observable Boundary |
| ---- | ---- | ----------- | ------------------- |
| `job.plan.push` | agent startup | `IControlPlaneTelemetryClient.PushTaskListAsync` | Single HTTP POST; structured log at `Information`; non-zero task count |
| `job.task.transition` | control plane | `ProgressController.PostProgress` | Structured log at `Debug` per state change; no span required |

### O-1 Traces

- `job.plan.push`: No new span. The plan push happens inside the existing job-startup Activity. Tag `job.task_count` on the existing span.
- `job.task.transition`: No span. State derivation is a synchronous in-memory operation inside the existing progress-ingestion request.

### O-2 Metrics

No new metric instruments. Task counts are reported via structured logs and the task list endpoint — not via OTel counters.

### O-3 Structured Logging

| Event | Level | Fields |
| ----- | ----- | ------ |
| Task list pushed to control plane | `Information` | `jobId`, `taskCount`, task `id` list |
| Task state transition | `Debug` | `jobId`, `taskId`, `oldStatus`, `newStatus` |
| Unknown `taskId` in ProgressEvent | `Warning` | `jobId`, `taskId`, `module` |
| Inventory totals carried forward | `Information` | `jobId`, `knownTotal` |

### O-4 Progress Events

Not applicable — this feature is infrastructure plumbing, not a migration module. No `IProgressSink` changes beyond adding `TaskId?` and `TaskStatus?` fields to `ProgressEvent`.

---

## Connector Coverage

**CONNECTOR COVERAGE CHECK: N/A for module operations.**

This feature adds no new export or import capability. The `IJobExecutionPlanBuilder` must produce correct task lists for all three connectors, but the logic is config-driven (reads `IConfiguration` keys for enabled modules) — no connector-specific code is required beyond emitting `TaskId` in `ProgressEvent` records.

| Concern | Simulated | AzureDevOps | TFS |
|---------|-----------|-------------|-----|
| Correct task list for Export job | Required | Required | Required (source-only; no Import tasks) |
| `taskId` in `ProgressEvent` | Required | Required | Required (net481 subprocess events) |

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The agent MUST build an execution plan (`JobTaskList`) immediately after reading `migration-config.json` and before creating the per-job DI scope or executing any module.
- **FR-002**: The execution plan MUST contain one `JobTask` per enabled module per applicable phase. For `Export` jobs: one task per enabled module at phase `Export`, preceded by an `Inventory` task at order 0. For `Import` jobs: one task per enabled module at phase `Import`. For `Migrate` jobs: `Inventory` + Export-phase tasks + Import-phase tasks. Module order follows DI registration order: Identities → Nodes → Teams → WorkItems.
- **FR-003**: The agent MUST push the `JobTaskList` to the control plane via `IControlPlaneTelemetryClient.PushTaskListAsync` before emitting any `ProgressEvent`.
- **FR-004**: `ProgressEvent` MUST gain two nullable fields: `TaskId: string?` (the `JobTask.Id` of the task being updated) and `TaskStatus: JobTaskStatus?` (the new status). Both are nullable for backward compatibility — events not tied to a task transition carry `null` for both fields.
- **FR-005**: Every `ProgressEvent` emitted at the start of a module execution MUST carry `TaskId` and `TaskStatus: Running`. Every event at module completion MUST carry `TaskId`, `TaskStatus: Completed`, and `CompletedCount`. Every event at module failure MUST carry `TaskId` and `TaskStatus: Failed`.
- **FR-006**: The control plane MUST persist the `JobTaskList` and update individual `JobTask` statuses when `ProgressEvent` records with `TaskId` + `TaskStatus` arrive via `POST /agents/lease/{leaseId}/progress`.
- **FR-007**: `GET /jobs/{jobId}/bootstrap` MUST include `Tasks: JobTaskList?` in the response. Null when no task list has been pushed yet.
- **FR-008**: The `JobTask.KnownTotal` field MUST be populated at plan-build time when the information is available without executing work — specifically: if `inventory.json` exists at the package root, the `Inventory` task MUST be marked `Skipped` with `SkipReason: "Completed in prior run"` and downstream tasks MUST have `KnownTotal` populated from the inventory data. If inventory does not exist, `KnownTotal` is null.
- **FR-009**: Resume awareness: tasks already completed in a prior run MUST be marked `Skipped` with a non-empty `SkipReason` at plan-build time. The agent reads `IPhaseTrackingService` to determine which phases completed.
- **FR-010**: The TFS agent (net481) MUST include `TaskId` and `TaskStatus` in every `ProgressEvent` it emits via stdout NDJSON. The `IJobExecutionPlanBuilder` interface MUST be accessible from net481 (defined in `Abstractions.Agent` which is multi-targeted).
- **FR-011**: The control plane MUST tolerate a `ProgressEvent` whose `TaskId` does not match any stored `JobTask` — log a `Warning` and skip the state update without failing the request.
- **FR-012**: The plan push MUST be idempotent — if the agent re-pushes on restart, the control plane replaces the stored task list with the new one.

### Key Entities

- **`JobTask`**: A record representing one unit of planned work. Fields: `Id` (e.g. `"WorkItems/Export"`), `Name` (display string), `Phase` (e.g. `"Export"`, `"Import"`, or null for cross-cutting tasks like `Inventory`), `Order` (int, execution sequence), `Status` (`JobTaskStatus`), `KnownTotal: long?`, `CompletedCount: long?`, `StartedAt: DateTimeOffset?`, `CompletedAt: DateTimeOffset?`, `SkipReason: string?`.
- **`JobTaskStatus`**: Enum — `Pending`, `Running`, `Completed`, `Failed`, `Skipped`.
- **`JobTaskList`**: A record containing `Tasks: IReadOnlyList<JobTask>` and `PushedAt: DateTimeOffset`.
- **`IJobExecutionPlanBuilder`**: Agent-side service (in `Abstractions.Agent`) that reads `IConfiguration` (PackageConfig) + `JobKind` + resume state + inventory artefact existence to produce a `JobTaskList`. Implementation in `Infrastructure.Agent`.
- **`IJobTaskStore`**: Control-plane-side store interface. Methods: `Upsert(jobId, taskList)`, `UpdateTask(jobId, taskId, status, startedAt, completedAt, completedCount)`, `Get(jobId) : JobTaskList?`.

---

## Success Criteria *(mandatory)*

- **SC-001**: `GET /jobs/{jobId}/bootstrap` for a queued-but-not-started job returns `tasks` with at least one entry per enabled module, all `Pending`.
- **SC-002**: Every `ProgressEvent` for a module start/complete/fail carries a `taskId` that matches a `JobTask.id` in the bootstrap task list.
- **SC-003**: After a module completes, `GET /jobs/{jobId}/bootstrap` returns that task as `Completed` with `completedAt` set.
- **SC-004**: For a resumed job where Export previously completed, all Export-phase tasks are `Skipped` in the bootstrap response.
- **SC-005**: If `inventory.json` exists, the `Inventory` task is `Skipped` and `knownTotal` on downstream tasks is populated from inventory data.
- **SC-006**: All existing tests pass after adding `TaskId?` + `TaskStatus?` to `ProgressEvent` — nullable fields are additive and do not break existing serialisation.
- **SC-007**: The TFS agent's NDJSON progress events include `taskId` and `taskStatus` fields when a module lifecycle transition occurs.
