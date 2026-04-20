# Research: Simulated Data Source for End-to-End Migration Testing

**Date**: 2026-04-09  
**Branch**: `copilot/simulate-migration-data`  
**Status**: Complete — all NEEDS CLARIFICATION resolved

---

## Decision 1: Deterministic Data Generation Strategy

**Decision**: Use `System.Random(int seed)` with a fixed seed, instantiated once per `SimulatedRevisionStream` run. All field values, work item IDs, revision counts, dates, and link structures are derived from successive calls to the seeded `Random` instance.

**Rationale**:
- `System.Random` in .NET 6+ uses a deterministic algorithm that is stable across platform and .NET version patch releases when given the same seed. For cross-version stability, we use the algorithm-locked constructor overload and document the .NET version requirement in `SimulatedSourceOptions`.
- The seeded approach guarantees SC-003 (byte-identical `revision.json` for same seed + workItemCount).
- `System.Random` is not thread-safe, so the stream is designed to run on a single thread (matching the streaming `IAsyncEnumerable<WorkItemRevision>` contract which is consumed sequentially by the orchestrator).
- No external libraries are needed; this keeps the project dependency surface minimal.

**Alternatives considered**:
- **`Bogus` (Faker.NET)**: Popular fake-data library, but it adds a package dependency, has its own versioning concerns, and `System.Random` is sufficient for deterministic field generation.
- **Pre-generated JSON fixture files**: Would limit scale testing and require regeneration; not viable at 25k items.
- **Deterministic hash functions (e.g., SHA-256 of item ID)**: More reproducible across .NET versions but much slower and harder to control value distributions; rejected on performance grounds.

---

## Decision 2: Import Target Abstraction (`IWorkItemImportSink`)

**Decision**: Introduce `IWorkItemImportSink` in `DevOpsMigrationPlatform.Abstractions` as the injection point for the import write target. `WorkItemsModule.ImportAsync` is updated to use this interface. Two implementations are delivered:
1. `AzureDevOpsWorkItemImportSink` in `Infrastructure.AzureDevOps` for the real ADO target.
2. `SimulatedWorkItemImportSink` in `Infrastructure.Simulated` for the testing target.

**Rationale**:
- `WorkItemsModule.ImportAsync` currently throws `NotImplementedException`. Implementing it requires writing to a target system. Without an abstraction, the module directly depends on ADO-specific infrastructure, making it untestable and preventing the simulated target.
- The abstraction follows the same pattern as `IWorkItemRevisionSource` on the export side.
- The `IWorkItemImportSink` interface exposes: `WriteRevisionAsync(WorkItemRevision, IArtefactStore packageStore, CancellationToken)` — the store is passed so that attachment binaries can be streamed from the package to the target without loading into memory.
- `SimulatedWorkItemImportSink` validates each revision against the `WorkItemRevision` schema (checks required fields, validates attachment `sha256` format) and counts items/revisions processed. It writes validation results to `Logs/` via `IArtefactStore`. It does not write to any external system.

**Alternatives considered**:
- **No abstraction — SimulatedWorkItemsModule overrides ImportAsync**: Would require a separate `IDataTypeModule` implementation just for simulated import, duplicating all cursor/checkpoint logic. Rejected: more code, less testable, violates SRP.
- **Factory-pattern for import target**: Similar to `IWorkItemRevisionSourceFactory`. Viable but unnecessarily complex for two static implementations; the DI registration approach (select implementation by target.type at agent startup) is simpler.

---

## Decision 3: Source-Type-Aware DI Registration

**Decision**: Source-type and target-type selection is resolved at agent DI setup time, not at runtime dispatch. The `MigrationAgent` reads `job.Source.Type` and `job.Target.Type` from the `MigrationJob` and registers the appropriate factory implementations before the DI container is built.

**Rationale**:
- The `MigrationJob` is available before DI container construction in the agent. The agent's host builder receives the job as input.
- A keyed service dispatcher (e.g., `IWorkItemRevisionSourceFactoryDispatcher`) would require changing `WorkItemsModule` and complicating the resolution logic. Registration-time selection is simpler and doesn't change the module interface.
- This pattern is already used implicitly: `ExportServiceCollectionExtensions.AddAzureDevOpsWorkItemExport()` registers a specific factory. For simulated runs, `SimulatedServiceCollectionExtensions.AddSimulatedWorkItemExport()` replaces that registration.

**Pattern**:
```csharp
// In MigrationAgent DI setup:
if (job.Source?.Type == "Simulated")
    services.AddSimulatedWorkItemExport(job.Source);
else
    services.AddAzureDevOpsWorkItemExport();

if (job.Target?.Type == "Simulated")
    services.AddSimulatedWorkItemImport(job.Target);
else
    services.AddAzureDevOpsWorkItemImport();
```

**Alternatives considered**:
- **Keyed services (`IServiceProvider.GetRequiredKeyedService`)**: Requires adding key-based registration to `WorkItemsModule` and changes the module contract. Rejected: unnecessary complexity.
- **Runtime `IWorkItemRevisionSourceFactoryResolver`**: A resolver interface that selects a factory by source-type string. Viable but adds an indirection layer that is not needed if the container is built with the right factory from the start.

