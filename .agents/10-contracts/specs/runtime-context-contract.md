# Runtime Context Contract

Canonical contract for materializing `Job.ConfigPayload` and exposing active runtime context.

## Contract Surface

- `PackageMigrationConfigLoader`
- `ICurrentPackageConfigAccessor`
- `ICurrentAgentJobContextAccessor`
- `ICurrentJobEndpointAccessor`
- `AgentJobContext`
- `PackageConfigNotFoundException`

## Required Semantics

1. Job config payload is materialized to package config before module execution.
2. Current package config, job context, and endpoint accessors are set before execution and cleared at job end.
3. Missing package config is a fail-fast condition.

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

