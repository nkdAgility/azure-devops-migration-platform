# ADR 0014 — ICapture: Unified Capture Contract

## Status

Accepted — amends ADR-0012

## Context

ADR-0012 introduced the five-phase `IModule` contract (`InventoryAsync`, `ExportAsync`, `PrepareAsync`, `ImportAsync`, `ValidateAsync`). The `InventoryAsync` method was placed directly on `IModule`, which created two problems when the `DependencyCapture` use-case arose:

1. **No extension point for non-module capture handlers.** `DependencyCapture` must perform a per-project discovery pass and write artefacts, but it has no export, import, prepare, or validate behaviour. Forcing it to implement the full `IModule` interface (or a new standalone interface dispatched by a separate executor branch) required either dead method stubs or a conditional dispatch path in `JobPlanExecutor` — both architectural hacks.

2. **`InventoryAsync` semantics were wrong.** "Inventory" describes the *output* (a count/catalogue of source items). "Capture" describes the *action* (perform a per-project discovery pass and write artefacts). The method name `InventoryAsync` was confusing to developers implementing new modules.

A third concern: `IProjectAnalyser` was introduced as a temporary workaround for the same per-project dispatch problem. It added a second executor branch and an additional DI registration type with no clear ownership boundary.

## Decision

`ICapture` is extracted as a standalone interface in `DevOpsMigrationPlatform.Abstractions.Agent`:

```csharp
public interface ICapture
{
    string Name { get; }
    Task CaptureAsync(InventoryContext context, CancellationToken ct);
}
```

`IModule` extends `ICapture` instead of declaring `InventoryAsync` directly:

```csharp
public interface IModule : ICapture
{
    // ... ExportAsync, PrepareAsync, ImportAsync, ValidateAsync, SupportsX flags
}
```

The plan executor assembles a single `captureHandlersByName` dictionary from all `ICapture` registrations (both `IModule` implementations where `SupportsInventory = true`, and pure `ICapture` implementations such as `DependencyCapture`). All `capture.*` tasks are dispatched through this dictionary with no branching on module vs. pure-capture type.

`IProjectAnalyser` is deleted. `DependencyCapture : ICapture` replaces `DependencyDiscoveryModule` and eliminates the need for a per-project analyser interface entirely.

The context type `InventoryContext` is **not** renamed — it describes the data shape (inventory data scoped per org+project), which remains accurate regardless of the method name.

## Alternatives Considered

**Keep `InventoryAsync` on `IModule`, add a separate `IProjectCapture` interface for non-module handlers**: Fixes the extension point problem but keeps the semantic mismatch and introduces a third dispatch type alongside `IModule` and `IAnalyser`.

**Add `DependencyCapture : IModule` with stub implementations**: Avoids the new interface but forces a class with no export/import/prepare/validate meaning to implement four no-op methods, violating ISP (Interface Segregation Principle).

**Keep `IProjectAnalyser` as the per-project dispatch hook**: Retains the architectural debt. Two dispatch dictionaries, two executor branches, two DI registration types — complexity with no benefit over the unified `ICapture` approach.

## Consequences

- All modules implement `ICapture` transitively via `IModule`. The rename `InventoryAsync` → `CaptureAsync` is a source-breaking change on the `IModule` interface; all existing module implementations must rename their override.
- Pure capture handlers (e.g. `DependencyCapture`) are registered in DI as `ICapture` only and appear in the `captureHandlersByName` dictionary automatically. No new executor branches are required.
- `IProjectAnalyser` is removed from the codebase. No code may reference it.
- Modules that return `SupportsInventory = false` are excluded from the capture handler registry — behaviour unchanged from ADR-0012.
- `InventoryContext` retains its name and shape — no downstream breakage for code that constructs or consumes inventory context objects.
- `docs/module-development-guide.md` must document `ICapture`, the rename from `InventoryAsync` to `CaptureAsync`, and the `SupportsInventory` flag that gates module registration.

## Related

- [ADR-0012](0012-imodule-five-phase-contract.md) — IModule five-phase contract (amended by this ADR)
- [ADR-0010](0010-plan-driven-dag-execution.md) — plan executor that dispatches capture tasks
- [docs/module-development-guide.md](../module-development-guide.md) — module contract
- [docs/architecture.md](../architecture.md) — phase gate rules
- Driving spec: `specs/032-icapture-interface/spec.md`
