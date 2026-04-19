# Research: TUI Job Dashboard

**Feature**: 008-tui-job-dashboard  
**Phase**: 0 — Unknowns resolved

---

## Decision 1: Terminal.Gui Version

**Decision**: Use `Terminal.Gui` v2 Beta (`Version="2.0.0-beta.*"`)  
**Rationale**: The user explicitly chose v2 Beta. NuGet confirms it targets .NET 10.0 (matching the project's `net10.0` TFM), is self-described as "Recommended for new projects", and supports full `IDisposable` management — eliminating the v1 static-singleton leaks that would be problematic in the unit-test harness.  
**Alternatives considered**: v1 stable (1.19.0) — rejected because it uses static singletons, making testing harder and introducing global-state risk; v2 develop-nightly (2.0.0-develop.*) — rejected because it has daily churn and is less stable than the beta channel.

**NuGet reference**:
```xml
<PackageReference Include="Terminal.Gui" Version="2.0.0-beta.*" />
```

---

## Decision 2: Terminal.Gui v2 Application Lifecycle Pattern

**Decision**: Use the instance-based `IApplication` model with `using` statement for automatic disposal.

```csharp
using IApplication app = Application.Create().Init();
app.Run<JobListWindow>();
// Disposal automatic on using-block exit
```

**Rationale**: v2 eliminates `Application.Shutdown()` (obsolete) in favour of `IDisposable`. The `using` pattern ensures the terminal driver is always restored even on exception — critical because `TuiCommand.ExecuteInternalAsync` runs inside the Spectre.Console host which must restore the console after the TUI exits.  
**Alternatives considered**: Static `Application.Init()` / `Application.Shutdown()` (v1 pattern) — rejected; v2 marks `Shutdown()` as obsolete and the static path is not compatible with test isolation.

---

## Decision 3: Thread-Safety Model for SSE Streams → UI Updates

**Decision**: Use `Application.Invoke` (Terminal.Gui's thread-safe UI-dispatch) to marshal updates from async SSE background tasks onto the Terminal.Gui main loop thread.

**Rationale**: Terminal.Gui v2 processes input on its own main loop thread. Mutating view state from an async SSE Task running on the .NET thread pool without marshalling causes data races and phantom rendering. `Application.Invoke(Action)` queues the action for execution on the next main loop iteration — consistent with every other Terminal.Gui event-driven UI update pattern.  
**Alternatives considered**: `lock` around view mutations — rejected because Terminal.Gui's `View.SetNeedsDraw()` is not thread-safe and locks do not prevent re-entrant draw calls; `Channel<T>` consumer on main loop timer — viable but more complex than `Application.Invoke` for the scale of this feature.

---

## Decision 4: SSE Reconnect Strategy

**Decision**: Implement reconnect in a `while (!ct.IsCancellationRequested)` loop around the existing `ControlPlaneClient.FollowLogsAsync` / `StreamDiagnosticsAsync` calls, with exponential back-off starting at 1 s, doubling each attempt, capped at 30 s. The loop is driven by a per-detail-view `CancellationTokenSource` that is cancelled when the view is closed.

**Rationale**: `ControlPlaneClient` already implements the SSE parsing and `job-ended` break condition. Wrapping it in a retry loop is the minimal-change approach. The CTS on view-close ensures no orphaned tasks after navigation (FR-009 compliance).  
**Alternatives considered**: Polly retry policy — viable for production hardening but adds a dependency for a simple back-off loop; per-sink reconnect inside `ControlPlaneClient` — would change the client contract used by other commands.

---

## Decision 5: Layout — Job Detail View Panel Arrangement

**Decision**: The Job Detail view uses a `Window` with three tiled `FrameView` panels arranged vertically:
- Top half: `Metrics Panel` (left ~40%) and `Progress Log Panel` (right ~60%) side by side
- Bottom quarter: `Diagnostics Panel` full width

This matches the layout described in `docs/tui.md` (Metrics panel | Progress table | Diagnostics panel).

**Rationale**: Terminal.Gui v2 `FrameView` with `Pos`/`Dim` computed layout is the canonical approach for tiled panels. The split matches the relative informational density — progress log is the primary stream, diagnostics is secondary. Full-width diagnostics gives room for long log messages.  
**Alternatives considered**: TabView (tab per panel) — simpler but hides concurrent data behind clicks; single scrolling list mixing both streams — loses the visual separation that motivates the feature.

---

## Decision 6: `GET /jobs` — List All Jobs

**Decision**: Add `GET /jobs` endpoint to `JobsController` returning `IReadOnlyList<MigrationJob>` via `IJobStore.GetAll()`. Add `GetAllJobsAsync` method to `ControlPlaneClient`.

**Rationale**: `IJobStore.GetAll()` already exists. The `GET /jobs` endpoint is documented in `docs/control-plane.md` in the Job Lifecycle table but is not yet implemented in `JobsController`. This is the minimal addition needed to power the TUI job list.  
**Alternatives considered**: Polling `GET /jobs/{jobId}` for known IDs — not viable because the TUI must discover jobs submitted by other sessions.

---

## Decision 7: `TuiCommandSettings` — `--job` Option

**Decision**: Add a new `TuiCommandSettings` class extending `ControlPlaneBaseCommandSettings` with an optional `--job` string option (`Guid?` after parsing). Replace `TuiCommand`'s generic parameter from `ControlPlaneBaseCommandSettings` to `TuiCommandSettings`.

**Rationale**: `docs/tui.md` explicitly specifies `--job <jobId>`. Keeping it in a dedicated settings class avoids polluting `ControlPlaneBaseCommandSettings` with a TUI-specific option.

---

## Decision 8: Existing Spectre.Console Views (`DiagnosticsPanel`, `TelemetryPanel`, `TelemetryPoller`)

**Decision**: These classes remain in the `Views/` folder and continue to serve the non-TUI progress rendering (streaming follow mode in `MigrationExportCommand` etc.). New Terminal.Gui views are added *alongside* them with distinct class names (`TuiJobListView`, `TuiJobDetailView`, `TuiMetricsView`, `TuiProgressLogView`, `TuiDiagnosticsView`). No existing files are deleted or modified.

**Rationale**: The Spectre.Console panels are used by the CLI follow-mode commands which must remain working. Mixing Terminal.Gui and Spectre.Console in the same file would violate FR-001. Keeping them separate and clearly named avoids confusion. Terminal.Gui v2 widget classes must never appear inside `MigrationExportCommand` or any follow-mode command.

---

## Decision 9: `docs/control-plane.md` — Telemetry endpoint discrepancy

**Decision**: `GET /jobs/{jobId}/telemetry` is implemented in `TelemetryController.cs` and used by `TelemetryPoller.cs`. The discrepancy was only in `docs/control-plane.md` not listing it. The plan will add it to the API surface table during the implementation compliance review loop.

**Rationale**: The implementation and the spec in `docs/tui.md` are consistent with each other. Only the docs/control-plane.md API table was missing the entry. No code change needed — only a doc update.

---

## Decision 10: Job Submission CLI Output (User Story 4)

**Decision**: After `SubmitAsync` returns, every migration command (`export`, `import`, `migrate`, `prepare`) prints:

```
Job ID  : 550e8400-e29b-41d4-a716-446655440000
Control : http://localhost:5100
```

This is added in the shared location where `SubmitAsync` is called in each command. `MigrationExportCommand` is the reference implementation; the same pattern is applied to the others.

**Rationale**: Each command calls `SubmitAsync` independently. The output must appear *before* the progress stream, which is consistent with the current output flow (submit → begin streaming). A helper on `CommandBase` or `ControlPlaneCommandBase` can print this without duplicating the format string.
