# Implementation Plan: Schema Generation from IOptions DI Registrations

**Branch**: `028-ioptions-schema-gen` | **Date**: 2026-04-30 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/028-ioptions-schema-gen/spec.md`

## Summary

Replace the `ActiveJobConfigState` global mutable singleton with a flat `IOptions<T>` per-slice injection pattern across all modules and tools. Introduce a `SchemaOptionsEntry` registry so that JSON Schema is derived at build time from the same DI registrations the running system uses. Add `IAgentJobContext` (in `Abstractions.Agent`) for cross-cutting job scalars, and `ISourceEndpointInfo`/`ITargetEndpointInfo` (in `Abstractions.Agent`) for connector-registered endpoint access. Wire `migration.schema.json` into the CLI's Tier 0 validation before `queue` submission. Delete `ActiveJobConfigState` once all consumers are migrated. `MigrationOptions` is reduced to a transient deserialisation bootstrap shim with no module injection role.

## Technical Context

**Language/Version**: C# 12, .NET 10 (schema generator + CLI + agent); .NET 4.8 (TFS agent — unaffected)  
**Primary Dependencies**: `Microsoft.Extensions.Options`, `NJsonSchema` (schema generation + Tier 0 validation), existing `IArtefactStore`/`IStateStore` abstractions  
**Storage**: N/A — no new persistence; schema file written to output directory  
**Testing**: MSTest + Reqnroll (Gherkin feature files in `features/`), `[TestCategory("SystemTest_Simulated")]` for end-to-end  
**Target Platform**: CLI (`net10.0`), MigrationAgent (`net10.0`), TfsMigrationAgent (`net481` — no changes required)  
**Project Type**: Infrastructure refactor + new build-time tool (`SchemaGenerator`)  
**Performance Goals**: Schema generation completes in < 5s on a development machine; Tier 0 JSON Schema validation adds < 50ms to `queue` command startup  
**Constraints**: `Abstractions` and `Abstractions.Agent` must remain multi-targeted (`net481;net10.0`); `SchemaGenerator` is `net10.0` only; TFS agent must compile and pass all tests throughout every migration step  
**Scale/Scope**: ~6 modules, ~3 tools, ~3 connector assemblies to migrate; 1 new project (`SchemaGenerator`); ~15 new or modified types

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** All guardrail files, context files, and relevant docs have been read in this session.

- [x] **Package-First (I):** No direct source-to-target migration. This feature is a DI/config refactor with no new package I/O. The `SchemaGenerator` writes only to the build output directory, not to a migration package.
- [x] **Streaming (II):** Not applicable — no new module processing. Existing streaming guarantees are preserved; this feature does not touch `EnumerateAsync` or revision processing.
- [x] **WorkItems Layout (III):** Not applicable — no changes to the package layout.
- [x] **Checkpointing (IV):** Not applicable — no new module state.
- [x] **Module Isolation (V):** This feature **improves** isolation by removing `ActiveJobConfigState` (global mutable state accessed from modules) and replacing it with `IOptions<T>` constructor injection. All persistence remains through `IArtefactStore`/`IStateStore`.
- [x] **Separation of Planes (VI):** Respected. The Tier 0 JSON Schema validation addition stays entirely within the CLI layer. No migration logic is added to the CLI. The `SchemaGenerator` is a build-time tool, not a runtime component.
- [x] **Determinism (VII):** Schema generation is deterministic — same DI registrations produce the same schema. The `SectionName` constants are compile-time values.
- [x] **ATDD-First (VIII):** All user stories in spec.md have Given/When/Then scenarios. Feature files will be written before implementation.
- [x] **SOLID & DI (IX):** This feature IS the enforcement of the `IOptions<T>` with `SectionName` pattern across all modules and tools. `IAgentJobContext`, `ISourceEndpointInfo`, and `ITargetEndpointInfo` interfaces are defined in `Abstractions.Agent`. All registrations in dedicated `Add*Services` extension methods.
- [x] **Full Connector Coverage (XI):** Each connector assembly (Simulated, AzureDevOps, TFS) must register `SchemaOptionsEntry` for its options types and register `ISourceEndpointInfo`/`ITargetEndpointInfo`. No connector is exempt from self-registration. TFS is source-only so registers `ISourceEndpointInfo` only.

## Observability Contract

*GATE: Must be completed before task generation.*

> This feature introduces two operations with observable boundaries. It does not introduce a runtime migration module — O-4 ProgressEvent emission is not applicable. Existing `WellKnownActivitySourceNames.Cli` and `WellKnownActivitySourceNames.Migration` are reused; no new meter or activity source names are required.

### Operations Table

| Operation | Class / Method | Span Name (O-1) | Metrics Instruments (O-2) | Log Events (O-3) | ProgressEvent Stage (O-4) |
|-----------|---------------|-----------------|--------------------------|-----------------|--------------------------|
| Schema generation | `SchemaGeneratorHost.RunAsync` | `schema.generate` (source: `WellKnownActivitySourceNames.Migration`) | None — build-time tool; no runtime OTel exporter | `Information`: "Schema generation started — {EntryCount} entries"; `Information`: "Schema generation succeeded — {EntryCount} entries in {DurationMs}ms → {OutputPath}"; `Error`: "Schema generation failed at step '{Step}': {Error}"; `Error`: "Duplicate SectionName '{SectionPath}' registered by {Type1} and {Type2}" | N/A — build tool |
| Tier 0 JSON Schema validation | `QueueCommand` (before `LoadConfigurationAsync`) | None — synchronous pre-flight, no distributed trace needed | None | `Error`: "Config validation failed: {JsonPath} — {Constraint} ({ConfigFile})"; `Warning`: "Schema validation skipped — schema file not found at {ExpectedSchemaPath}" | N/A — CLI pre-flight |
| `IAgentJobContext` resolution | `AgentJobContext` (registered per-job) | None — DI registration, not an operation | None | `Debug`: "Agent job context resolved — Mode={Mode} ConfigVersion={ConfigVersion}" | N/A |

### Wiring Checklist

- [x] **O-1 ActivitySource:** `schema.generate` uses `WellKnownActivitySourceNames.Migration` (existing). No new source names required.
- [x] **O-2 Metric instruments:** No new metric instruments. Schema generation and Tier 0 validation are not metered operations.
- [x] **O-2 Meter registration:** No new meters. No changes to MigrationAgent or TFS host registration.
- [x] **O-3 Log structured params:** All log calls use structured params (`{EntryCount}`, `{DurationMs}`, `{OutputPath}`, `{JsonPath}`, `{Constraint}`, `{ConfigFile}`, `{ExpectedSchemaPath}`, `{Mode}`, `{ConfigVersion}`).
- [x] **O-4 IProgressSink wiring:** Not applicable — no runtime migration module introduced.
- [x] **O-4 ModuleCounters property:** Not applicable.
- [x] **O-4 CLI row:** Not applicable — no new progress bar row.
- [x] **DI wiring verified:** `IAgentJobContext` → `AgentJobContext` registered in `MigrationAgentServiceExtensions`. `ISourceEndpointInfo`/`ITargetEndpointInfo` registered by each connector's own `Add*Services` extension.

### Tests Required for Observability

- [ ] Unit test: `SchemaGenerator` logs `Information` at start and success, with `EntryCount > 0`
- [ ] Unit test: `SchemaGenerator` logs `Error` and fails when two entries share a `SectionPath`
- [ ] Unit test: `QueueCommand` Tier 0 validation logs `Error` with `JsonPath` when config contains an unknown key
- [ ] Unit test: `QueueCommand` Tier 0 validation logs `Warning` when `migration.schema.json` is absent

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
