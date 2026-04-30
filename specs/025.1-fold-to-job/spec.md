# Feature Specification: Fold Everything Into a Single Job Object

**Feature Branch**: `job-change`
**Created**: 2026-04-30
**Status**: Implemented (retrospective)
**Predecessor**: `025-agent-config-package` — established config-in-package; this branch consolidates the resulting changes into a clean, unified `Job` dispatch token and hardens all surrounding infrastructure.

---

## Problem Statement

After `025-agent-config-package` was merged, several related problems remained:

1. **Job payload inconsistency** — `MigrationJob` and `DiscoveryJob` were separate types with duplicated fields. The control plane accepted both but the agent had to switch on type rather than a discriminator field. Adding a new job kind required changes in at least five places.

2. **Config never reached the agent** — The CLI submitted a `MigrationJob` containing source/target/module config inline. The agent received it but the per-job DI scope was never populated from the job payload. Modules ran with empty options.

3. **FR-007 (re-submission guard) was on the wrong side** — The CLI was checking for an existing `migration-config.json` before submission. The CLI cannot reliably see the package when running in Hosted mode (the package lives on the agent machine). The check had to move to the agent.

4. **Test isolation was absent** — All system tests pointed at the same `storage/<scenario-name>` directory. Parallel test runs created SQLite lock conflicts and tests were non-reproducibly flaky.

5. **Race condition: `progress.jsonl` never written** — The agent called `SignalTerminalAsync("complete")`, which caused the CLI to kill the subprocess. `OnPostJobFlushAsync` (the base-class flush) ran _after_ signal, so async-batched sinks (`PackageProgressSink`) were never flushed. `progress.jsonl` was missing from the package.

6. **ControlPlaneHost startup race** — `ManageProgressCommandTests` and `ManageDiagnosticsCommandTests` run in parallel across test classes. Both called `FindOrStartAsync`, both saw port 5101 free, both tried to bind it. One crashed with `-2147450749` (`COR_E_APPMODEL_ERROR`).

---

## Confirmed Design

### Single `Job` class

```
Job  (DevOpsMigrationPlatform.Abstractions.Jobs)
  ├── JobId          : string          (UUID v4, CLI-assigned)
  ├── ConfigVersion  : string          ("2.0")
  ├── Kind           : JobKind         (Export | Import | Migrate | Prepare | Inventory | Dependencies)
  ├── Connectors     : ConnectorType[] ([AzureDevOps], [TeamFoundationServer], [] = Simulated)
  ├── Package        : JobPackage      (packageUri, createPackage)
  ├── Diagnostics    : JobDiagnostics? (minimumLevel)
  ├── Resume         : JobResume?      (Auto | ForceFresh)
  └── ConfigPayload  : string?         (raw JSON of migration-config.json, written by CLI)
```

`MigrationJob` and `DiscoveryJob` are **eliminated**. All job kinds travel as `Job`. The `Kind` discriminator replaces the class hierarchy. The control plane stores `Job` directly; agents switch on `job.Kind` in a single dispatch method.

### Config Flow (confirmed)

```
1. CLI reads migration.json
2. CLI serialises full config → Job.ConfigPayload (raw JSON string)
3. CLI submits Job to ControlPlane
4. ControlPlane stores job, assigns lease to Agent
5. Agent receives Job via lease
6. Agent writes Job.ConfigPayload → migration-config.json in package     ← FR-007 guard here
7. Agent builds per-job IConfiguration from migration-config.json
8. Agent builds per-job IOptions<T> DI scope from IConfiguration
9. Agent executes modules with correct config
```

`Job.ConfigPayload` is the carrier. The CLI sets it; the agent materialises it to disk. The control plane is opaque to the config contents.

---

## Functional Requirements

