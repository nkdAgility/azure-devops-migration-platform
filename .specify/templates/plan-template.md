# Implementation Plan: [FEATURE]

**Branch**: `[###-feature-name]` | **Date**: [DATE] | **Spec**: [link]
**Input**: Feature specification from `/specs/[###-feature-name]/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

[Extract from feature spec: primary requirement + technical approach from research]

## Technical Context

<!--
  ACTION REQUIRED: Replace the content in this section with the technical details
  for the project. The structure here is presented in advisory capacity to guide
  the iteration process.
-->

**Language/Version**: [e.g., Python 3.11, Swift 5.9, Rust 1.75 or NEEDS CLARIFICATION]  
**Primary Dependencies**: [e.g., FastAPI, UIKit, LLVM or NEEDS CLARIFICATION]  
**Storage**: [if applicable, e.g., PostgreSQL, CoreData, files or N/A]  
**Testing**: [e.g., pytest, XCTest, cargo test or NEEDS CLARIFICATION]  
**Target Platform**: [e.g., Linux server, iOS 15+, WASM or NEEDS CLARIFICATION]
**Project Type**: [e.g., library/cli/web-service/mobile-app/compiler/desktop-app or NEEDS CLARIFICATION]  
**Performance Goals**: [domain-specific, e.g., 1000 req/s, 10k lines/sec, 60 fps or NEEDS CLARIFICATION]  
**Constraints**: [domain-specific, e.g., <200ms p95, <100MB memory, offline-capable or NEEDS CLARIFICATION]  
**Scale/Scope**: [domain-specific, e.g., 10k users, 1M LOC, 50 screens or NEEDS CLARIFICATION]

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** Before completing this gate, confirm that ALL files in
> `/.agents/guardrails/`, ALL files in `/.agents/context/`, and relevant `/docs/` files
> have been read. Skipping either `.agents/` subdirectory is a constitution violation.

- [ ] **Package-First (I):** No direct source-to-target migration. All reads/writes go via the on-disk package through `IArtefactStore`.
- [ ] **Streaming (II):** Import processes one revision folder at a time. No in-memory list/array of all revisions. No in-memory sort of `EnumerateAsync` results.
- [ ] **WorkItems Layout (III):** Folder structure `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` is preserved. No attachments root. No renaming or flattening.
- [ ] **Checkpointing (IV):** Module uses a cursor file under `Checkpoints/`. No watermark tables or in-memory progress counters.
- [ ] **Module Isolation (V):** All persistence through `IArtefactStore`/`IStateStore`. No concrete store references in module code. Identity via `IIdentityMappingService`.
- [ ] **Separation of Planes (VI):** Control plane has no migration logic. Job Engine has no UI coupling. TUI has no migration logic. TFS exporter only via subprocess adapter.
- [ ] **Determinism (VII):** Same inputs produce stable package layout. All breaking schema changes include an upgrader.
- [ ] **ATDD-First (VIII):** Every user story in `spec.md` has at least one Given/When/Then acceptance scenario. Each scenario will be implemented via the ATDD inner loop (Specification → Test Gen → Implementation → Review) — one scenario per session per commit.
- [ ] **SOLID & DI (IX):** All new services and modules receive dependencies via constructor injection only. Configuration is bound via `IOptions<T>` with a sealed options class and `SectionName` constant. No raw `IConfiguration` access inside services. Interfaces are defined in `DevOpsMigrationPlatform.Abstractions`. Registration lives in a dedicated `Add*Services` extension method.
- [ ] **Full Connector Coverage (XI):** Every feature that interacts with source or target systems is designed for all three connectors — Simulated, AzureDevOpsServices, and TeamFoundationServer. No connector is left as a stub, placeholder, or deferred to a follow-up. TFS exemptions require a documented API limitation rationale.

## Observability Contract

*GATE: Must be completed before task generation. Every operation enumerated here MUST appear as explicit tasks in `tasks.md`. This is not optional — a feature without a complete observability contract will not reach done.*

> **Read `.agents/context/telemetry-architecture.md` before completing this section.**
> Read `WellKnownMetricNames.cs`, `WellKnownActivitySourceNames.cs`, and `WellKnownMeterNames.cs` to use correct existing names or define new ones following the established naming convention.

For each operation introduced or modified by this feature, fill in one row. A feature with no operations (e.g. a pure refactor) must state "No operations — pure refactor" explicitly.

### Operations Table

| Operation | Class / Method | Span Name (O-1) | Metrics Instruments (O-2) | Log Events (O-3) | ProgressEvent Stage (O-4) |
|-----------|---------------|-----------------|--------------------------|-----------------|-----------------------------|
| [e.g. Export work items] | [e.g. WorkItemsModule.ExportAsync] | `[e.g. export.workitems]` | `migration.workitems.attempts`, `migration.workitems.completed`, `migration.workitems.errors`, `migration.workitems.duration`, `migration.workitems.inflight` | `Information`: "Exporting {Count} work items"; `Warning`: "Skipping {Id}: {Reason}"; `Debug`: "Processing revision {Path}" | `Exporting`, `Exported`, `Failed` |
| [add rows for every operation] | | | | | |

### Wiring Checklist

For this feature, confirm that data flows end-to-end from the module to the CLI:

- [ ] **O-1 ActivitySource:** Span name(s) listed above exist in `WellKnownActivitySourceNames` (or new names are added)
- [ ] **O-2 Metric instruments:** All metric names listed above exist in `WellKnownMetricNames` (or new names are added with matching `IMigrationMetrics` / `IDiscoveryMetrics` interface methods and `MigrationMetrics` / `DiscoveryMetrics` implementations)
- [ ] **O-2 Meter registration:** If new meters are introduced, `.AddMeter(WellKnownMeterNames.X)` is added to BOTH the MigrationAgent host AND the TFS host registration sites
- [ ] **O-3 Log structured params:** Every log call uses structured params (`{WorkItemId}`, `{Count}`, etc.) — no string interpolation
- [ ] **O-4 IProgressSink wiring:** `IProgressSink` is injected as optional (`IProgressSink?`) and `EmitAsync` is called (not just injected)
- [ ] **O-4 ModuleCounters property:** `MigrationCounters` (or `DiscoveryCounters`) has a property for this module's counters; `SnapshotMetricExporter.cs` is updated to extract this metric into `JobMetrics`
- [ ] **O-4 CLI row:** `QueueCommand.BuildProgressRenderable` has a progress bar row for this module in correct execution order (Identities → Nodes → Teams → WorkItems → ...)
- [ ] **DI wiring verified:** Every new `class` implementing an interface has a corresponding `services.AddSingleton<IFoo, Foo>()` (or appropriate lifetime) in a `ServiceCollectionExtensions` method that is itself called from the host startup

### Tests Required for Observability

These test tasks MUST appear in `tasks.md`:

- [ ] Unit test: verify `ActivitySource.StartActivity` is called with correct span name (use `TestActivityListener` or mock)
- [ ] Unit test: verify `IMigrationMetrics` receives attempt/completion/error calls (inject mock `IMigrationMetrics`)
- [ ] Unit test: verify `IProgressSink.EmitAsync` is called at start, per-item (or per batch ≤50), and completion
- [ ] Unit test: verify `ILogger` receives `Information` at start and end with correct structured parameters
- [ ] Simulated system test: run scenario end-to-end → CLI output shows progress bar row for this module

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)
<!--
  ACTION REQUIRED: Replace the placeholder tree below with the concrete layout
  for this feature. Delete unused options and expand the chosen structure with
  real paths (e.g., apps/admin, packages/something). The delivered plan must
  not include Option labels.
-->

```text
# [REMOVE IF UNUSED] Option 1: Single project (DEFAULT)
src/
├── models/
├── services/
├── cli/
└── lib/

tests/
├── contract/
├── integration/
└── unit/

# [REMOVE IF UNUSED] Option 2: Web application (when "frontend" + "backend" detected)
backend/
├── src/
│   ├── models/
│   ├── services/
│   └── api/
└── tests/

frontend/
├── src/
│   ├── components/
│   ├── pages/
│   └── services/
└── tests/

# [REMOVE IF UNUSED] Option 3: Mobile + API (when "iOS/Android" detected)
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure: feature modules, UI flows, platform tests]
```

**Structure Decision**: [Document the selected structure and reference the real
directories captured above]

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| [e.g., 4th project] | [current need] | [why 3 projects insufficient] |
| [e.g., Repository pattern] | [specific problem] | [why direct DB access insufficient] |
