# Feature Specification: Simulated Infrastructure Connector

**Feature Branch**: `017-simulated-infrastructure`
**Created**: 2026-04-18
**Status**: Draft
**Input**: Add Infrastructure.Simulated connector assembly with polymorphic endpoint config, config-driven work item generator, and full boundary cleanup

## Architecture References

The following `docs/` and `.agents/` files were read before drafting this spec:

| File | Status |
|---|---|
| `docs/architecture.md` | Confirmed accurate — Simulated is listed as a valid source/target mode |
| `docs/module-development-guide.md` | Confirmed accurate — `WorkItemsModule` contract drives the connector pattern |
| `.agents/20-guardrails/core/architecture-boundaries.md` | Confirmed accurate — all rules applied |
| `analysis/Simulated.md` | **Design input** — full connector analysis; drives this spec |

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Run a full export without connecting to Azure DevOps or TFS (Priority: P1)

An operator or developer wants to validate the export pipeline, test checkpointing behaviour, or verify that a new extension works end-to-end, without needing ADO credentials or a live project. They use a `Simulated` source to generate a synthetic package of any size and shape.

**Why this priority**: This is the primary motivation for the entire feature. Without it all other stories are inaccessible.

**Independent Test**: Run `scenarios/queue-export-workitems-simulated-source.json` via the `.vscode/launch.json` profile. The package folder is written with chronological `WorkItems/` layout, `revision.json` files, checkpoints, and logs. No network calls are made.

**Acceptance Scenarios**:

1. **Given** a scenario config with `Source.Type: "Simulated"` and `Source.Generator.Projects` describing 2 projects with 3 work item types and 5 revisions each, **When** the export job runs to completion, **Then** a valid migration package exists at the configured output path with one revision folder per synthetic revision in chronological lexicographic order.
2. **Given** the same config, **When** the export job is interrupted mid-run and resumed, **Then** only revisions not yet written are processed (cursor-based resume), and the final package is identical to a clean run.
3. **Given** `LinkTopology: "TreeWithCrossLinks"` in the config, **When** the export completes, **Then** each `revision.json` contains the expected synthetic link entries referencing valid work item IDs within the generated set.
4. **Given** `HasAttachments: true` with `AttachmentSizeKb: 50`, **When** the export completes, **Then** each revision folder with an attachment contains a deterministic binary file of the expected size beside `revision.json`.
5. **Given** `HasComments: true`, **When** the export completes, **Then** each revision folder contains a `comments.json` file with the configured number of synthetic comments.

---

### User Story 2 — Run a full import into a simulated target (Priority: P1)

A developer wants to test the full import pipeline without writing to a live ADO organisation. They run an import job with `Target.Type: "Simulated"`. The import target accepts all calls, assigns sequential IDs, and produces observable progress output — no data is written to any external system.

**Why this priority**: Enables roundtrip (export + import) testing without any external credentials. Unlocks system tests that run in CI.

**Independent Test**: Run the existing `scenarios/queue-import-workitems-simulated-fixture.json` and a new `scenarios/queue-import-workitems-simulated-target.json`. Both complete without error.

**Acceptance Scenarios**:

1. **Given** a migration package with 10 work items and a scenario config with `Target.Type: "Simulated"`, **When** the import job runs, **Then** all 10 items are reported as created, sequential IDs are assigned, and progress events are emitted for each.
2. **Given** the import job is interrupted and resumed, **Then** already-imported items are skipped (cursor-based resume), and final counts match a clean run.
3. **Given** `WorkItemResolutionStrategy: "Null"` (always create-new), **When** the import runs twice on the same package, **Then** duplicate items are created as expected — the strategy is not silently overridden.

---

### User Story 3 — Config-driven roundtrip: export then import without credentials (Priority: P2)

A developer runs a full Source → Files → Target pipeline using Simulated for both ends. The config specifies generator settings for the source side and a no-op target. The developer gets confidence that the whole platform plumbing works end-to-end.

**Why this priority**: Validates the full export+import pipeline in one scenario. Valuable but requires both US1 and US2 first.

**Independent Test**: A new scenario config `scenarios/roundtrip-simulated.json` runs export with Simulated source, then import with Simulated target against the produced package, all without any network calls.

**Acceptance Scenarios**:

