# Data Model: Close DSL Migration Gaps

**Feature**: `specs/038-close-dsl-gaps/spec.md`
**Date**: 2026-06-03

---

## New Abstractions

### IIdentityAdapter

**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Identity`
**Project**: `DevOpsMigrationPlatform.Abstractions.Agent`
**Role**: Adapter — connector-specific abstraction for querying the live target tenant during PrepareAsync.

```csharp
public interface IIdentityAdapter
{
    Task<IReadOnlyList<IdentityCandidate>> FindByUpnAsync(
        string upn,
        string projectName,
        CancellationToken ct);

    Task<IReadOnlyList<IdentityCandidate>> FindByDisplayNameAsync(
        string displayName,
        string projectName,
        CancellationToken ct);
}
```

**Implementations required (FR-005)**:
- `AzureDevOpsIdentityAdapter` — ADO Graph API (`_apis/graph/users`)
- `TfsIdentityAdapter` — TFS Identity Service REST (`_apis/identities`); returns empty list with structured warning when search not supported
- `SimulatedIdentityAdapter` — in-memory deterministic store matching the `SimulatedIdentitySource` data set

---

### IdentityCandidate

**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Identity`
**Project**: `DevOpsMigrationPlatform.Abstractions.Agent`
**Kind**: Immutable record

```csharp
public sealed record IdentityCandidate(
    string Descriptor,
    string? Upn,
    string? DisplayName);
```

---

### IIdentityMatchingStrategy

**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Identity`
**Project**: `DevOpsMigrationPlatform.Abstractions.Agent`
**Role**: Strategy — pluggable matching variant applied by the Orchestrator during PrepareAsync.

```csharp
public interface IIdentityMatchingStrategy
{
    /// <summary>Returns the resolved target descriptor, or null if this strategy cannot match.</summary>
    string? Match(
        string sourceIdentity,
        string sourceDisplayName,
        IReadOnlyList<IdentityCandidate> candidates,
        ILogger logger);
}
```

**Implementations required**:
- `UpnIdentityMatchingStrategy` — exact UPN match (case-insensitive)
- `DisplayNameIdentityMatchingStrategy` — Unicode-NFC normalised, case-insensitive exact match; ambiguous match (>1 result) returns null and logs a structured warning

---

### IIdentityTranslationTool

**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Tools`
**Project**: `DevOpsMigrationPlatform.Abstractions.Agent`
**Role**: Tool — optional cross-cutting seam injected into all consumers. Replaces `IIdentityLookupTool`.

```csharp
public interface IIdentityTranslationTool
{
    bool IsEnabled { get; }

    /// <summary>
    /// Returns the resolved target identity descriptor for the given source descriptor.
    /// Synchronous — reads from the Orchestrator cache populated during PrepareAsync.
    /// Returns the source descriptor unchanged when IsEnabled is false.
    /// </summary>
    string Translate(string sourceIdentity);
}
```

**Configuration section**: `MigrationPlatform:Tools:IdentityTranslation`

---

### IdentityTranslationOptions

**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Tools`
**Project**: `DevOpsMigrationPlatform.Abstractions.Agent`

```csharp
public sealed class IdentityTranslationOptions
{
    public static string SectionName => "MigrationPlatform:Tools:IdentityTranslation";
    public bool IsEnabled { get; init; } = true;

    /// <summary>
    /// Optional fallback identity (carried over from <c>IdentityLookupOptions.DefaultIdentity</c>).
    /// When null/empty, <c>Translate()</c> returns the source identity unchanged.
    /// Target-existence validation is owned by <c>PrepareAsync</c>, not by this default.
    /// </summary>
    public string? DefaultIdentity { get; init; }
}
```

---

## Modified Abstractions

### IIdentitiesOrchestrator — PrepareAsync added

**Change**: Add `PrepareAsync` method. Remove `IIdentityLookupTool?` parameter from `ImportAsync`.

```csharp
public interface IIdentitiesOrchestrator
{
    // NEW — FR-001, FR-002
    Task PrepareAsync(
        string projectName,
        ImportContext context,
        CancellationToken ct);

    Task ExportAsync(
        IIdentitySource identitySource,
        ExportContext context,
        string organisation,
        string project,
        ICheckpointingServiceFactory? checkpointingFactory,
        CancellationToken ct);

    // CHANGED — IIdentityLookupTool? parameter removed (FR-016)
    Task ImportAsync(
        ImportContext context,
        string organisation,
        string project,
        ICheckpointingServiceFactory? checkpointingFactory,
        CancellationToken ct);

    Task ValidateAsync(
        IPackageAccess package,
        string organisation,
        string project,
        ValidationContext context,
        CancellationToken ct);
}
```

---

## Deleted Abstractions

### IIdentityLookupTool (FR-016)

- **Delete**: `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IIdentityLookupTool.cs`
- **Delete**: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/IdentityLookup/IdentityLookupTool.cs`
- **Delete**: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/IdentityLookup/IdentityLookupToolServiceCollectionExtensions.cs`
- **Callers to update**: `TeamImportOrchestrator`, `RevisionFolderProcessor`, `WorkItemsModule`, `IdentitiesModule`, `IdentityServiceCollectionExtensions`

---

## Runtime Data (prepare-report.json)

Written by `IdentitiesOrchestrator.PrepareAsync` to the package under `Identities/prepare-report.json`:

```json
{
  "resolvedCount": 42,
  "unresolvedCount": 3,
  "resolutionBreakdown": {
    "override": 5,
    "upn": 30,
    "displayName": 7,
    "default": 3
  }
}
```

---

## NodesModuleOptions — no change

`NodesModuleOptions` retains only `Enabled` and `ReplicateSourceTree`. `AutoCreateNodes` remains exclusively on `NodeTranslationOptions`. No structural change — the existing class is correct.

---

## Key Relationships

```
IdentitiesModule
  → IIdentitiesOrchestrator.PrepareAsync(projectName, context, ct)
      ← IIdentityAdapter.FindByUpnAsync / FindByDisplayNameAsync
      ← IIdentityMatchingStrategy[].Match(...)
  → IIdentitiesOrchestrator.ExportAsync(...)     [unchanged]
  → IIdentitiesOrchestrator.ImportAsync(...)     [parameter removed]
  → IIdentitiesOrchestrator.ValidateAsync(...)   [unchanged]

IIdentityTranslationTool.Translate(sourceIdentity)
  → IIdentitiesOrchestrator [reads in-memory cache — no I/O]

Consumers of IIdentityTranslationTool:
  - IdentitiesModule (constructor injection)
  - TeamImportOrchestrator (replaces _identityLookupTool field)
  - RevisionFolderProcessor (replaces _identityLookupTool field)
  - WorkItemsModule (replaces _identityLookupTool field)
```
