# Definition of Done

Every unit of work (feature, task, bugfix, refactor) must satisfy **all** of the following criteria before it can be declared complete. There are zero exceptions.

---

## 1. Build

- `dotnet clean && dotnet build --no-incremental` succeeds with **0 errors and 0 warnings treated as errors**.
- Both Debug and Release configurations must build clean.
- The `build.ps1 install` script must complete without error. 

## 2. Tests — All Green, No Exceptions

| Rule | Detail |
|------|--------|
| **All tests run** | Every test in the solution is executed. No test may be excluded from the run. |
| **All tests pass** | Exit code 0. Zero failures, zero errors. |
| **No `Assert.Inconclusive`** | `Assert.Inconclusive()` is treated as a **build-breaking error**. Every test must assert a real outcome. The fix is always to **implement the assertion**. A test may only be removed if it is genuinely invalid (e.g. tests a deleted feature or contradicts the spec) — never to avoid implementation work. Only a human may decide to remove a test. |
| **No `@ignore` tag** | Gherkin `@ignore` tags are forbidden in committed code. They may be used **temporarily within a single editing session** to isolate a problem, but must be removed before the work is declared done. |
| **No `[Ignore]` attribute** | MSTest `[Ignore]` attributes are forbidden in committed code. Same temporary-use-only rule as `@ignore`. |
| **No `throw new NotImplementedException()`** | Stubs are permitted only within a single session. Every reachable code path must have a real implementation before done. |
| **No hanging tests** | Every test must complete within a reasonable time. Infinite loops, unbounded waits, and clock-racing conditions (e.g. comparing against a live `DateTime.UtcNow` in a tight loop) are bugs. |

### Temporary Isolation (Session-Only)

During active development, you **may** temporarily use `@ignore`, `[Ignore]`, or `Assert.Inconclusive` to isolate a subset of tests while debugging. This is a valid workflow technique. However:

- These markers must be removed before the session ends.
- Code containing these markers must never be committed.
- A build that contains any of these markers is **not done**.

## 3. Observability — All Four Requirements Verified ⛔ MANDATORY

Every module and tool must pass all four observability requirements before done. This gate has **zero exceptions**. Each requirement must be verified by inspection of the actual code — "it compiles" is not verification.

| Requirement | Check | Verification Method |
|-------------|-------|---------------------|
| **O-1 Traces** | Every export/import/validate/per-item operation has a `using var activity = ActivitySource.StartActivity(...)` with meaningful tags | Grep changed files for `StartActivity`; confirm present in every method that performs I/O or iteration |
| **O-2 Metrics** | `IMigrationMetrics` called for attempt, completion, error, duration, and in-flight at every operation boundary | Grep changed files for `_metrics?.Record`; confirm 5 call sites per operation boundary |
| **O-3 Logs** | `Information` at start/end with counts; `Warning` for skips/errors; `Debug` for per-item detail; structured params only — no string interpolation | Grep for `LogInformation`, `LogWarning`, `LogDebug` at correct call sites; grep for `$"` in log calls (reject if found) |
| **O-4 ProgressEvents** | `IProgressSink` injected as optional; `EmitAsync` CALLED (not just injected) at start, per item (or per ≤50 batch), and completion; `Metrics.Migration.{ModuleName}` populated with `ModuleCounters` on completion event | Grep for `EmitAsync`; confirm called in 3+ places; confirm completion event populates `Metrics` property |
| **O-4 CLI Visible** | Progress bar row for this module appears in `QueueCommand.BuildProgressRenderable` in correct execution order | Open `QueueCommand.cs` and confirm row exists; confirm it is visible when module counter is non-null |

**FAIL** if any row above is not satisfied. A functional module that is invisible in the CLI is **not done**.

### End-to-End Pipeline Wiring Check ⛔ MANDATORY

Before declaring done, trace the complete data path from module code to CLI/TUI display for every new or modified module. There are two parallel paths — both must be intact:

```
Agent: Module/Tool
  ├─► IMigrationMetrics → OTel → SnapshotMetricExporter → JobMetrics DTO
  │       └─► POST /agents/lease/{id}/telemetry [every ~5s]
  │               └─► ControlPlane stores snapshot
  │                       ├─► CLI polls GET /jobs/{id}/telemetry   → counters in BuildProgressRenderable
  │                       └─► TUI polls GET /jobs/{jobId}/telemetry → Metrics panel
  │
  └─► IProgressSink.EmitAsync → ControlPlaneProgressSink
          └─► POST /agents/lease/{id}/progress
                  └─► ControlPlane SSE fan-out
                          ├─► CLI subscribes GET /jobs/{id}/progress?follow=true  → stage/cursor rows
                          └─► TUI subscribes GET /jobs/{jobId}/progress?follow=true → Progress table
```

Produce a wiring table and confirm every link is ✅:

