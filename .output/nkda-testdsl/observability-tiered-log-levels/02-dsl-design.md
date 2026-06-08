# DSL Design: observability-tiered-log-levels

## Target Test Classes

### Scenario 1 → PackageDiagnosticsSinkTests
File: `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Telemetry/PackageDiagnosticsSinkTests.cs`
Method: `PackageLoggerProvider_AgentAtDebug_WritesDebugAndAboveRegardlessOfControlPlaneLevel`

Creates `PackageLoggerProvider` with `MinimumLevel="Debug"`, emits Debug/Information/Warning/Error,
flushes, asserts all four levels appear in the package NDJSON payload.

### Scenario 2 → DiagnosticLogStoreTests
File: `tests/DevOpsMigrationPlatform.ControlPlane.Tests/Diagnostics/DiagnosticLogStoreTests.cs`
Method: `StandaloneMode_OperatorLevelInformation_ControlPlaneAcceptsInformationAndAbove`

Creates `DiagnosticLogStore` with `MinimumLevel="Information"`, adds Debug/Information/Warning/Error,
asserts snapshot contains exactly 3 records (Information and above).
