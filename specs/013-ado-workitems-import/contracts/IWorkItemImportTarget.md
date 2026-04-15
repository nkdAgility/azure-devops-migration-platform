# Contract: IWorkItemImportTarget

**Feature**: 013-ado-workitems-import  
**Date**: 2026-04-15

---

## Purpose

`IWorkItemImportTarget` is the abstraction through which import modules write to the target system (Azure DevOps, Simulated, or future targets). It mirrors `IWorkItemRevisionSource` on the export side.

All Azure DevOps SDK calls for creating work items, updating fields, managing links, uploading attachments, and creating comments are wrapped behind this interface. Module and orchestrator code never reference `WorkItemTrackingHttpClient` or any other SDK type directly.

---

## Interface Definition

**Location**: `DevOpsMigrationPlatform.Abstractions/Services/IWorkItemImportTarget.cs`

```csharp
namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Abstraction for writing work items to a target system during import.
/// All target system SDK calls are behind this interface.
/// Implementations: AzureDevOpsWorkItemImportTarget, SimulatedWorkItemImportTarget.
/// </summary>
public interface IWorkItemImportTarget
{
    /// <summary>
    /// Create a new work item in the target project.
    /// </summary>
    /// <param name="workItemType">The work item type name (e.g. "Bug", "Task").</param>
    /// <param name="fields">Initial field values to set.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The created work item's target ID and creation status.</returns>
    Task<ImportedWorkItemResult> CreateWorkItemAsync(
        string workItemType,
        IReadOnlyList<WorkItemField> fields,
        CancellationToken ct);

    /// <summary>
    /// Update an existing work item's fields.
    /// </summary>
    /// <param name="targetWorkItemId">The target work item ID.</param>
    /// <param name="fields">Field values to apply.</param>
    /// <param name="ct">Cancellation token.</param>
    Task UpdateFieldsAsync(
        int targetWorkItemId,
        IReadOnlyList<WorkItemField> fields,
        CancellationToken ct);

    /// <summary>
    /// Add links to a target work item, skipping any that already exist.
    /// </summary>
    /// <param name="targetWorkItemId">The target work item ID.</param>
    /// <param name="relatedLinks">Related work item links (source IDs must be resolved to target IDs by caller).</param>
    /// <param name="externalLinks">External links.</param>
    /// <param name="hyperlinks">Hyperlinks.</param>
    /// <param name="ct">Cancellation token.</param>
    Task AddLinksAsync(
        int targetWorkItemId,
        IReadOnlyList<RelatedWorkItemLink> relatedLinks,
        IReadOnlyList<ExternalWorkItemLink> externalLinks,
        IReadOnlyList<HyperlinkWorkItemLink> hyperlinks,
        CancellationToken ct);

    /// <summary>
    /// Upload an attachment binary and attach it to a work item.
    /// </summary>
    /// <param name="targetWorkItemId">The target work item ID.</param>
    /// <param name="fileName">The attachment file name.</param>
    /// <param name="content">The attachment binary stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The target attachment identifier (URL or GUID).</returns>
    Task<string> UploadAttachmentAsync(
        int targetWorkItemId,
        string fileName,
        Stream content,
        CancellationToken ct);

    /// <summary>
    /// Upload an embedded image binary to the target.
    /// </summary>
    /// <param name="fileName">The image file name.</param>
    /// <param name="content">The image binary stream.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The target URL where the image is now accessible.</returns>
    Task<string> UploadEmbeddedImageAsync(
        string fileName,
        Stream content,
        CancellationToken ct);

    /// <summary>
    /// Create a comment on a target work item.
    /// </summary>
    /// <param name="targetWorkItemId">The target work item ID.</param>
    /// <param name="text">The comment text (HTML or Markdown).</param>
    /// <param name="ct">Cancellation token.</param>
    Task CreateCommentAsync(
        int targetWorkItemId,
        string text,
        CancellationToken ct);

    /// <summary>
    /// Query existing relations on a target work item (for idempotency checks in Stage C).
    /// </summary>
    /// <param name="targetWorkItemId">The target work item ID.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The current set of relations on the target work item.</returns>
    Task<WorkItemRelations> GetExistingRelationsAsync(
        int targetWorkItemId,
        CancellationToken ct);
}
```

---

## Supporting Types

### IWorkItemImportTargetFactory

**Location**: `DevOpsMigrationPlatform.Abstractions/Services/IWorkItemImportTargetFactory.cs`

```csharp
/// <summary>
/// Factory for creating IWorkItemImportTarget instances from job configuration.
/// </summary>
public interface IWorkItemImportTargetFactory
{
    Task<IWorkItemImportTarget> CreateAsync(
        string orgUrl,
        string project,
        string accessToken,
        CancellationToken ct);
}
```

