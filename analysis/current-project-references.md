# Current Project Reference Topology

> Captured before Phase 1 separation-of-concerns refactoring.

## Production Projects

### DevOpsMigrationPlatform.Abstractions
- _(no project references — leaf dependency)_

### DevOpsMigrationPlatform.Infrastructure
- DevOpsMigrationPlatform.Abstractions

### DevOpsMigrationPlatform.Infrastructure.AzureDevOps
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Infrastructure

### DevOpsMigrationPlatform.Infrastructure.Simulated
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Infrastructure

### DevOpsMigrationPlatform.Infrastructure.TfsObjectModel
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Infrastructure

### DevOpsMigrationPlatform.ControlPlane
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Infrastructure

### DevOpsMigrationPlatform.ServiceDefaults
- DevOpsMigrationPlatform.Abstractions

### DevOpsMigrationPlatform.MigrationAgent
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Infrastructure
- DevOpsMigrationPlatform.Infrastructure.AzureDevOps
- DevOpsMigrationPlatform.Infrastructure.Simulated
- DevOpsMigrationPlatform.ServiceDefaults

### DevOpsMigrationPlatform.ControlPlaneHost
- DevOpsMigrationPlatform.ControlPlane
- DevOpsMigrationPlatform.Infrastructure.AzureDevOps
- DevOpsMigrationPlatform.Infrastructure.Simulated
- DevOpsMigrationPlatform.ServiceDefaults

### DevOpsMigrationPlatform.CLI.Migration ⚠️ (over-referenced — target of refactoring)
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.ControlPlane           ← **violation**: CLI must not reference ControlPlane directly
- DevOpsMigrationPlatform.Infrastructure
- DevOpsMigrationPlatform.Infrastructure.AzureDevOps  ← **violation**: connector detail belongs in agent
- DevOpsMigrationPlatform.Infrastructure.Simulated    ← **violation**: connector detail belongs in agent
- DevOpsMigrationPlatform.MigrationAgent         ← **violation**: CLI must not reference MigrationAgent directly

### DevOpsMigrationPlatform.CLI.TfsMigration
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Infrastructure
- DevOpsMigrationPlatform.Infrastructure.TfsObjectModel

### DevOpsMigrationPlatform.AppHost
- DevOpsMigrationPlatform.ControlPlaneHost
- DevOpsMigrationPlatform.MigrationAgent

## Test Projects

### DevOpsMigrationPlatform.CLI.Migration.Tests
- DevOpsMigrationPlatform.CLI.Migration
- DevOpsMigrationPlatform.Infrastructure
- DevOpsMigrationPlatform.Infrastructure.AzureDevOps

### DevOpsMigrationPlatform.ControlPlane.Tests
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.ControlPlane

### DevOpsMigrationPlatform.Infrastructure.Simulated.Tests
- DevOpsMigrationPlatform.Infrastructure.Simulated
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Infrastructure

### DevOpsMigrationPlatform.Infrastructure.Tests ⚠️ (over-referenced)
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Infrastructure
- DevOpsMigrationPlatform.Infrastructure.AzureDevOps
- DevOpsMigrationPlatform.Infrastructure.Simulated
- DevOpsMigrationPlatform.CLI.Migration            ← pulling CLI into Infrastructure tests

## Known Violations (Goals of Phase 1 Refactoring)

| Violation | File | Target State |
|---|---|---|
| CLI references ControlPlane | CLI.Migration.csproj | Remove — CLI talks via HTTP only |
| CLI references MigrationAgent | CLI.Migration.csproj | Remove — agent runs in subprocess |
| CLI references Infrastructure.AzureDevOps | CLI.Migration.csproj | Remove — option types move to Abstractions |
| CLI references Infrastructure.Simulated | CLI.Migration.csproj | Remove — option types move to Abstractions |
| Option types in connector projects | AzureDevOps, Simulated | Move to Abstractions/Options/ |
| WiqlValidator in AzureDevOps | Infrastructure.AzureDevOps | Move to Abstractions/Validation/ |
| LogDownloadController in ControlPlane | ControlPlane project | Remove — logs live in package on disk |
| MigrateLogsSteps in Infrastructure.Tests | Infrastructure.Tests | Move to CLI.Migration.Tests |
