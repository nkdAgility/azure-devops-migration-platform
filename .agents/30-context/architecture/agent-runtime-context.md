# agent_runtime_context — Runtime Configuration and Context Materialization System

- Tag: `agent_runtime_context`
- Responsibility: Materialize `Job.ConfigPayload` into package config and expose current job/package/source/target context accessors.

## Core Classes

- `PackageMigrationConfigLoader`
- `ICurrentPackageConfigAccessor`
- `ICurrentAgentJobContextAccessor`
- `ICurrentJobEndpointAccessor`
- `AgentJobContext`
- `PackageConfigNotFoundException`

## Validating Tests

- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/PackageMigrationConfigLoaderTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobAgentWorkerDispatchTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs`

## Sequence Diagram

```mermaid
sequenceDiagram
  participant JW as JobAgentWorker
  participant PCM as PackageMigrationConfigLoader
  participant PA as IPackageAccess
  participant PCA as ICurrentPackageConfigAccessor
  participant JCA as ICurrentAgentJobContextAccessor
  participant ECA as ICurrentJobEndpointAccessor

  JW->>PA: PersistMetaAsync(MigrationConfig)
  JW->>PCM: LoadAsync(...)
  PCM->>PA: RequestMetaAsync(MigrationConfig)
  PCM-->>JW: IConfiguration
  JW->>PCA: Set(config)
  JW->>JCA: Set(AgentJobContext)
  JW->>ECA: SetSource/SetTarget
  JW->>PCA: Clear() at job end
  JW->>JCA: Clear() at job end
  JW->>ECA: Clear() at job end
```




