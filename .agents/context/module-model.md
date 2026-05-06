# Module Model

Compressed module model for agents. See `docs/module-development-guide.md` for the full guide.

## Purpose

A module is a self-contained unit of migration logic for a single data type (e.g. WorkItems, Teams, Nodes, Identities). Modules are the only extension point for adding new capabilities.

## Boundaries

- Modules must not call other modules.
- Modules must not call connectors from other modules.
- Modules must not access the filesystem directly — all package access goes through `IArtefactStore` and `IStateStore`.

## Execution Shape

Every module implements `IModule`, which exposes:

- `InventoryAsync(context, cancellationToken)` — counts and catalogues
- `ExportAsync(context, cancellationToken)` — writes to package
- `PrepareAsync(context, cancellationToken)` — validates target
- `ImportAsync(context, cancellationToken)` — reads from package and pushes to target
- `ValidateAsync(context, cancellationToken)` — compares source and target

## Telemetry Contract

Every module operation must satisfy all four telemetry obligations (O-1 through O-4). See [.agents/guardrails/observability-requirements.md](../guardrails/observability-requirements.md).

## Test Expectations

- Every export module must have a `SystemTest_Simulated` that asserts the artefact exists and has non-empty content.
- Every import module must have a `SystemTest_Simulated` that asserts the target connector received data.
- Zero-item Simulated sources are forbidden.

## Configuration

- Every module has an `Enabled` property in configuration.
- Module configuration is under `MigrationPlatform.Modules.{ModuleName}`.
- Configuration must be bound via `IOptions<T>`.

## Isolation

- A module failure must not crash other modules.
- Module code does not hold references to other module instances.
- Module service registration uses DI extension methods in the module assembly.