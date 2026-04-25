# Current Project Reference Topology

> Updated after Phase 6 separation-of-concerns refactoring (all source violations fixed; test projects reorganized; ControlPlaneHost cleaned).

## Production Projects

### DevOpsMigrationPlatform.Abstractions
- _(no project references — leaf dependency)_

### DevOpsMigrationPlatform.Abstractions.Agent
- DevOpsMigrationPlatform.Abstractions

### DevOpsMigrationPlatform.Abstractions.ControlPlane
- DevOpsMigrationPlatform.Abstractions

### DevOpsMigrationPlatform.Infrastructure
- DevOpsMigrationPlatform.Abstractions

### DevOpsMigrationPlatform.Infrastructure.AzureDevOps
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Abstractions.Agent
- DevOpsMigrationPlatform.Infrastructure
- DevOpsMigrationPlatform.Infrastructure.Agent

### DevOpsMigrationPlatform.Infrastructure.Agent
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Abstractions.Agent
- DevOpsMigrationPlatform.Infrastructure

### DevOpsMigrationPlatform.Infrastructure.ControlPlane
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Abstractions.ControlPlane
- DevOpsMigrationPlatform.Infrastructure

### DevOpsMigrationPlatform.Infrastructure.Simulated
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Abstractions.Agent
- DevOpsMigrationPlatform.Infrastructure
- DevOpsMigrationPlatform.Infrastructure.Agent

### DevOpsMigrationPlatform.Infrastructure.TfsObjectModel
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Infrastructure

### DevOpsMigrationPlatform.ControlPlane
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Abstractions.ControlPlane

### DevOpsMigrationPlatform.ServiceDefaults
- DevOpsMigrationPlatform.Abstractions

### DevOpsMigrationPlatform.MigrationAgent
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Abstractions.Agent
- DevOpsMigrationPlatform.Infrastructure
- DevOpsMigrationPlatform.Infrastructure.Agent
- DevOpsMigrationPlatform.Infrastructure.AzureDevOps
- DevOpsMigrationPlatform.Infrastructure.Simulated
- DevOpsMigrationPlatform.ServiceDefaults

### DevOpsMigrationPlatform.ControlPlaneHost
- DevOpsMigrationPlatform.Abstractions.ControlPlane
- DevOpsMigrationPlatform.ControlPlane
- DevOpsMigrationPlatform.Infrastructure.ControlPlane
- DevOpsMigrationPlatform.ServiceDefaults

### DevOpsMigrationPlatform.CLI.Migration
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Abstractions.Agent
- DevOpsMigrationPlatform.Infrastructure

### DevOpsMigrationPlatform.CLI.TfsMigration
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Infrastructure
- DevOpsMigrationPlatform.Infrastructure.TfsObjectModel

### DevOpsMigrationPlatform.AppHost
- DevOpsMigrationPlatform.ControlPlaneHost
- DevOpsMigrationPlatform.MigrationAgent

## Test Projects

### DevOpsMigrationPlatform.Infrastructure.Agent.Tests _(new in Phase 6)_
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Abstractions.Agent
- DevOpsMigrationPlatform.Infrastructure
- DevOpsMigrationPlatform.Infrastructure.Agent
- DevOpsMigrationPlatform.Infrastructure.AzureDevOps
- DevOpsMigrationPlatform.Infrastructure.Simulated

### DevOpsMigrationPlatform.Infrastructure.Tests _(cleaned in Phase 6)_
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Infrastructure

### DevOpsMigrationPlatform.CLI.Migration.Tests _(cleaned in Phase 6)_
- DevOpsMigrationPlatform.CLI.Migration

### DevOpsMigrationPlatform.ControlPlane.Tests
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.ControlPlane

### DevOpsMigrationPlatform.Infrastructure.Simulated.Tests
- DevOpsMigrationPlatform.Abstractions
- DevOpsMigrationPlatform.Infrastructure
- DevOpsMigrationPlatform.Infrastructure.Simulated

## Resolved Violations (Phase 6)

| Violation | Resolution |
|---|---|
| ControlPlane referenced Infrastructure + Infrastructure.ControlPlane | Moved `AddControlPlaneTelemetryServices` call to ControlPlaneHost; added Infrastructure.ControlPlane ref to Host |
| MigrationAgent referenced Infrastructure.ControlPlane | Moved InMemoryJobMetricsStore/InMemoryJobSnapshotStore to Infrastructure.Telemetry; added `AddAgentJobMetricsServices()` to Infrastructure.Agent |
| CLI.Migration referenced Infrastructure.Agent | Moved `PackagePathUtilities` to Abstractions; removed Infrastructure.Agent ref (kept Abstractions.Agent for IProgressSink, InventorySummary, etc.) |
| ControlPlaneHost referenced Infrastructure.AzureDevOps + Infrastructure.Simulated | Removed unused references; Infrastructure types accessed transitively via Infrastructure.ControlPlane |
| Infrastructure.Tests over-referenced (Agent + ControlPlane) | Created Infrastructure.Agent.Tests; moved all Agent/AzureDevOps-dependent tests there; Infrastructure.Tests now only references Abstractions + Infrastructure |
| CLI.Migration.Tests over-referenced (Infrastructure + Infrastructure.AzureDevOps) | Moved Transitive*.cs tests to Infrastructure.Agent.Tests; CLI.Migration.Tests now only references CLI.Migration |
