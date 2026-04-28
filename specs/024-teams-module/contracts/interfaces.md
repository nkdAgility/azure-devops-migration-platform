# Interface Contracts — IdentitiesModule, NodeStructureModule & TeamsModule

**Phase 1 Output** — defines the interface signatures for all new abstractions introduced by this spec.

> These contracts are design-time documentation. The authoritative definitions live in source code under `src/DevOpsMigrationPlatform.Abstractions.Agent/`.

---

## IIdentitySource

**Location**: `Abstractions.Agent/Tools/IIdentitySource.cs`

```csharp
public interface IIdentitySource
{
    IAsyncEnumerable<IdentityDescriptor> EnumerateIdentitiesAsync(
        string projectName,
        CancellationToken cancellationToken);
}
```

**Connectors**: `SimulatedIdentitySource`, `AzureDevOpsIdentitySource`, TFS subprocess bridge.

---

## ITeamSource

**Location**: `Abstractions.Agent/Tools/ITeamSource.cs`

```csharp
public interface ITeamSource
{
    IAsyncEnumerable<TeamDefinition> EnumerateTeamsAsync(
        string projectName,
        CancellationToken cancellationToken);

    Task<TeamSettings> GetTeamSettingsAsync(
        string projectName, string teamId,
        CancellationToken cancellationToken);

    IAsyncEnumerable<TeamIteration> GetTeamIterationsAsync(
        string projectName, string teamId,
        CancellationToken cancellationToken);

    IAsyncEnumerable<TeamMember> GetTeamMembersAsync(
        string projectName, string teamId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<TeamCapacityEntry>> GetTeamCapacityAsync(
        string projectName, string teamId, string iterationId,
        CancellationToken cancellationToken);

    Task<TeamAreaPaths> GetTeamAreaPathsAsync(
        string projectName, string teamId,
        CancellationToken cancellationToken);
}
```

**Connectors**: `SimulatedTeamSource`, `AzureDevOpsTeamSource`, TFS subprocess bridge.

---

## ITeamTarget

**Location**: `Abstractions.Agent/Tools/ITeamTarget.cs`

```csharp
public interface ITeamTarget
{
    Task<TeamDefinition> CreateOrUpdateTeamAsync(
        string projectName, TeamDefinition team,
        CancellationToken cancellationToken);

    Task SetTeamSettingsAsync(
        string projectName, string teamId, TeamSettings settings,
        CancellationToken cancellationToken);

    Task AssignIterationAsync(
        string projectName, string teamId, string iterationPath,
        CancellationToken cancellationToken);

    Task AddMemberAsync(
        string projectName, string teamId, string identityDescriptor, bool isAdmin,
        CancellationToken cancellationToken);

    Task SetCapacityAsync(
        string projectName, string teamId, string iterationId,
        IReadOnlyList<TeamCapacityEntry> capacities,
        CancellationToken cancellationToken);

    Task SetAreaPathsAsync(
        string projectName, string teamId, TeamAreaPaths areaPaths,
        CancellationToken cancellationToken);
}
```

**Connectors**: `SimulatedTeamTarget`, `AzureDevOpsTeamTarget`, TFS subprocess bridge.

---

## INodeTranslationTool (rename from INodeStructureTool)

**Location**: `Abstractions.Agent/Tools/INodeTranslationTool.cs` (existing, renamed)

```csharp
public interface INodeTranslationTool
{
    string? TranslatePath(string sourcePath, ClassificationNodeType nodeType);
}
```

No signature changes — rename only.

---

## Options Classes

### IdentitiesModuleOptions

**Location**: `Abstractions.Agent/Modules/IdentitiesModuleOptions.cs`

```csharp
public sealed class IdentitiesModuleOptions
{
    public const string SectionName = "MigrationPlatform:Modules:Identities";
    public bool Enabled { get; init; }
    public string DefaultIdentity { get; init; } = string.Empty;
}
```

### NodeStructureModuleOptions

**Location**: `Abstractions.Agent/Modules/NodeStructureModuleOptions.cs`

```csharp
public sealed class NodeStructureModuleOptions
{
    public const string SectionName = "MigrationPlatform:Modules:Nodes";
    public bool Enabled { get; init; }
    public bool ReplicateSourceTree { get; init; }
    public bool AutoCreateNodes { get; init; }
}
```

### TeamsModuleOptions

**Location**: `Abstractions.Agent/Modules/TeamsModuleOptions.cs`

```csharp
public sealed class TeamsModuleOptions
{
    public const string SectionName = "MigrationPlatform:Modules:Teams";
    public bool Enabled { get; init; }
    public TeamsModuleExtensionsOptions Extensions { get; init; } = new();
}

public sealed class TeamsModuleExtensionsOptions
{
    public bool TeamSettings { get; init; } = true;
    public bool NodeStructure { get; init; } = true;
    public bool TeamIterations { get; init; } = true;
    public bool TeamMembers { get; init; } = true;
    public bool TeamCapacity { get; init; } = true;
}
```

---

## DI Registration Extensions

| Method | Registers |
|--------|-----------|
| `AddIdentitiesModule()` | `IdentitiesModule` as `IModule`, `IdentitiesModuleOptions`, `IIdentityMappingService` |
| `AddNodeStructureModule()` | `NodeStructureModule` as `IModule`, `NodeStructureModuleOptions` (wraps existing tool registration) |
| `AddTeamsModule()` | `TeamsModule` as `IModule`, `TeamsModuleOptions`, `TeamExportOrchestrator`, `TeamImportOrchestrator`, `TeamSlugGenerator` |
| `AddSimulatedIdentityServices()` | `SimulatedIdentitySource` as `IIdentitySource` |
| `AddAzureDevOpsIdentityServices()` | `AzureDevOpsIdentitySource` as `IIdentitySource` |
| `AddTfsIdentityServices()` | TFS identity subprocess bridge as `IIdentitySource` |
| `AddSimulatedTeamServices()` | `SimulatedTeamSource` as `ITeamSource`, `SimulatedTeamTarget` as `ITeamTarget` |
| `AddAzureDevOpsTeamServices()` | `AzureDevOpsTeamSource` as `ITeamSource`, `AzureDevOpsTeamTarget` as `ITeamTarget` |
| `AddTfsTeamServices()` | TFS team subprocess bridge as `ITeamSource` (import via TFS OM is export-only; target is ADO REST) |
