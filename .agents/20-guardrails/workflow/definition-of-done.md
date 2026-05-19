# Definition of Done

Every unit of work must satisfy **all** criteria below. Zero exceptions.

---

## 1. Build

- `dotnet clean && dotnet build --no-incremental` — 0 errors, 0 warnings-as-errors.
- Both Debug and Release must build clean.
- `build.ps1 install` must complete without error.

## 2. Tests

- All tests run and pass. Zero failures, zero errors.
- Every addition, bug fix, and behaviour change has evidence of RED → GREEN → REFACTOR: a failing behavioural test first, the minimal passing implementation second, then a fresh full-suite run returning the repository to an all-green state, and only then refactoring.
- No production-first additions. If the change did not begin from an intended failing test, it is not done.
- No `Assert.Inconclusive()` — treated as build-breaking. Implement the assertion or delete the test.
- No `@ignore` (Gherkin) or `[Ignore]` (MSTest) in committed code. Session-only temporary use permitted.
- No `throw new NotImplementedException()` in any reachable code path.
- No hanging tests (infinite loops, unbounded waits, clock-racing).

Declaring GREEN from a targeted subset alone is a workflow failure. The final validation for the green state is always the full test suite.

## 3. Observability ⛔ MANDATORY

Every module/tool must pass all four checks:

| Req | Check | Verification |
| --- | --- | --- |
| O-1 | `using var activity = ActivitySource.StartActivity(...)` with meaningful tags | Grep for `StartActivity` in every I/O/iteration method |
| O-2 | `IMigrationMetrics` called for attempt, completion, error, duration, in-flight | 5 call sites per operation boundary |
| O-3 | `Information` start/end; `Warning` skips; `Debug` per-item; structured params only (no `$"` in log calls) | Grep for `Log*` calls |
| O-4 | `IProgressSink` injected optional; `Emit` called at start, per-item/batch ≤50, completion; `Metrics.Migration.{Module}` populated | Grep for `Emit` in 3+ places |
| O-4 CLI | Progress row in `QueueCommand.BuildProgressRenderable` in correct order | Inspect `QueueCommand.cs` |
| O-5 | Every `IWorkItemDiscoveryService` and `WorkItemFetchScope` call site passes a non-null `IProgress<int>` wired to `IProgressSink.Emit`; `null` only where the method signature documents a permitted exception | Grep for `DiscoverWorkItemsAsync`/`CountWorkItemsAsync`/`FetchAsync` callers |

**Pipeline wiring:** Verify both paths are intact:

- Metrics path: Module → `IMigrationMetrics` → OTel → `SnapshotMetricExporter` → `JobMetrics` → `POST /telemetry` → CLI polls `GET /jobs/{id}/telemetry` → `BuildProgressRenderable`
- Progress path: Module → `IProgressSink.Emit` → `ControlPlaneProgressSink` → `POST /progress` → SSE → CLI subscribes `GET /jobs/{id}/progress?follow=true`

**FAIL conditions:** Any link missing; counter read from `ProgressEvent.Metrics` in CLI/TUI (null for .NET 10 = silent zeros); direct `IProgressSink` wiring in CLI/TUI.

## 3a. DI Wiring

- Every new interface implementation has `services.Add*<IFoo, Foo>()` registration.
- Extension method called from host startup.
- Constructor injection only — no `new` in module/service code.
- Verified by scenario run (missing reg → `InvalidOperationException`).

## 3b. Capability Seam Integrity ⛔ MANDATORY

- A canonical seam exists for each concern touched by the change.
- No parallel runtime entry point was introduced for that concern.
- Core concern logic remains centralized behind the seam (no duplicate engines in modules/orchestrators/extensions).
- Adapter/extension changes are policy-orchestration only.
- Review evidence explicitly states pass/fail across Modular Monolith, Clean, Hexagonal, Vertical Slice, Screaming, and Architecture Deepening perspectives for the touched scope.
- Perspective evidence must follow `.agents/20-guardrails/core/architecture-perspectives-ethos.md`.

Failing any item blocks completion.

## 4. Scenario Execution

- At least one scenario config run via `.vscode/launch.json` debug profile.
- Completes without errors, produces expected output.
- All enabled module progress bars visible in CLI output.

## 4a. Functional Correctness ⛔ MANDATORY

Compiling + not crashing ≠ working. Every module must produce correct side effects:

**Export:** `IArtefactStore` contains expected file at documented path AND file is non-empty (length > 0). Count > 0 when source is non-empty. Count = 0 → `Warning` log (never silent).

**Import:** Target connector received data (e.g., `SimulatedTeamTarget.Teams.Count > 0`). Count > 0 when package is non-empty. Count = 0 → `Warning` log.

**Connectors:** `Simulated*Source` yields ≥ 2 items. `Simulated*Target` records state in inspectable collection. `AzureDevOps*` calls at least one SDK client method per operation.

**Forbidden assertion patterns:**

- `Assert.IsTrue(true)` or `Assert.IsNotNull(result)` as sole assertion
- `Assert.IsTrue(count >= 0)` — always true, asserts nothing
- Test body with no `Assert` at all

## 5. Code Quality

- No `NotSupportedException("... not yet implemented")` in reachable code.
- No `.Result` or `.Wait()` on `Task`.
- No hard-coded secrets.
- No floating NuGet versions.
- All coding standards satisfied.

## 6. Connector Coverage

- Simulated, AzureDevOpsServices, and TeamFoundationServer all implemented (where API supports).
- No stubs, placeholders, or deferred implementations.
- TFS exemptions documented with structured warning in code.

## 7. Documentation

- Every canonical doc named in doc-tasks in `tasks.md` is updated.
- CLI changes → `.vscode/launch.json` entry.
- Host changes → `build.ps1` coverage.

## 8. Compliance Review

Re-read every relevant doc. Check each change line by line. Fix any non-compliance and repeat. Done only when review loop finds zero violations.

---

## Summary Checklist

```text
[ ] Build passes (0 errors)
[ ] All tests pass (0 failures, no Inconclusive/Ignore/NotImplementedException)
[ ] RED → GREEN → REFACTOR evidence exists for every addition, bug fix, and behaviour change
[ ] build.ps1 install passes
[ ] O-1: Traces verified
[ ] O-2: Metrics verified (5 call sites per operation)
[ ] O-3: Logs verified (structured, correct levels)
[ ] O-4: ProgressEvents verified (3+ Emit calls, Metrics populated)
[ ] O-4: CLI progress row visible in BuildProgressRenderable
[ ] O-5: Every discovery/fetch call site wired with IProgress<int> callback
[ ] Capability seam integrity verified (single canonical seam, no parallel entry points, adapter-only policy)
[ ] Perspective evidence recorded for Modular Monolith, Clean, Hexagonal, Vertical Slice, Screaming, and Architecture Deepening
[ ] Pipeline wiring table all ✅
[ ] CLI reads counters from telemetry endpoint, NOT ProgressEvent.Metrics
[ ] No direct IProgressSink wiring in CLI or TUI
[ ] DI wiring verified
[ ] Scenario executed — progress bars visible
[ ] Functional correctness — artefacts exist AND non-empty (export)
[ ] Functional correctness — target received data (import)
[ ] Simulated yields ≥ 2 items; no forbidden assertions
[ ] ADO connector calls SDK
[ ] Connectors all implemented
[ ] Docs updated
[ ] Compliance review loop: 0 findings
```




