# ADR 0012 — IModule Five-Phase Contract

## Status

Accepted — amended by ADR-0014

## Context

The original `IModule` interface exposed two methods: `ExportAsync` and `ImportAsync`. Inventory and dependency analysis were handled by standalone module classes (`InventoryModule`, `InventoryDiscoveryModule`, `DependencyDiscoveryModule`) that abused `ExportAsync` to run non-export logic. This created three problems:

1. **Semantic mismatch.** Running `queue inventory` triggered `ExportAsync` on `InventoryModule`. Operators and developers had to know this mapping; the code did not communicate it.
2. **Duplicate module registrations.** An operator who wanted both export and inventory had to configure two modules with similar names, increasing config complexity and confusion.
3. **Phase gate rules were not enforced.** The `Inventory` and `Prepare` phases were documented in `docs/architecture.md` but `IModule` had no corresponding methods, so the phase gate rules could not be automatically applied by the plan executor.

A separate concern arose for cross-cutting analysis operations (dependency mapping, process diff). These are not migrations — their output is a planning artefact, never imported. Modelling them as `ExportAsync` methods on `IModule` misrepresented their purpose.

## Decision

`IModule` exposes all five migration phases:

```csharp
Task InventoryAsync(IJobContext context, CancellationToken cancellationToken);
Task ExportAsync(IJobContext context, CancellationToken cancellationToken);
Task PrepareAsync(IJobContext context, CancellationToken cancellationToken);
Task ImportAsync(IJobContext context, CancellationToken cancellationToken);
Task ValidateAsync(IJobContext context, CancellationToken cancellationToken);
```

The plan executor calls the appropriate phase method based on `Job.Kind`. An Export job calls `ExportAsync`; an Import job calls `ImportAsync`; a Migrate job calls all five in order (subject to phase gate rules); a Prepare job calls `PrepareAsync` only.

The standalone `InventoryModule`, `InventoryDiscoveryModule`, and `DependencyDiscoveryModule` classes are eliminated. Each domain module (`WorkItemsModule`, `TeamsModule`, etc.) contributes its own inventory counts via `InventoryAsync`. No separate module class is required.

**`IAnalyser` is introduced** for cross-cutting analysis operations that produce planning artefacts but are never imported. `DependencyAnalyser` replaces `DependencyDiscoveryModule`. Analysers participate in `JobKind.Dependencies` jobs and may be declared as dependencies of module `PrepareAsync` steps.

## Alternatives Considered

**Keep `ExportAsync`/`ImportAsync`, add a separate `IInventoryModule` interface**: Fixes the semantic mismatch for inventory but does not add Prepare or Validate phases to `IModule`. The plan executor still cannot generically call any phase on any module.

**Add optional methods with default no-op implementations**: Allows incremental adoption, but a module that does not override `PrepareAsync` silently skips prepare with no log or warning. Operators cannot distinguish "prepare not configured" from "prepare ran and found nothing".

**Use a plugin/extension model with separate phase registrations**: Maximum flexibility, but adds significant complexity for the common case (one module class per domain, all phases).

## Consequences

- Every domain module implements all five phase methods. Modules that have no meaningful behaviour for a phase return `Task.CompletedTask` and emit a `Debug` log.
- The plan executor can call any phase method on any `IModule` without switching on module type.
- `InventoryAsync` is called automatically before `ExportAsync` in a `Migrate` job — no separate inventory module configuration is required.
- `PrepareAsync` is called between Export and Import in a `Migrate` job — operators get target-side validation by default.
- `IAnalyser` implementations are registered separately and appear in the task list when a `JobKind.Dependencies` job runs.
- `docs/module-development-guide.md` must document all five phase methods and the `IAnalyser` interface.

## Related

- [docs/architecture.md](../architecture.md) — phase gate rules
- [docs/module-development-guide.md](../module-development-guide.md) — module contract
- [ADR-0010](0010-plan-driven-dag-execution.md) — plan executor that calls phase methods
- [ADR-0014](0014-icapture-unified-capture-contract.md) — ICapture extraction that amends this ADR
- [.agents/20-guardrails/domains/module-rules.md](../../.agents/20-guardrails/domains/module-rules.md) — module implementation checklist
- Driving spec: `specs/030-module-analiser-refactor/spec.md`

