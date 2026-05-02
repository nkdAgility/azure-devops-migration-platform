# Contract: `IAgentJobContext`

**Location**: `DevOpsMigrationPlatform.Abstractions.Agent/Context/IAgentJobContext.cs`

## Purpose

`IAgentJobContext` provides modules with read-only access to the scalar values belonging to the current agent job. It replaces the cross-cutting scalar usage of `ActiveJobConfigState.Current` (e.g. `activeJobConfig.Current?.Mode`, `activeJobConfig.Current?.Package?.Path`). Modules MUST NOT inject `MigrationOptions`, `ActiveJobConfigState`, or any connector-specific options type to obtain these values.

## Interface Contract

```csharp
namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// Read-only view of the current agent job's execution context.
/// Scoped to a single agent job — constructed once when the job starts, never mutated.
/// </summary>
public interface IAgentJobContext
{
    /// <summary>
    /// The execution mode for this job: "Export", "Import", "Prepare", or "Migrate".
    /// </summary>
    string Mode { get; }

    /// <summary>
    /// Resolved, expanded absolute path to the migration package directory on disk.
    /// Never contains '~' or environment variable expansions.
    /// </summary>
    string PackagePath { get; }

    /// <summary>
    /// The declared config schema version from migration-config.json (e.g. "2.0").
    /// </summary>
    string ConfigVersion { get; }
}
```

## Implementation: `AgentJobContext`

```csharp
namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

public sealed class AgentJobContext : IAgentJobContext
{
    public required string Mode { get; init; }
    public required string PackagePath { get; init; }
    public required string ConfigVersion { get; init; }
}
```

## Registration

Registered once per job in `MigrationAgentServiceExtensions.AddMigrationAgentServices()`, using the parsed `MigrationOptions` values available at job-start time:

```csharp
services.AddSingleton<IAgentJobContext>(new AgentJobContext
{
    Mode        = migrationOptions.Mode ?? "Export",
    PackagePath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(
                      migrationOptions.Package?.Path ?? string.Empty)),
    ConfigVersion = migrationOptions.ConfigVersion ?? "2.0"
});
```

## Invariants

- `Mode` MUST be one of `"Export"`, `"Import"`, `"Prepare"`, `"Migrate"`.
- `PackagePath` MUST be an absolute path (validated at registration time; throw `InvalidOperationException` if not).
- All properties are `{ get; init; }` — immutable after construction.
- `IAgentJobContext` is in `Abstractions.Agent` — it is visible to modules. `AgentJobContext` is in `Infrastructure.Agent` — concrete implementation, not visible to modules.
