# Package Persistence Contract

Canonical contract for artefact/state persistence under the package boundary.

## Contract Surface

- `IArtefactStore`
- `IStateStore`
- `IPackageStoreFactory`
- `FileSystemArtefactStore`
- `AzureBlobArtefactStore`
- `FileSystemStateStore`
- `FileSystemPackageStoreFactory`
- `PackageMigrationConfigLoader`

## Required Semantics

1. Persist artefacts/state only via abstractions.
2. Runtime persistence covers config (`migration-config.json`), plan (`plan.json`), cursors, and package logs.
3. Connector/store implementation swaps must not require module or orchestrator code changes.

## Sequence Diagram

```mermaid
sequenceDiagram
  participant JW as JobAgentWorker
  participant PF as IPackageStoreFactory
  participant PA as IPackageAccess
  participant PCM as PackageMigrationConfigLoader

  JW->>PCM: LoadAsync(...)
  PCM->>PA: RequestMetaAsync(MigrationConfig)
  PCM-->>JW: IConfiguration
  JW->>PA: PersistMetaAsync / PersistContentAsync
  JW->>PA: AppendLogAsync / PersistContentAsync
```

