# Contract: IModuleExtension

**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**Layer**: Abstractions  
**Spec**: [spec.md](../spec.md) | [plan.md](../plan.md) | [data-model.md](../data-model.md)

---

## Purpose

Cross-cutting marker interface for all per-entity module extensions.
Every module-specific extension interface (e.g. `ITeamExtension`) extends this.

The interface allows platform tooling to discover, enumerate, and order
extensions uniformly across modules without coupling to any specific module type.

---

## Contract

```csharp
/// <summary>Cross-cutting marker for all per-entity module extensions.</summary>
public interface IModuleExtension
{
    /// <summary>
    /// Name of the module this extension belongs to (e.g. "Teams", "WorkItems").
    /// Used for filtering and diagnostics.
    /// </summary>
    string Module { get; }

    /// <summary>
    /// Name of this extension (e.g. "BoardConfig", "TeamSettings").
    /// Must be unique within the owning module.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Declared execution order within the module. Lower values execute first.
    /// Extensions with the same Order value execute in DI registration order.
    /// </summary>
    int Order { get; }

    /// <summary>
    /// True when this extension participates in the export pipeline.
    /// </summary>
    bool SupportsExport { get; }

    /// <summary>
    /// True when this extension participates in the import pipeline.
    /// </summary>
    bool SupportsImport { get; }
}
```

---

## Constraints

- `Module` must not be null or whitespace.
- `Name` must not be null or whitespace.
- `Name` must be unique within the owning module's extension set (enforced at runtime by the orchestrator's ordering step).
- `Order` has no enforced range; negative values are permitted for extensions that must run before framework defaults.
- At least one of `SupportsExport` or `SupportsImport` should be `true` — an extension that supports neither is a no-op and will be ignored by orchestrators.

---

## Implementors

| Interface | Module | Notes |
|-----------|--------|-------|
| `ITeamExtension` | Teams | Per-team export/import extension |

---

## Related

- [`ITeamExtension.md`](ITeamExtension.md) — Teams-specific extension contract
- [`IModule.cs`](../../../../src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs) — `IModuleExtension` mirrors the `SupportsExport`/`SupportsImport` pattern from `IModule`
