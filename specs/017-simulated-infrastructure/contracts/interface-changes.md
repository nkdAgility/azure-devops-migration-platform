# Interface Contracts: Simulated Infrastructure Connector

**Feature**: 017-simulated-infrastructure
**Phase**: 1 — Design & Contracts

---

## Changed Interfaces (`Abstractions`)

### `IWorkItemRevisionSourceFactory`

**Before**:
```csharp
Task<IWorkItemRevisionSource> CreateAsync(
    OrganisationEndpoint endpoint,
    string project,
    string wiqlQuery,
    CancellationToken cancellationToken);
```

**After**:
```csharp
Task<IWorkItemRevisionSource> CreateAsync(
    MigrationEndpointOptions endpoint,
    CancellationToken cancellationToken);
```

**Impact**:
- All implementations must be updated: `AzureDevOpsWorkItemRevisionSourceFactory`, `SimulatedWorkItemRevisionSourceFactory` (new).
- `WorkItemsModule.ExportAsync` removes its construction of `OrganisationEndpoint` and passes `job.Source` directly.
- `OrganisationEndpoint` is no longer part of the shared contract — moves to `Infrastructure.AzureDevOps` (internal).

---

### `IWorkItemImportTargetFactory`

**Before**:
```csharp
Task<IWorkItemImportTarget> CreateAsync(
    string targetType,
    string orgUrl,
    string project,
    string accessToken,
    CancellationToken ct);
```

**After**:
```csharp
Task<IWorkItemImportTarget> CreateAsync(
    MigrationEndpointOptions endpoint,
    CancellationToken ct);
```

**Impact**:
- All implementations must be updated: `AzureDevOpsWorkItemImportTargetFactory`, `SimulatedWorkItemImportTargetFactory` (new).
- `WorkItemsModule.ImportAsync` passes `job.Target` directly — no field reads.
- Implementations are now keyed services resolved by `job.Target.Type` discriminator.

---

## New Types in `Infrastructure`

### `EndpointOptionsTypeRegistry`

```csharp
public sealed class EndpointOptionsTypeRegistry
{
    public void Register(string key, Type type);
    public bool TryGetType(string key, [NotNullWhen(true)] out Type? type);
}
```

Registered as a singleton. Populated by `AddEndpointOptionsType` / `AddOrganisationEntryType` extension methods.

---

### `PolymorphicEndpointOptionsConverter`

```csharp
public sealed class PolymorphicEndpointOptionsConverter : JsonConverter<MigrationEndpointOptions>
{
    // Constructor: receives EndpointOptionsTypeRegistry from DI
    // Reads "type" field first; looks up concrete type in registry; deserialises
}
```

---

### `PolymorphicOrganisationEntryConverter`

Same pattern as `PolymorphicEndpointOptionsConverter` but for `OrganisationEntry`.

---

## New Types in `Infrastructure.Simulated`

### `SimulatedServiceCollectionExtensions`

```csharp
public static class SimulatedServiceCollectionExtensions
{
    // Registers: SimulatedEndpointOptions type, SimulatedOrganisationEntry type,
    //            SimulatedWorkItemRevisionSourceFactory, SimulatedProjectDiscoveryService,
    //            SimulatedWorkItemDiscoveryService, SimulatedAttachmentBinarySource,
    //            SimulatedEmbeddedImageDownloader, SimulatedWorkItemCommentSourceFactory
    public static IServiceCollection AddSimulatedWorkItemExport(this IServiceCollection services);

    // Registers: SimulatedWorkItemImportTargetFactory (keyed "Simulated"),
    //            SimulatedResolutionStrategyFactory (keyed "Simulated")
    public static IServiceCollection AddSimulatedWorkItemImport(this IServiceCollection services);

    // Registers: SimulatedWorkItemLinkAnalysisService (keyed "Simulated")
    public static IServiceCollection AddSimulatedDependencyAnalysis(this IServiceCollection services);
}
```

---

## Scenario Config Contract

New scenario config format for Simulated source:

```json
{
  "Source": {
    "Type": "Simulated",
    "Generator": {
      "Projects": [
        {
          "Name": "ProjectA",
          "WorkItemTypes": [
            { "Type": "User Story", "Count": 10, "RevisionsPerItem": 3 },
            { "Type": "Task", "Count": 20, "RevisionsPerItem": 2 }
          ],
          "LinkTopology": "Tree",
          "AttachmentSizeKb": 0,
          "HasComments": false
        }
      ]
    }
  },
  "Target": {
    "Type": "Simulated"
  }
}
```

Note: `SimulatedEndpointOptions` has no `Url`, `Project`, or `Authentication` fields. The JSON is shorter than ADO configs. Any `Url` or `Authentication` fields present in the JSON are silently ignored by the converter (not mapped to the type).
