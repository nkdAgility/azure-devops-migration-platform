# Module Model

Compressed module model for agents. See `docs/module-development-guide.md` for the full guide.

## Purpose

A module is a self-contained unit of migration logic for a single data type (e.g. WorkItems, Teams, Nodes, Identities). Modules are the only extension point for adding new capabilities.

## Boundaries

- Modules must not call other modules.
- Modules must not call connectors from other modules.
- Modules must not access the filesystem directly — package-facing access goes through `IPackageAccess` (with `IArtefactStore`/`IStateStore` beneath the boundary).

## Execution Shape

Every module implements `IModule : ICapture`, which exposes:

- `CaptureAsync(context, cancellationToken)` — counts and catalogues (inherited from `ICapture`)
- `ExportAsync(context, cancellationToken)` — writes to package
- `PrepareAsync(context, cancellationToken)` — validates target
- `ImportAsync(context, cancellationToken)` — reads from package and pushes to target
- `ValidateAsync(context, cancellationToken)` — compares source and target

## Capture Dispatch

Capture tasks (`capture.*`) are dispatched via a unified `captureHandlersByName` dictionary of type `IReadOnlyDictionary<string, ICapture>`. This dictionary is assembled by `JobAgentWorker.BuildCaptureHandlers`:

1. Step 1: all `IModule` instances where `SupportsInventory = true` are added (cast to `ICapture`).
2. Step 2: pure `ICapture` registrations (not `IModule`) are unioned in, de-duplicating by name with `OrdinalIgnoreCase`.

The `ICapture` interface is the unified capture contract:
```csharp
public interface ICapture
{
    string Name { get; }
    Task CaptureAsync(InventoryContext context, CancellationToken ct);
}
```

`Name` returns the second dot-segment of task IDs (e.g., `"workitems"` for `capture.workitems.org.project`).

## Related Extension Points

Modules are one of three extension points in the job engine:

| Extension point | Interface | When it runs |
|---|---|---|
| Module | `IModule` | Inventory, Export, Prepare, Import, Validate phases |
| Pure Capture Handler | `ICapture` (not `IModule`) | Inventory capture only; no export/import lifecycle |
| Analyser | `IAnalyser` | After all inventory modules complete; writes analysis artefacts to the package |

**Tools** (`MigrationPlatform:Tools.*`) are a fourth extension point but are stateless services, not phase participants. They are injected into modules and orchestrators to perform pure transformations (field rewriting, path translation, identity lookup) with no I/O.

Current pure capture handlers:
- `DependencyCapture` — captures per-project dependency links via `IDependencyDiscoveryServiceFactory.CreateForProject` and `IDependencyOrchestrator.CaptureProjectAsync`. Registered as `ICapture` only via `AddDependencyCapture()`. TFS agents must NOT register this.

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
