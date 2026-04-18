# Research: Simulated Infrastructure Connector

**Feature**: 017-simulated-infrastructure
**Phase**: 0 — Unknowns resolved

All research items were resolved from codebase analysis and `analysis/Simulated.md`. No external research was required.

## Summary of Resolved Decisions

| Topic | Decision | Rationale |
|---|---|---|
| JSON polymorphism strategy | `EndpointOptionsTypeRegistry` + custom `JsonConverter` in `Infrastructure` | Avoids circular dependency from `[JsonDerivedType]` on base; zero changes per new connector |
| `net481` targeting | `Infrastructure.Simulated` is `net10.0` only | TFS subprocess never calls Simulated; same as `Infrastructure.AzureDevOps` |
| Config schema upgrader | No upgrader needed | JSON shape unchanged; C# type changes are transparent to consumers |
| `OrganisationEndpoint` move | To `Infrastructure.AzureDevOps`, `internal` | Only referenced in ADO-specific code |
| Generator determinism | Seeded `System.Random` per item + fixed base date | Required by SC-002; independence between items |

## Research Item: `[JsonDerivedType]` circular dependency

**Question**: Can `MigrationEndpointOptions` carry `[JsonDerivedType(typeof(AzureDevOpsEndpointOptions))]`?

**Finding**: No. `Infrastructure.AzureDevOps` references `Abstractions`. Adding a reference from `Abstractions` to `Infrastructure.AzureDevOps` creates a circular dependency. `System.Text.Json` polymorphism via attributes requires the attributes to be present on the type declaration, which means compile-time assembly references are mandatory.

**Resolution**: Use `EndpointOptionsTypeRegistry` in shared `Infrastructure`. Populated at DI startup by each connector's `AddXxx()` method. The `PolymorphicEndpointOptionsConverter` reads the registry at deserialisation time. No compile-time attribute on the base is needed.

## Research Item: Existing `SimulatedWorkItemImportTarget` call sites

**Question**: Where is `SimulatedWorkItemImportTarget` currently used? Are there call sites outside `Infrastructure.AzureDevOps`?

**Finding**: Two references found:
1. `AzureDevOpsWorkItemImportTargetFactory.CreateAsync` — constructs `new SimulatedWorkItemImportTarget()` when `targetType == "Simulated"`.
2. `AzureDevOpsResolutionStrategyFactory.CreateAsync` — type-checks `if (target is SimulatedWorkItemImportTarget)`.

Both are the boundary leaks documented in `analysis/Simulated.md`. No other call sites. Removal of both is safe once keyed DI is in place.

## Research Item: `CatalogService` ADO SDK dependencies

**Question**: Does `CatalogService` in `Infrastructure.AzureDevOps` use any ADO SDK types?

**Finding**: `CatalogService` uses only `IProjectDiscoveryService` and `IWorkItemDiscoveryService` — both `Abstractions` interfaces. No `Microsoft.TeamFoundationServer.Client` or `Microsoft.VisualStudio.Services.Client` types appear in the file. Move to `Infrastructure` (shared) is safe with zero code changes.

## Research Item: `IWorkItemCommentSourceFactory` existence

**Question**: Does `IWorkItemCommentSourceFactory` already exist in `Abstractions`?

**Finding**: The interface exists as `IWorkItemCommentSourceFactory` in `Abstractions`. `AzureDevOpsWorkItemCommentSourceFactory` implements it. The Simulated implementation can follow the same pattern.

## Research Item: `System.Text.Json` version on `net481`

**Question**: Is `System.Text.Json` available on `net481` for `Infrastructure` (which is multi-targeted)?

**Finding**: `Infrastructure.csproj` already includes `<PackageReference Include="System.Text.Json" VersionOverride="9.0.5" />` under `Condition="'$(TargetFramework)' == 'net481'"`. The `EndpointOptionsTypeRegistry` and converters can be gated with `#if !NET481` or by placing them in a `net10.0`-only conditional compile group. The simpler approach is to wrap the converter registration in a `Condition="'$(TargetFramework)' != 'net481'"` compile group — the TFS subprocess never deserialises job config JSON.

## Research Item: Generator field value determinism

**Question**: What fields need to be populated on `WorkItemRevision` for the export pipeline to produce a valid package?

**Finding from codebase analysis**: `WorkItemRevision` requires at minimum: `WorkItemId`, `RevisionIndex`, `RevisedDate`, `Fields` (dictionary), `Links` (list), `Attachments` (list). The `WorkItemExportOrchestrator` writes these to `revision.json` via `IArtefactStore`. The generator must produce values for all mandatory fields. Optional extension fields (comments, embedded images, attachments) are populated only if the corresponding extension is configured in the job.

**Resolution**: Generator produces a minimal but valid field set: `System.Id`, `System.Title`, `System.WorkItemType`, `System.State`, `System.CreatedDate`, `System.ChangedDate`, `System.AreaPath`, `System.IterationPath`, `System.Rev`. All values are deterministic from `(projectName, workItemType, workItemId, revisionIndex)`.
