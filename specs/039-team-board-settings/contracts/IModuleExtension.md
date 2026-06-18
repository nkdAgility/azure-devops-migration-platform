# Contract: IModuleExtension

**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**Layer**: Abstractions  
**Spec**: [spec.md](../spec.md) | [plan.md](../plan.md) | [data-model.md](../data-model.md)

---

## Purpose

The **single, cross-cutting** contract for all per-entity module extensions. Every extension —
Teams or otherwise — implements `IModuleExtension` **directly**. There is **no** `I{Domain}Extension`
sub-interface; extensions are module-neutral and interchangeable, and the same extension may be bound
to more than one module in the same form.

Export and import are capabilities **on one extension**, not separate types. An extension is
instantiated with its **own custom config** (its own `IOptions<T>`) — contrast a Tool, a run-wide
singleton with one central config.

---

## Contract

```csharp
public interface IModuleExtension
{
    /// <summary>Owning module name (e.g. "Teams", "WorkItems").</summary>
    string Module { get; }

    /// <summary>Unique name within the module (e.g. "BoardConfig").</summary>
    string Name { get; }

    /// <summary>Execution order within the module. Lower runs first.</summary>
    int Order { get; }

    /// <summary>True when this extension participates in export.</summary>
    bool SupportsExport { get; }

    /// <summary>True when this extension participates in import.</summary>
    bool SupportsImport { get; }

    /// <summary>
    /// Parameterless. The extension answers from its OWN IOptions&lt;T&gt;:
    /// a mandatory extension returns true; an optional extension returns its own Enabled.
    /// Never reads a shared, module-level options object.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>Export phase for one entity. Cast context to the concrete type.</summary>
    Task ExportAsync(IExtensionContext context, CancellationToken ct);

    /// <summary>Import phase for one entity. TargetEntityId is set before this call.</summary>
    Task ImportAsync(IExtensionContext context, CancellationToken ct);
}
```

`IExtensionContext` is the module-neutral per-entity context (`Organisation`, `ProjectName`,
`EntityId`, `TargetEntityId`, `Package`). Each module supplies a concrete record implementing it;
extensions cast to the type they require.

---

## Constraints

- All extensions implement `IModuleExtension` directly — no `I{Domain}Extension`.
- `Module` / `Name` must not be null or whitespace; `Name` unique within the module.
- `IsEnabled` is **parameterless** and reads the extension's **own** `IOptions<T>` — pure, no I/O, no shared module options object.
- Default / mandatory / optional status is a property of the module→extension **binding**, not the extension. Mandatory extensions are forced enabled; disabling one is a fail-closed config error.
- `Order` has no enforced range; negatives permitted for extensions that must run before defaults.
- At least one of `SupportsExport` / `SupportsImport` should be `true`.

---

## Implementors

| Class | Module | Notes |
|-------|--------|-------|
| `BoardConfigTeamExtension` | Teams | Per-team board-config export/import extension |

---

## Related

- [`BoardConfigTeamExtension.md`](BoardConfigTeamExtension.md) — worked example: a Teams capability realised as an `IModuleExtension`
- [`.agents/10-contracts/specs/execution-contract.md`](../../../.agents/10-contracts/specs/execution-contract.md) — canonical interface surface and rules
- [`IModule.cs`](../../../../src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs) — `IModuleExtension` mirrors the both-directions phase shape of `IModule`