| ID | Requirement |
|----|-------------|
| FR-001 | A single `Job` class replaces `MigrationJob` and `DiscoveryJob`. All job kinds use the same wire format. |
| FR-002 | `Job.Kind` (enum `JobKind`) is the dispatch discriminator. Values: `Export`, `Import`, `Migrate`, `Prepare`, `Inventory`, `Dependencies`. |
| FR-003 | `Job.Connectors` (array of `ConnectorType`) replaces the per-type `sourceType` string. The control plane matches capabilities using this array. |
| FR-004 | `Job.ConfigPayload` carries the raw JSON of `migration-config.json`. The CLI reads the scenario config and serialises it into this field before submission. |
| FR-005 | The agent writes `Job.ConfigPayload` to `migration-config.json` at the package root before any module reads config. |
| FR-006 | After writing config, the agent builds an `IConfiguration` from the file and constructs per-job `IOptions<T>` instances in a scoped DI container. Modules receive correctly populated options. |
| FR-007 | If `migration-config.json` already exists in the package and `Job.Resume.Mode != ForceFresh`, the agent throws `InvalidOperationException` and signals `fail`. The CLI cannot perform this check reliably in Hosted mode. |
| FR-008 | The flush of all `IFlushable` sinks (`PackageProgressSink`, `PackageLoggerProvider`) MUST complete before `SignalTerminalAsync` is called in any terminal path of any agent worker (`JobAgentWorker`, `TfsJobAgentWorker`). |
| FR-009 | Each system test that invokes a scenario must pass `DEVOPS_MIGRATION_TEST_STORAGE=storage/<TestMethodName>` as an environment variable so each test gets an isolated working directory. |
| FR-010 | All scenario files use `"WorkingDirectory": "%DEVOPS_MIGRATION_TEST_STORAGE%"`. The CLI expands this before job construction. |
| FR-011 | `ControlPlaneHostRunner.FindOrStartAsync` must serialise startup with a `static SemaphoreSlim` so parallel test classes cannot both try to bind port 5101 simultaneously. |

---

## Non-Functional Requirements

| ID | Requirement |
|----|-------------|
| NFR-001 | `Job` is JSON-serialisable with `System.Text.Json` defaults. No custom converters needed — all fields are primitive types, enums, or flat records. |
| NFR-002 | The `Job` wire format is stable at `configVersion: "2.0"`. Removing or renaming fields requires a version bump and an upgrader. |
| NFR-003 | Test storage directories are isolated per test method. No two tests share a SQLite file. |
| NFR-004 | `progress.jsonl` and `agent.jsonl` are present in the package after every successful export, including simulated tests. |

---

## Acceptance Scenarios

### Scenario 1 — Single Job type dispatched correctly

**Given** the CLI submits an Export job and an Inventory job in sequence,
**When** the agent worker processes each,
**Then** `job.Kind == JobKind.Export` routes to `OnMigrationJobAsync` and `job.Kind == JobKind.Inventory` routes to `OnDiscoveryJobAsync` with no type-switch errors.

### Scenario 2 — Config payload reaches modules

**Given** a scenario config containing at least one `FieldTransform` rule,
**When** a simulated export job runs end-to-end,
**Then** `migration-config.json` exists at the package root AND the exported `revision.json` files reflect the configured transform.

### Scenario 3 — FR-007 re-submission guard on agent side

**Given** a completed export package (containing `migration-config.json`),
**When** the CLI re-submits without `--force-fresh`,
**Then** the agent fails the job with a message containing `"Use --force-fresh"` and `progress.jsonl` records the failure event.

### Scenario 4 — `progress.jsonl` present after simulated export

**Given** a simulated export is run via `./build.ps1 -Mode SystemTest_Simulated`,
**When** the test `QueueExportSimulated_ProducesBothLogFiles` runs,
**Then** both `agent.jsonl` AND `progress.jsonl` exist in the test's isolated storage directory with non-zero byte count.

### Scenario 5 — Test isolation: no SQLite lock collisions

**Given** all simulated system tests run in parallel (`[DoNotParallelize]` at class level only),
**When** the full simulated suite runs,
**Then** no test fails with `SQLite database is locked` or `Could not delete directory`.

### Scenario 6 — ControlPlaneHost startup race resolved

**Given** `ManageProgressCommandTests` and `ManageDiagnosticsCommandTests` run concurrently,
**When** neither has a running ControlPlane at the start,
**Then** exactly one instance starts, and the second test class reuses it without error.

---

## Implementation Summary (what was built)

### `Job` class consolidation (`DevOpsMigrationPlatform.Abstractions/Jobs/Job.cs`)

- `MigrationJob` and `DiscoveryJob` deleted; `Job` is the single type.
- Added `Kind : JobKind`, `Connectors : ConnectorType[]`, `ConfigPayload : string?`.
- Removed inline `source`, `target`, `modules`, `policies` fields (moved to `migration-config.json` in feature 025).