1. **Given** a roundtrip scenario config, **When** a job in `Both` mode completes, **Then** the produced package passes the platform's validation pass and all imported items match the exported ones.
2. **Given** the roundtrip scenario, **When** interrupted anywhere and resumed, **Then** the result is identical to a clean run.

---

### User Story 4 — Polymorphic endpoint config deserialization routes to the correct connector type (Priority: P2)

An operator writes a scenario config file with `Source.Type: "Simulated"` containing generator-specific fields, or with `Source.Type: "AzureDevOpsServices"` containing a URL and authentication. The config is deserialized into the correct strongly-typed options object — the wrong type is never silently returned.

**Why this priority**: Foundation for all connector routing. If deserialization produces the wrong type, everything downstream breaks.

**Independent Test**: Unit tests deserialize three config files (ADO, TFS, Simulated) and assert the resulting `MigrationEndpointOptions` is the expected derived type with the correct field values.

**Acceptance Scenarios**:

1. **Given** a config with `Source.Type: "AzureDevOpsServices"` and a `Url` field, **When** deserialized, **Then** the result is `AzureDevOpsEndpointOptions` with `Url` populated.
2. **Given** a config with `Source.Type: "Simulated"` and a `Generator.Projects` array, **When** deserialized, **Then** the result is `SimulatedEndpointOptions` with `Generator.Projects` populated and no `Url` or `Authentication` fields present.
3. **Given** a config with an unknown `Source.Type: "UnknownConnector"`, **When** deserialized, **Then** a clear, actionable error is thrown naming the unrecognised type — no silent null.
4. **Given** two connectors try to register the same discriminator key, **When** the DI container is built, **Then** an `InvalidOperationException` is thrown at startup identifying the conflicting key.

---

### User Story 5 — ADO assembly has zero knowledge of Simulated (Priority: P2)

After this feature, `AzureDevOpsWorkItemImportTargetFactory` no longer contains a `"Simulated"` routing branch and `AzureDevOpsResolutionStrategyFactory` no longer inspects `SimulatedWorkItemImportTarget`. Each connector registers its own keyed factory via DI.

**Why this priority**: Architectural correctness required before adding the new assembly — otherwise the new assembly creates a second path and the leaks remain.

**Independent Test**: Remove the `"Simulated"` branch from `AzureDevOpsWorkItemImportTargetFactory` and the `is SimulatedWorkItemImportTarget` check from `AzureDevOpsResolutionStrategyFactory`. Existing simulated import scenario still passes.

**Acceptance Scenarios**:

1. **Given** the Simulated assembly is registered via `AddSimulatedWorkItemImport()`, **When** a job with `Target.Type: "Simulated"` runs, **Then** `SimulatedWorkItemImportTarget` is resolved via keyed DI without any code in `Infrastructure.AzureDevOps` being involved.
2. **Given** the Simulated assembly is not registered, **When** a job with `Target.Type: "Simulated"` is submitted, **Then** the error is a clear "no keyed service registered for 'Simulated'" — not a null reference or silent no-op.

---

### Edge Cases

- What happens when `Generator.Projects` is empty? — The export job completes immediately with zero items written and a log entry explaining the empty config.
- What happens when `Generator.Projects[].WorkItemTypes[].Count` is 0? — That work item type is skipped; other types in the same project are still generated.
- What happens when `RevisionsPerItem` is 0? — Fail fast with a validation error at job startup; do not silently produce zero-revision items.
- What happens when the Simulated assembly is loaded but the config `Type` is `"AzureDevOpsServices"`? — ADO factory is used as normal; Simulated registration is inert.
- What happens when two connector assemblies register the same discriminator key? — `EndpointOptionsTypeRegistry` throws on duplicate key registration at startup, not silently at deserialisation time.
- What happens when `CatalogService` (moved to `Infrastructure`) is used with a connector that does not implement `IProjectDiscoveryService`? — A clear DI resolution failure at startup, not a runtime null reference.

---

## Requirements *(mandatory)*

### Functional Requirements

**Polymorphic endpoint config**

