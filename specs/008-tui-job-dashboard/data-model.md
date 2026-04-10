# Data Model: TUI Job Dashboard

**Feature**: 008-tui-job-dashboard  
**Phase**: 1 — Design

---

## New Entities

### `JobSummary` (new DTO — `DevOpsMigrationPlatform.Abstractions`)

Lightweight view of a job for the TUI list. Returned by `GET /jobs`.

```csharp
/// <summary>
/// Lightweight job summary returned by <c>GET /jobs</c>.
/// Used by the TUI job list view.
/// </summary>
public sealed record JobSummary(
    Guid JobId,
    string Mode,
    string State,
    string SubmittedByUpn,
    DateTimeOffset SubmittedAt
);
```

| Field | Source | Notes |
|-------|--------|-------|
| `JobId` | `MigrationJob.JobId` (parsed to `Guid`) | Full UUID |
| `Mode` | `MigrationJob.Mode` | `Export`, `Import`, `Both` |
| `State` | `IJobStore` state tracking | `Queued`, `Running`, `Completed`, etc. |
| `SubmittedByUpn` | `MigrationJob` metadata | Populated from auth context on submission |
| `SubmittedAt` | `MigrationJob` metadata | UTC timestamp |

> **Note**: `State` and `SubmittedAt` require extensions to the existing `MigrationJob` or `IJobStore`. The `MigrationJob` record does not currently carry state or timestamp. This is the primary data model gap — see the Gaps section below.

---

### `DiagnosticLogRecord` (existing — `DevOpsMigrationPlatform.Abstractions`)

Already defined. Used by `DiagnosticsPanel`, `DiagnosticsController`, `ControlPlaneClient.StreamDiagnosticsAsync`. No changes needed.

```csharp
public record DiagnosticLogRecord { Level, Timestamp, Message, ... }
```

---

### `ProgressEvent` (existing — `DevOpsMigrationPlatform.Abstractions`)

Already defined. Used by `ControlPlaneClient.FollowLogsAsync`. No changes needed.

---

### `MetricSnapshot` (existing — `DevOpsMigrationPlatform.Abstractions`)

Already defined and served by `GET /jobs/{jobId}/telemetry`. No changes needed.

---

## New View Classes (Terminal.Gui v2)

These are Terminal.Gui `View` subclasses, not domain models, but they are the structural building blocks of the TUI.

| Class | Namespace | Extends | Responsibility |
|-------|-----------|---------|---------------|
| `TuiMainView` | `DevOpsMigrationPlatform.CLI.Views` | `Window` | Single-screen Window; hosts all three panels; manages per-selection `CancellationTokenSource`; handles `TuiJobListView.JobSelected` events; starts/cancels telemetry polling and SSE tasks on selection change; `PreSelectJob(Guid)` for `--job` launch |
| `TuiJobListView` | `DevOpsMigrationPlatform.CLI.Views` | `FrameView` | `TableView` of `JobSummary`; auto-refreshes every 10 s; fires `JobSelected(Guid?)` event on row change |
| `TuiMetricsView` | `DevOpsMigrationPlatform.CLI.Views` | `FrameView` | Displays latest `MetricSnapshot`; `"(no job selected)"` placeholder when cleared; `Update(MetricSnapshot?)` marshals via `Application.Invoke` |
| `TuiLogView` | `DevOpsMigrationPlatform.CLI.Views` | `FrameView` | Toggleable log panel: **Progress mode** streams `ProgressEvent` SSE; **Diagnostics mode** streams `DiagnosticLogRecord` SSE; Tab key toggles; mode indicator in header; SSE back-off reconnect; level-coloured diagnostics; `MinLevel` filter |

---

## New Settings Class

### `TuiCommandSettings` (new — `DevOpsMigrationPlatform.CLI.Migration.Settings`)

```csharp
public sealed class TuiCommandSettings : ControlPlaneBaseCommandSettings
{
    [CommandOption("--job")]
    [Description("Jump directly to the detail view for this Job ID, bypassing the job list.")]
    public string? Job { get; init; }
}
```

---

## New Client Method

`ControlPlaneClient` gains one new method:

```csharp
/// <summary>Returns all jobs visible to the caller via <c>GET /jobs</c>.</summary>
public Task<IReadOnlyList<JobSummary>> GetAllJobsAsync(CancellationToken ct);
```

---

## New Interface: `IControlPlaneClient` (`DevOpsMigrationPlatform.Abstractions`)

Enables TUI view classes to be unit-tested with a fake/stub without a real HTTP server.

```csharp
/// <summary>
/// Abstraction over the control-plane HTTP client.
/// Declared in Abstractions so TUI view unit tests can inject a fake.
/// </summary>
public interface IControlPlaneClient
{
    Task<IReadOnlyList<JobSummary>> GetAllJobsAsync(CancellationToken ct);
    IAsyncEnumerable<ProgressEvent> FollowLogsAsync(Guid jobId, CancellationToken ct);
    IAsyncEnumerable<DiagnosticLogRecord> StreamDiagnosticsAsync(Guid jobId, CancellationToken ct);
    Task<MetricSnapshot?> GetTelemetryAsync(Guid jobId, CancellationToken ct);
}
```

`ControlPlaneClient` in `DevOpsMigrationPlatform.CLI.Migration` implements this interface. All TUI view constructors accept `IControlPlaneClient`.

---

## Data Model Gaps (require implementation changes)

### Gap 1: `MigrationJob` lacks `State`, `SubmittedAt`, `SubmittedByUpn`

**Current state**: `MigrationJob` is the job contract record. `IJobStore.GetAll()` returns a list of `MigrationJob` but carries no runtime state, no submission timestamp, and no submitter identity.

**Required change**: Either:
- Introduce a `JobRecord` wrapper in the control plane that pairs a `MigrationJob` with `State`, `SubmittedAt`, `SubmittedByUpn` (stored in `JobStore`); or
- Extend `MigrationJob` with those fields (config-version-safe, set on submission by the control plane).

**Recommended approach**: `JobRecord` internal to the control plane. `GET /jobs` projects `JobRecord → JobSummary`. `GET /jobs/{jobId}` continues to return the full `MigrationJob` (or the full `JobRecord`). This avoids polluting the job contract with control-plane runtime state.

### Gap 2: `docs/control-plane.md` missing `GET /jobs/{jobId}/telemetry` in API table

**Required change**: Add the telemetry endpoint to the API surface table. Implementation already exists in `TelemetryController.cs`. Doc update only.

---

## State Transitions (displayed in TUI)

The TUI renders state from the control plane. The state machine is defined in `docs/control-plane.md`:

```
Queued → Leased → Running → Completed
                           → Failed
                 ↓
               Paused → Queued (resume)
                      → Cancelled
         ↑
       Cancelled (from Queued)
```

TUI colour coding (Terminal.Gui v2 `Scheme`):
| State | Colour |
|-------|--------|
| Queued | Yellow |
| Leased | Yellow |
| Running | Green |
| Paused | Blue |
| Completed | White (bold) |
| Failed | Red |
| Cancelled | Grey |