---

## Decision 4: Simulated Discovery Implementation

**Decision**: `SimulatedWorkItemDiscoveryService` implements `IWorkItemDiscoveryService`. It returns a single `ProjectDiscoverySummary` (or multiple, one per configured project) with `WorkItemCount` derived directly from `SimulatedSourceOptions.WorkItemCount / ProjectCount`. No WIQL queries, no date-windowing algorithm.

**Rationale**:
- The spec assumption states: "The `discovery inventory` command with a simulated source does not need to exercise the WIQL date-windowing algorithm — it returns counts derived directly from configuration."
- Streaming a single (or few) summary events is correct because the existing `IWorkItemDiscoveryService` contract yields incremental snapshots; for a simulated source with instant counts, a single final event per project is sufficient.
- The service emits `IsWorkItemComplete = true` immediately, consistent with how the real service marks completion after its final date window.

**Alternatives considered**:
- **Simulating windowed date queries**: Would add false realism and complexity with no testing value; the discovery pipeline logic is already tested via ADO integration.

---

## Decision 5: Attachment Binary Generation

**Decision**: When `SimulatedSourceOptions.IncludeAttachments = true`, the source generates synthetic attachment binary files as `byte[]` of configurable size filled with deterministic pseudo-random bytes (seeded). Binaries are written to the package via `IArtefactStore.WriteBinaryAsync` as the orchestrator already does for real attachments. `IAttachmentBinarySource` is implemented as `SimulatedAttachmentBinarySource`.

**Rationale**:
- Attachment binaries must be beside `revision.json` per Canon III. The orchestrator already handles writing them when an `IAttachmentBinarySource` is injected.
- Generating synthetic bytes (rather than copying a real file) keeps the simulated infrastructure self-contained and size-configurable.
- Attachment size defaults to 4 KB (small enough for fast tests, large enough to exercise binary write paths).

**Alternatives considered**:
- **No binary generation, metadata only**: Simpler, but would not exercise `WriteBinaryAsync` or the attachment `sha256` verification path. Rejected for E2E completeness.
- **Referencing a real file on disk**: Would create a filesystem dependency and break CI. Rejected.

---

## Decision 6: `manifest.json` Seed Recording

**Decision**: The simulated seed (whether provided or auto-generated) is recorded in `manifest.json` under a `source.simulatedSeed` field. Auto-generated seeds are logged at `Information` level before the export begins (FR-011).

**Rationale**:
- FR-011 requires seed logging and recording for reproducibility.
- Adding `simulatedSeed` to the `manifest.json` `source` block is consistent with how other source-specific fields are stored in the manifest.
- This does not break the existing manifest schema version (`packageVersion`); the field is optional and absent for non-simulated packages.

**Alternatives considered**:
- **Separate `simulated-manifest.json`**: Unnecessary indirection; the main manifest is the canonical reproducibility record.

---

## Decision 7: System Test Location

**Decision**: System tests are added to a new `tests/DevOpsMigrationPlatform.SystemTests/` project tagged `[TestCategory("SystemTest")]`.

**Rationale**:
- Existing test projects are `CLI.Migration.Tests`, `ControlPlane.Tests`, `Infrastructure.Tests`. A dedicated `SystemTests` project makes the test category visible at project level and avoids coupling CLI unit tests with long-running E2E tests.
- CI pipelines can filter by project path to run system tests separately from unit tests.
- The system test orchestrates the full CLI → ControlPlane → Agent → Package → Validate flow in-process using `devopsmigration migrate` programmatically.

**Alternatives considered**:
- **Adding to `CLI.Migration.Tests`**: Mixes unit and system test concerns. Rejected.

---

## Decision 8: `configHash` Inclusion of Simulated Parameters

**Decision**: `SimulatedSourceOptions` and `SimulatedTargetOptions` are included in the serialised config JSON before hashing (via the existing `configHash` computation in the CLI). The hash changes if any simulated parameter changes, which triggers the "configHash mismatch" resume rejection (edge case in spec).

**Rationale**:
- The spec edge case states: "If the seed or workItemCount changes between runs, the platform detects a `configHash` mismatch and rejects the resume." This is already handled by the existing `configHash` mechanism — no special logic needed in the simulated source.
- Simply ensuring `SimulatedSourceOptions` serialises all fields (including `seed`) means the existing hash mechanism covers this case.

---

## All NEEDS CLARIFICATION Items: Resolved

| Item | Resolution |
|------|-----------|
| How to dispatch source-type-specific factories | Registration-time selection in agent DI setup (Decision 3) |
| How to implement simulated target without changing module contract | New `IWorkItemImportSink` abstraction (Decision 2) |
| How to ensure deterministic byte-identical output | `System.Random(seed)` with sequential generation (Decision 1) |
| How to handle seed recording and logging | `manifest.json` `source.simulatedSeed` field + Information log (Decision 6) |
| Where system tests live | New `DevOpsMigrationPlatform.SystemTests` project (Decision 7) |
| Attachment binary generation strategy | `SimulatedAttachmentBinarySource` with deterministic pseudo-random bytes (Decision 5) |