- **FR-001**: `MigrationEndpointOptions` MUST be an abstract base class with only a `Type` string property. No STJ attributes, no connector-specific fields.
- **FR-002**: `OrganisationEntry` MUST follow the same pattern: abstract base with `Type`, `Projects[]`, and `Enabled` only.
- **FR-003**: Each connector assembly MUST define its own derived `*EndpointOptions` and `*OrganisationEntry` types, inheriting from the respective base.
- **FR-004**: Polymorphic JSON deserialization MUST be driven by an `EndpointOptionsTypeRegistry` in shared `Infrastructure`, populated at DI startup by each connector's extension method.
- **FR-005**: An unknown `type` discriminator MUST produce a clear, actionable error at deserialization time naming the unrecognised value.
- **FR-006**: `OrganisationEndpoint` MUST be moved from `Abstractions` to `Infrastructure.AzureDevOps` and become ADO-internal. It MUST NOT be referenced by `IWorkItemRevisionSourceFactory` or any other shared interface.

**Factory interfaces**

- **FR-007**: `IWorkItemRevisionSourceFactory.CreateAsync` MUST accept `MigrationEndpointOptions` (base type) and a `CancellationToken`. No decomposed scalar parameters.
- **FR-008**: `IWorkItemImportTargetFactory.CreateAsync` MUST accept `MigrationEndpointOptions` and a `CancellationToken`. No decomposed scalar parameters.
- **FR-009**: Each connector factory MUST cast the base to its expected derived type and throw `ArgumentException` with the actual received type name if the cast fails.

**`WorkItemsModule` connector-agnostic cleanup**

- **FR-010**: `WorkItemsModule.ExportAsync` MUST NOT read `job.Source.Url`, `job.Source.Project`, or `job.Source.Authentication`. It MUST pass `job.Source` as-is to `IWorkItemRevisionSourceFactory.CreateAsync`.
- **FR-011**: `WorkItemsModule.ImportAsync` MUST NOT read connector-specific fields from `job.Target`. It MUST pass `job.Target` as-is to `IWorkItemImportTargetFactory.CreateAsync`.

**Boundary leak fixes**

- **FR-012**: `AzureDevOpsWorkItemImportTargetFactory` MUST NOT contain any routing logic for `"Simulated"`. It MUST be registered under its own DI key (`"AzureDevOpsServices"`) and resolve only `AzureDevOpsWorkItemImportTarget`.
- **FR-013**: `AzureDevOpsResolutionStrategyFactory` MUST NOT reference `SimulatedWorkItemImportTarget`. It MUST be registered under key `"AzureDevOpsServices"` and never inspect the target type from another connector assembly.

**`CatalogService` relocation**

- **FR-014**: `CatalogService` MUST reside in `Infrastructure` (shared), not `Infrastructure.AzureDevOps`. It depends only on `IProjectDiscoveryService` and `IWorkItemDiscoveryService`, both of which are Abstractions interfaces.

**`Infrastructure.Simulated` assembly**

- **FR-015**: `SimulatedWorkItemRevisionSource` MUST generate `WorkItemRevision` records lazily (streaming). No buffering into a list or array before returning.
- **FR-016**: `SimulatedWorkItemRevisionSourceFactory` MUST cast the incoming `MigrationEndpointOptions` to `SimulatedEndpointOptions` and drive the generator from `Generator.Projects`.
- **FR-017**: `SimulatedWorkItemImportTarget` MUST be moved from `Infrastructure` to `Infrastructure.Simulated`.
- **FR-018**: `SimulatedWorkItemImportTargetFactory` MUST be registered under DI key `"Simulated"`.
- **FR-019**: `SimulatedResolutionStrategyFactory` MUST be registered under DI key `"Simulated"` and MUST always return `NullResolutionStrategy`.
- **FR-020**: `SimulatedWorkItemLinkAnalysisService` MUST be registered under DI key `"Simulated"`, resolving the existing `NotSupportedException` in `DependencyDiscoveryService`.
- **FR-021**: `SimulatedProjectDiscoveryService` MUST return project names from `SimulatedEndpointOptions.Generator.Projects`.
- **FR-022**: `SimulatedWorkItemDiscoveryService` MUST return deterministic work item counts derived from `Generator.Projects[].WorkItemTypes[].Count` without any network calls.
- **FR-023**: `SimulatedAttachmentBinarySource` MUST return deterministic bytes for a given attachment ID and filename — same inputs always produce the same output.
- **FR-024**: `SimulatedEmbeddedImageDownloader` MUST return a minimal valid placeholder byte stream for any URL.
- **FR-025**: `SimulatedWorkItemCommentSourceFactory` MUST produce synthetic comments proportional to the config. If `HasComments: false`, it returns an empty stream.
- **FR-026**: `SimulatedServiceCollectionExtensions` MUST expose `AddSimulatedWorkItemExport()`, `AddSimulatedWorkItemImport()`, and `AddSimulatedDependencyAnalysis()`.

