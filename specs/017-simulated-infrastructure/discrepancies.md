# Architecture Discrepancies

**Feature**: Simulated Infrastructure Connector
**Flagged by**: speckit.specify + speckit.analyze
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### 1. Factory interface signatures use decomposed scalars, not base endpoint options

- **Source doc**: `docs/modules.md`
- **Section**: WorkItemsModule / Factory interfaces
- **Issue**: `IWorkItemRevisionSourceFactory.CreateAsync` currently accepts `(OrganisationEndpoint endpoint, string project, string wiqlQuery, CancellationToken)` and `IWorkItemImportTargetFactory.CreateAsync` accepts `(string targetType, string orgUrl, string project, string accessToken, CancellationToken)`. The spec requires both to accept `MigrationEndpointOptions` (base type) + `CancellationToken`. The docs do not yet reflect this change.
- **Suggested update**: Update the interface signatures in `docs/modules.md` to show `CreateAsync(MigrationEndpointOptions endpoint, CancellationToken ct)` for both factories.

### 2. `OrganisationEndpoint` is documented as an Abstractions type

- **Source doc**: `docs/architecture.md`
- **Section**: Key types / Abstractions layer
- **Issue**: `OrganisationEndpoint` is listed as a shared type in `Abstractions`. The spec moves it to `Infrastructure.AzureDevOps` (ADO-internal). Once moved it is no longer a shared contract.
- **Suggested update**: Remove `OrganisationEndpoint` from the Abstractions type table in `docs/architecture.md`. Add a note that ADO-specific connection details are encapsulated in `AzureDevOpsEndpointOptions` and resolved internally by the ADO connector factories.

### 3. Polymorphic endpoint config model is not documented anywhere

- **Source doc**: `docs/configuration.md`
- **Section**: Job config / Source and Target sections
- **Issue**: The docs describe `Source.Type` and `Source.Url` as flat scalars. There is no mention of the polymorphic options model, `EndpointOptionsTypeRegistry`, or `PolymorphicEndpointOptionsConverter`. Operators reading the docs have no guidance on how the config discriminator works.
- **Suggested update**: Add a "Polymorphic Endpoint Config" section to `docs/configuration.md` explaining the `type` discriminator, how each connector extends the base config with its own fields, and the fact that unknown types produce a startup error.

### 4. `CatalogService` is documented as part of ADO infrastructure

- **Source doc**: `docs/architecture.md`
- **Section**: Infrastructure layer / AzureDevOps sub-assembly
- **Issue**: `CatalogService` is listed under `Infrastructure.AzureDevOps`. The spec moves it to shared `Infrastructure`.
- **Suggested update**: Move `CatalogService` in the architecture doc to the shared `Infrastructure` component list. Note it is connector-agnostic and depends only on `IProjectDiscoveryService` and `IWorkItemDiscoveryService`.

### 5. `Infrastructure.Simulated` assembly is not listed in the project/assembly table

- **Source doc**: `docs/architecture.md`
- **Section**: Assembly list / Connector assemblies
- **Issue**: `Infrastructure.Simulated` does not appear in any assembly table. It needs to be listed alongside `Infrastructure.AzureDevOps` and `Infrastructure.TfsObjectModel`.
- **Suggested update**: Add `DevOpsMigrationPlatform.Infrastructure.Simulated` to the assembly table with description: "Config-driven synthetic connector for offline testing. Implements all source and target interfaces with deterministic generated data."

### 6. `SimulatedWorkItemImportTarget` is in the wrong assembly in the source

- **Source doc**: `docs/architecture.md`
- **Section**: Infrastructure layer / Infrastructure (shared) sub-assembly
- **Issue**: `SimulatedWorkItemImportTarget` is currently in `Infrastructure` (shared). It is Simulated-specific and must live in `Infrastructure.Simulated`.
- **Suggested update**: In the assembly table, remove `SimulatedWorkItemImportTarget` from shared `Infrastructure` and add it to `Infrastructure.Simulated`.