### IIdMapStore

**Location**: `DevOpsMigrationPlatform.Abstractions/Services/IIdMapStore.cs`

```csharp
/// <summary>
/// Abstraction for source-to-target work item ID and attachment ID mapping storage.
/// Backed by SQLite (Checkpoints/idmap.db) in production.
/// </summary>
public interface IIdMapStore : IAsyncDisposable
{
    Task InitializeAsync(CancellationToken ct);
    Task<int?> GetTargetWorkItemIdAsync(int sourceId, CancellationToken ct);
    Task SetWorkItemMappingAsync(int sourceId, int targetId, CancellationToken ct);
    Task<string?> GetAttachmentIdAsync(int sourceWorkItemId, int revisionIndex, string relativePath, CancellationToken ct);
    Task SetAttachmentMappingAsync(int sourceWorkItemId, int revisionIndex, string relativePath, string targetAttachmentId, CancellationToken ct);
    Task SeedWorkItemMappingsAsync(IAsyncEnumerable<IdMapEntry> entries, CancellationToken ct);
}
```

### IWorkItemResolutionStrategy

**Location**: `DevOpsMigrationPlatform.Abstractions/Services/IWorkItemResolutionStrategy.cs`

```csharp
/// <summary>
/// Strategy for discovering existing source-to-target work item mappings
/// from the target system during import startup.
/// </summary>
public interface IWorkItemResolutionStrategy
{
    /// <summary>
    /// Seed the idmap from the target system at import startup.
    /// </summary>
    Task SeedAsync(IIdMapStore idMapStore, CancellationToken ct);

    /// <summary>
    /// Attempt to resolve a single source work item ID against the target
    /// as a fallback when not found in the idmap.
    /// Returns the target ID if found, null otherwise.
    /// </summary>
    Task<int?> ResolveSingleAsync(int sourceWorkItemId, CancellationToken ct);

    /// <summary>
    /// After creating a new work item in the target, write the provenance marker
    /// (custom field value or hyperlink) so the mapping is discoverable in future runs.
    /// </summary>
    Task WriteProvenanceAsync(int sourceWorkItemId, int targetWorkItemId, CancellationToken ct);
}
```

### WorkItemRelations (record)

**Location**: `DevOpsMigrationPlatform.Abstractions/Models/WorkItemRelations.cs`

```csharp
/// <summary>
/// Relations currently present on a target work item (for idempotency checks).
/// </summary>
public record WorkItemRelations
{
    public IReadOnlyList<RelatedWorkItemLink> RelatedLinks { get; init; } = Array.Empty<RelatedWorkItemLink>();
    public IReadOnlyList<ExternalWorkItemLink> ExternalLinks { get; init; } = Array.Empty<ExternalWorkItemLink>();
    public IReadOnlyList<HyperlinkWorkItemLink> Hyperlinks { get; init; } = Array.Empty<HyperlinkWorkItemLink>();
}
```

---

## Implementations

| Interface | Implementation | Project | Description |
|-----------|---------------|---------|-------------|
| `IWorkItemImportTarget` | `AzureDevOpsWorkItemImportTarget` | `Infrastructure.AzureDevOps` | Wraps `WorkItemTrackingHttpClient` |
| `IWorkItemImportTargetFactory` | `AzureDevOpsWorkItemImportTargetFactory` | `Infrastructure.AzureDevOps` | Creates target instances from job config |
| `IIdMapStore` | `SqliteIdMapStore` | `Infrastructure` | SQLite-backed `Checkpoints/idmap.db` |
| `IWorkItemResolutionStrategy` | `NullResolutionStrategy` | `Infrastructure.AzureDevOps` | No target query (default) |
| `IWorkItemResolutionStrategy` | `TargetFieldResolutionStrategy` | `Infrastructure.AzureDevOps` | WIQL custom field query |
| `IWorkItemResolutionStrategy` | `TargetHyperlinkResolutionStrategy` | `Infrastructure.AzureDevOps` | Hyperlink scan |

---

## DI Registration

All import services are registered via a dedicated extension method:

```csharp
// In DevOpsMigrationPlatform.Infrastructure.AzureDevOps
public static class ImportServiceCollectionExtensions
{
    public static IServiceCollection AddAzureDevOpsImportServices(
        this IServiceCollection services)
    {
        services.AddSingleton<IWorkItemImportTargetFactory, AzureDevOpsWorkItemImportTargetFactory>();
        services.AddSingleton<IIdMapStore, SqliteIdMapStore>();
        // Resolution strategy registered based on config (keyed services or factory pattern)
        return services;
    }
}
```