**Scenario configs and launch profiles**

- **FR-027**: A scenario config `scenarios/queue-export-workitems-simulated-source.json` MUST exist exercising the full export pipeline with a Simulated source.
- **FR-028**: A corresponding `.vscode/launch.json` debug profile MUST exist for each new scenario config added.

### Key Entities

- **`MigrationEndpointOptions`** (abstract, `Abstractions`): `Type` discriminator only. Parent of all connector endpoint config types.
- **`AzureDevOpsEndpointOptions`** (`Infrastructure.AzureDevOps`): Url, ResolvedUrl, Project, ApiVersion, Authentication.
- **`TeamFoundationServerEndpointOptions`** (`Infrastructure.TfsObjectModel`): Url, ResolvedUrl, Project, ApiVersion, Authentication.
- **`SimulatedEndpointOptions`** (`Infrastructure.Simulated`): `Generator` (SimulatedGeneratorConfig).
- **`SimulatedGeneratorConfig`**: Projects array of `SimulatedProjectConfig`.
- **`SimulatedProjectConfig`**: Name, WorkItemTypes[], LinkTopology, AttachmentSizeKb, HasComments, HasEmbeddedImages.
- **`SimulatedWorkItemTypeConfig`**: Type (string), Count, RevisionsPerItem.
- **`EndpointOptionsTypeRegistry`** (`Infrastructure`): Maps discriminator string → `System.Type`. Populated at DI startup by each connector's `AddXxx()` extension method.
- **`PolymorphicEndpointOptionsConverter`** (`Infrastructure`): `JsonConverter<MigrationEndpointOptions>` that reads the registry to dispatch to the correct derived type.
- **`OrganisationEntry`** (abstract, `Abstractions`): `Type`, `Projects[]`, `Enabled`. Parent of all connector org entry config types.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A full simulated export of 500 work items × 5 revisions with attachments and comments completes without any network calls and without loading all revisions into memory simultaneously.
- **SC-002**: The simulated export + import roundtrip can be executed by any developer on any machine with zero credentials configured.
- **SC-003**: All existing ADO and fixture-based system tests continue to pass after the boundary refactors with no changes to their test config files.
- **SC-004**: Adding a fourth connector requires zero changes to `Abstractions`, `Infrastructure`, `Infrastructure.AzureDevOps`, `Infrastructure.TfsObjectModel`, or `Infrastructure.Simulated` — only a new assembly and its registration.
- **SC-005**: No `throw new NotImplementedException()` or equivalent placeholder exists in any reachable code path when the feature ships.
- **SC-006**: `dotnet clean && dotnet build --no-incremental` passes with zero warnings related to this change.
- **SC-007**: `dotnet test` passes fully, including at least one new `[TestCategory("SystemTest")]` asserting the simulated export scenario produces a valid package.

---

## Assumptions

- `Abstractions` gains no new NuGet dependencies from this feature. `EndpointOptionsTypeRegistry` and converters live in shared `Infrastructure`, which already has `System.Text.Json`.
- `TeamFoundationServerEndpointOptions` is placed in `Infrastructure.TfsObjectModel`. Given the .NET 4 constraints on that assembly and the TFS best-effort rule, full polymorphic registration may not work identically there — this is acceptable.
- The `IWorkItemCommentSourceFactory` interface already exists or will be introduced as part of this work — the Simulated implementation ships in the same change.
- `NullResolutionStrategy` remains in shared `Infrastructure` — it is a generic always-create-new strategy, not Simulated-specific.
- The `wiql` query parameter on `IWorkItemRevisionSourceFactory.CreateAsync` is removed when the signature changes to accept `MigrationEndpointOptions`. The ADO connector internalises the WIQL query — it is not passed via endpoint options.
- Architecture docs `docs/architecture.md` and `docs/module-development-guide.md` are confirmed accurate and consistent with this spec. No doc updates are required from the spec itself — the `speckit.implement` phase will update them as discrepancies are resolved.
- `analysis/Simulated.md` is the canonical design reference for the implementation phase and MUST be read by the implementing agent before writing any code.