### CLI job construction (`QueueCommand`, `MigrationExportCommand`, etc.)

- Reads scenario config into `MigrationOptions`.
- Re-serialises full config JSON into `job.ConfigPayload`.
- Sets `job.Kind` from command mode, `job.Connectors` from `config.Source.Type`.
- Removed pre-submission FR-007 check (moved to agent).

### Agent config materialisation (`JobAgentWorker.WriteConfigPayloadAsync`)

- Writes `job.ConfigPayload` → `migration-config.json` before any module reads config.
- FR-007 guard: if file exists and `Resume.Mode != ForceFresh`, throws and signals `fail`.

### Agent dispatch (`JobAgentWorker.OnJobAsync`)

```csharp
switch (job.Kind)
{
    case JobKind.Export:
    case JobKind.Import:
    case JobKind.Migrate:
    case JobKind.Prepare:
        await OnMigrationJobAsync(job, controlPlane, leaseId, ct);
        break;
    case JobKind.Inventory:
    case JobKind.Dependencies:
        await OnDiscoveryJobAsync(job, controlPlane, leaseId, ct);
        break;
}
```

### Flush-before-signal fix (`JobAgentWorker`, `TfsJobAgentWorker`)

Added in every terminal path before `SignalTerminalAsync`:

```csharp
foreach (var flushable in _flushables)
    await flushable.FlushAsync().ConfigureAwait(false);
await SignalTerminalAsync(controlPlane, leaseId, terminal, ct);
```

Ensures `progress.jsonl` is written before the CLI kills the subprocess.

### Test isolation (`scenarios/*.json`, all system test classes)

- All 14 scenario files: `"WorkingDirectory": "%DEVOPS_MIGRATION_TEST_STORAGE%"`.
- All system tests: `env: { ["DEVOPS_MIGRATION_TEST_STORAGE"] = Path.Combine("storage", nameof(TestMethod)) }`.
- VS Code launch.json profiles: `"DEVOPS_MIGRATION_TEST_STORAGE": "storage/<scenario-name>"`.

### ControlPlaneHostRunner startup serialisation

```csharp
private static readonly SemaphoreSlim _startLock = new(1, 1);

// double-checked lock: fast path → check IsReadyAsync → lock → re-check → start
await _startLock.WaitAsync(ct);
try { ... }
finally { _startLock.Release(); }
```

### Build tooling (`build.ps1`)

- `Stats` mode: `.\build.ps1 -Mode Stats` — reads existing `.trx` files and outputs test summary + 10 slowest tests, no build or GitVersion needed.
- Build timings persisted to `TestResults/build-timings.json` after every `Write-BuildSummary` call; `Stats` mode renders them.
- Empty `.trx` files (0-test environment records) suppressed from summary.

---

## Files Changed (key)

| File | Change |
|------|--------|
| `src/DevOpsMigrationPlatform.Abstractions/Jobs/Job.cs` | Unified job type |
| `src/DevOpsMigrationPlatform.Abstractions/Jobs/JobKind.cs` | Enum covering all job kinds |
| `src/DevOpsMigrationPlatform.Abstractions/Jobs/ConnectorType.cs` | Replaces sourceType string |
| `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` | Dispatch + config write + FR-007 + flush-before-signal |
| `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs` | Flush-before-signal |
| `src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs` | Job construction, ConfigPayload |
| `scenarios/*.json` (14 files) | `%DEVOPS_MIGRATION_TEST_STORAGE%` |
| `tests/**/Commands/*.cs` (all system tests) | Per-test isolated storage env var |
| `tests/**/TestUtilities/ControlPlaneHostRunner.cs` | Startup semaphore |
| `build.ps1` | `Stats` mode, build timings persistence, 10-slowest output |

---

## Definition of Done Checklist

- [x] `dotnet clean && dotnet build --no-incremental` passes with 0 errors
- [x] `dotnet test --filter "TestCategory=SystemTest_Simulated"` — 11/11 pass
- [x] `progress.jsonl` present in package after simulated export
- [x] No SQLite lock collisions across parallel simulated tests
- [x] ControlPlaneHost startup race does not occur when Progress and Diagnostics tests run concurrently
- [x] `.\build.ps1 -Mode Stats` outputs test summary and timings without invoking GitVersion
