# Contracts — NodeStructure Tool

**Feature**: 023-workitems-nodestructure-tool  
**Date**: 2026-04-26

---

## Public Interfaces

### INodeStructureTool

**Project**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Tools`

Pure path-mapping tool. No I/O, no state. Called per-revision during import.

```csharp
/// <summary>
/// Translates area and iteration path values from source to target format.
/// Stateless — safe to call concurrently.
/// </summary>
public interface INodeStructureTool
{
    /// <summary>
    /// Translates a single path value using language override, explicit mapping,
    /// and auto project-name swap.
    /// </summary>
    /// <param name="fieldName">"System.AreaPath" or "System.IterationPath".</param>
    /// <param name="sourcePathValue">The raw path from revision.json.</param>
    /// <param name="context">Source/target project names.</param>
    /// <returns>Translation result with metadata.</returns>
    PathTranslation TranslatePath(
        string fieldName,
        string sourcePathValue,
        ProjectMapping context);

    /// <summary>Whether the tool is enabled (Enabled config flag).</summary>
    bool IsEnabled { get; }
}
```

### INodeCreator

**Project**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Tools`

Target-side node management. Called during import pre-processing and bulk replication.

```csharp
/// <summary>
/// Creates and queries classification nodes in the target ADO project.
/// All methods are idempotent and retryable.
/// </summary>
public interface INodeCreator
{
    Task<bool> NodeExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        CancellationToken ct);

    Task EnsureExistsAsync(
        ClassificationNodeType nodeType,
        string path,
        CancellationToken ct);

    Task SetIterationDatesAsync(
        string path,
        DateTimeOffset? startDate,
        DateTimeOffset? finishDate,
        CancellationToken ct);
}
```

### IClassificationTreeReader

**Project**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Tools`

Source-side node enumeration. Called during export to capture the source classification tree.

```csharp
/// <summary>
/// Enumerates the full classification tree from the source project.
/// Export-only — never called at import time.
/// </summary>
public interface IClassificationTreeReader
{
    IAsyncEnumerable<string> EnumerateAreaNodesAsync(CancellationToken ct);
    IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(CancellationToken ct);
}
```

### INodeStructureValidator

**Project**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Tools`

Pre-import validation. Scans package for unmapped paths.

```csharp
/// <summary>
/// Validates NodeStructure configuration against the package contents.
/// No side effects — read-only scan.
/// </summary>
public interface INodeStructureValidator
{
    Task<NodeStructureValidationReport> ValidateAsync(
        IArtefactStore artefactStore,
        ProjectMapping context,
        CancellationToken ct);
}
```

---

## Configuration Contract

### JSON Schema

```json
{
  "MigrationPlatform": {
    "Tools": {
      "NodeStructure": {
        "Enabled": true,
        "AreaPathMappings": {
          "<source-area-path>": "<target-area-path>"
        },
        "IterationPathMappings": {
          "<source-iteration-path>": "<target-iteration-path>"
        },
        "AreaLanguageOverride": null,
        "IterationLanguageOverride": null,
        "AutoCreateNodes": false,
        "SkipOnUnresolvableArea": false,
        "SkipOnUnresolvableIteration": false,
        "ReplicateSourceTree": false
      }
    }
  }
}
```

### Path Format

- Separator: `\` (backslash)
- Format: `"ProjectName\\NodeLevel1\\NodeLevel2"`
- Matching: case-insensitive, trimmed

---

## Package Artifact Contracts

### Nodes/source-tree.json

Always written by export. Read by import when `ReplicateSourceTree: true`.

```json
{
  "areaNodes": [
    "ProjectName\\Area1",
    "ProjectName\\Area1\\SubArea"
  ],
  "iterationNodes": [
    {
      "path": "ProjectName\\Sprint 1",
      "startDate": "2024-01-15",
      "finishDate": "2024-01-28",
      "isBacklogIteration": false
    },
    {
      "path": "ProjectName\\Sprint 2",
      "startDate": null,
      "finishDate": null,
      "isBacklogIteration": false
    }
  ]
}
```

### Nodes/referenced-paths.json

Always written during work item export. Read by import for pre-collection pass.

```json
{
  "areaPaths": [
    "ProjectName\\Team A",
    "ProjectName\\Team B"
  ],
  "iterationPaths": [
    "ProjectName\\Sprint 1",
    "ProjectName\\Sprint 2"
  ]
}
```

---

## DI Registration Contract

```csharp
public static class NodeStructureToolServiceCollectionExtensions
{
    public static IServiceCollection AddNodeStructureToolServices(
        this IServiceCollection services)
    {
        services.AddOptions<NodeStructureOptions>()
            .BindConfiguration(NodeStructureOptions.SectionName);

        services.AddSingleton<INodeStructureTool, NodeStructureTool>();
        services.AddSingleton<INodeStructureValidator, NodeStructureValidator>();
        // INodeCreator and IClassificationTreeReader
        // registered by connector-specific DI (ADO REST implementation)
        return services;
    }
}
```
