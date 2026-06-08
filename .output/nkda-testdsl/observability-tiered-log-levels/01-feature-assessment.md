# Feature Assessment: observability-tiered-log-levels

## Feature File
`features/platform/observability/tiered-log-levels.feature`

## Scenarios
1. Agent writes at its configured level regardless of control plane level
2. Standalone mode aligns control plane minimum with operator level

## Wiring State
Unwired — no Reqnroll step bindings exist for this feature.

## Key Types Under Test
- `PackageLoggerProvider` (src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/PackageLoggerProvider.cs)
- `ControlPlaneLoggerProvider` (src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneLoggerProvider.cs)
- `DiagnosticLogStore` (src/DevOpsMigrationPlatform.ControlPlane/Jobs/DiagnosticLogStore.cs)
- `DiagnosticLogOptions` (src/DevOpsMigrationPlatform.Abstractions/Diagnostics/DiagnosticLogOptions.cs)
- `DiagnosticLogStoreOptions` (src/DevOpsMigrationPlatform.ControlPlane/Jobs/DiagnosticLogStoreOptions.cs)

## Migration Risk
Low — both classes have existing unit test infrastructure and in-memory mocks available.