| Link | Class/Method | Status |
|------|-------------|--------|
| IMigrationMetrics called | Module.OperationAsync | ✅ / ❌ |
| SnapshotMetricExporter maps counter | SnapshotMetricExporter.cs case for `[metric-name]` | ✅ / ❌ |
| Counter DTO property exists | MigrationCounters.[PropertyName] | ✅ / ❌ |
| IProgressSink.EmitAsync called | Module.OperationAsync | ✅ / ❌ |
| CLI reads counters from telemetry endpoint | QueueCommand polls GET /jobs/{id}/telemetry | ✅ / ❌ |
| CLI reads stages from SSE | QueueCommand subscribes GET /jobs/{id}/progress?follow=true | ✅ / ❌ |
| BuildProgressRenderable row exists | QueueCommand.BuildProgressRenderable | ✅ / ❌ |
| TUI Metrics panel renders counter | TUI polls GET /jobs/{jobId}/telemetry | ✅ / ❌ |

**FAIL** conditions: any link missing; any counter read from `ProgressEvent.Metrics` in CLI/TUI code (null for .NET 10 agents — silently displays zeros); any direct `IProgressSink` wiring in CLI or TUI code.

## 3a. DI Wiring — All Services Registered ⛔ MANDATORY

- Every new class implementing an interface has a corresponding `services.Add*<IFoo, Foo>()` registration in a `ServiceCollectionExtensions` method.
- That extension method is called from the host startup (not buried or orphaned).
- No class is instantiated via `new` inside module or service code — constructor injection only.
- Verified by running a scenario config end-to-end — any missing registration will throw `InvalidOperationException` at startup.

## 4. Scenario Execution

- At least one scenario configuration (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) must be executed via a `.vscode/launch.json` debug profile.
- The run must complete without errors and produce expected observable output.
- All enabled module progress bars must be visible in the CLI output.

## 5. Code Quality

- No `throw new NotSupportedException("... not yet implemented")` in reachable code paths.
- No `.Result` or `.Wait()` on `Task`.
- No hard-coded secrets, credentials, or connection strings.
- No floating NuGet version ranges (`Version="*"`).
- All coding standards in [coding-standards.md](./coding-standards.md) are satisfied.

## 6. Connector Coverage

- Every feature that interacts with source or target systems is fully implemented for **Simulated**, **AzureDevOpsServices**, and **TeamFoundationServer** (where the TFS OM API supports the capability).
- No connector implementation is left as a stub, placeholder, or `NotImplementedException`.
- No connector implementation is deferred to a follow-up PR or future task.
- TFS exemptions have a specific API limitation rationale documented and the code gracefully skips the operation with a structured warning.

## 7. Documentation

- Every canonical doc named in any doc-task in `tasks.md` is updated.
- CLI changes have a corresponding `.vscode/launch.json` entry.
- Deployable Host changes are covered in `build.ps1`.

## 8. Compliance Review

After completing the work, re-read every relevant doc referenced by the guardrails. Check each change against the docs line by line. If any non-compliance is found, fix it and repeat. Only when the review loop finds zero violations is the task done.

---

## Summary Checklist

```
[ ] dotnet clean && dotnet build --no-incremental — 0 errors
[ ] dotnet test — all tests pass, 0 failures, 0 skipped-by-marker
[ ] No Assert.Inconclusive in any test
[ ] No @ignore or [Ignore] in committed code
[ ] No NotImplementedException in reachable code
[ ] No hanging tests
[ ] build.ps1 install — passes
[ ] O-1: ActivitySource.StartActivity on every operation (traces) — verified by code inspection, not just compilation
[ ] O-2: IMigrationMetrics called for attempt/completion/error/duration/in-flight — all 5 call sites present per operation boundary
[ ] O-3: ILogger at Information (start/end with counts), Warning (skips/errors), Debug (per-item) — no string interpolation in log calls
[ ] O-4: IProgressSink injected (optional); EmitAsync CALLED (not just injected) at start, per-item, and complete; Metrics.Migration.{Module} populated on completion
[ ] O-4: Progress bar row for this module visible in CLI BuildProgressRenderable in correct execution order
[ ] Pipeline wiring table completed — all 8 links ✅ (metrics via telemetry endpoint, stages via SSE, CLI and TUI both reading from ControlPlane only)
[ ] CLI BuildProgressRenderable reads counters from GET /jobs/{id}/telemetry — NOT from ProgressEvent.Metrics
[ ] No direct IProgressSink wiring in CLI or TUI code
[ ] DI wiring verified — every new class registered in Add*Services, extension called from host startup
[ ] Scenario config executed — all module progress bars appear in CLI output
[ ] Unit tests for O-1 (StartActivity called), O-2 (metrics recorded), O-4 (EmitAsync called at start and completion)
[ ] Docs updated where required
[ ] Compliance review loop completed with 0 findings
```
