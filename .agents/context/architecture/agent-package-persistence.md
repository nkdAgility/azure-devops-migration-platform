# agent_package_persistence — Package Persistence System

- Tag: `agent_package_persistence`
- Responsibility: Persist artefacts and state via abstractions only, including `migration-config.json`, `plan.json`, cursors, and package logs.

## Core Classes

- `IArtefactStore`
- `IStateStore`
- `IPackageStoreFactory`
- `FileSystemArtefactStore`
- `AzureBlobArtefactStore`
- `FileSystemStateStore`
- `FileSystemPackageStoreFactory`
- `PackageConfigStore`

## Validating Tests

- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/FileSystemArtefactStoreTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/FileSystemStateStoreTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/PackageConfigStoreTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs`

## Notes

- `AzureBlobArtefactStore` currently has no dedicated unit test file; coverage is indirect through higher-level tests.

## Sequence Diagram

```mermaid
sequenceDiagram
  participant JW as JobAgentWorker
  participant PF as IPackageStoreFactory
  participant AS as IArtefactStore
  participant SS as IStateStore
  participant PCS as PackageConfigStore

  JW->>PF: Create(packageUri)
  PF-->>JW: (IArtefactStore, IStateStore)
  JW->>PCS: ReadAsync(IArtefactStore)
  PCS->>AS: ExistsAsync(migration-config.json)
  PCS->>AS: ReadAsync(migration-config.json)
  PCS-->>JW: IConfiguration
  JW->>SS: Write plan/cursor/phase state
  JW->>AS: Write logs and artefacts
```
