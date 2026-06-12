# Contract: `BoardConfigTeamExtension` (implements `IModuleExtension`)

**Class**: `BoardConfigTeamExtension`  
**Implements**: `IModuleExtension`  
**Namespace**: `DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions` (class) / `...Abstractions.Agent.Teams` (context)  
**Layer**: Infrastructure (class) over Abstractions (contract)  
**Spec**: [spec.md](../spec.md) | [plan.md](../plan.md) | [data-model.md](../data-model.md)

> **Model note.** There is **no `ITeamExtension`** and no `I{Domain}Extension` sub-interface. All
> extensions — Teams or otherwise — implement the single, cross-cutting `IModuleExtension` contract
> directly. This file documents the Teams board-config extension (`BoardConfigTeamExtension`) as the
> worked example of how a per-team capability is realised as an `IModuleExtension`.

---

## Purpose

A Teams-specific per-entity capability — board config, settings sync, etc. — is implemented as an
`IModuleExtension`. Export and import are **capabilities on one extension**, not separate types: a
single implementation class provides both `ExportAsync` and `ImportAsync`. This mirrors `IModule`,
which carries both phases on one interface.

An extension is thin, module-neutral, and interchangeable. It is **instantiated with its own custom
config** (its own `IOptions<T>`) — unlike a Tool, which is a run-wide singleton with one central
config. The same extension may, in the same form, be bound to more than one module.

---

## Contract

All extensions implement `IModuleExtension` directly:

```csharp
public interface IModuleExtension
{
    string Module { get; }       // owning module name, e.g. "Teams"
    string Name { get; }         // unique within module, e.g. "BoardConfig"
    int Order { get; }           // lower runs first
    bool SupportsExport { get; }
    bool SupportsImport { get; }
    bool IsEnabled { get; }      // parameterless — reads this extension's OWN IOptions<T>

    Task ExportAsync(IExtensionContext context, CancellationToken ct);
    Task ImportAsync(IExtensionContext context, CancellationToken ct);
}
```

A Teams board-config extension is therefore:

```csharp
public sealed class BoardConfigTeamExtension : IModuleExtension
{
    private readonly BoardConfigExtensionOptions _options;     // its OWN IOptions<T>
    private readonly ITeamBoardAdapter _adapter;

    public BoardConfigTeamExtension(IOptions<BoardConfigExtensionOptions> options, ITeamBoardAdapter adapter)
    { _options = options.Value; _adapter = adapter; }

    public string Module => "Teams";
    public string Name => "BoardConfig";
    public int Order => 100;
    public bool SupportsExport => true;
    public bool SupportsImport => true;

    // Parameterless. Optional extension → reads its own Enabled. (A mandatory extension would return true.)
    public bool IsEnabled => _options.Enabled;

    public Task ExportAsync(IExtensionContext context, CancellationToken ct) { /* cast to TeamExtensionContext */ }
    public Task ImportAsync(IExtensionContext context, CancellationToken ct) { /* ... */ }
}
```

`net481` (a build target of `DevOpsMigrationPlatform.Abstractions.Agent`) does not support default
interface methods (CS8701); every member is declared on the implementation class. No abstract base
class is introduced — interface-first DI ethos, and YAGNI with a single planned implementer.

---

## Per-extension configuration

The extension owns its own, distinct `IOptions<T>` — **not** a property nested inside a shared
`TeamsModuleExtensionsOptions` god-object:

```csharp
public sealed class BoardConfigExtensionOptions
{
    public bool Enabled { get; init; } = true;             // optional extension → has Enabled
    public bool Columns { get; init; } = true;
    public bool Rows { get; init; } = true;
    public bool CardRules { get; init; } = true;
    public BoardConfigImportMode ImportMode { get; init; } = BoardConfigImportMode.Replace;
}
```

Whether this extension is **default**, **mandatory**, or **optional** for the Teams module is a
property of the module→extension **binding** (decided when `TeamsModule` builds its list), not of the
extension type. A mandatory extension is forced enabled; an operator disabling a mandatory extension
is a fail-closed configuration error.

---

## Module → Orchestrator seam

`TeamsModule` builds the extension list and hands it off; `TeamsOrchestrator` runs it.

```
TeamsModule:
  1. resolve IEnumerable<IModuleExtension> from DI
  2. apply tiers: default + mandatory (force enabled; disabling = fail-closed error) + optional (IsEnabled)
  3. filter SupportsExport (export run) | SupportsImport (import run)
  4. sort by Order
  5. pass IReadOnlyList<IModuleExtension> → TeamsOrchestrator

TeamsOrchestrator:
  foreach team:
    build TeamExtensionContext (set TargetEntityId before import)
    foreach extension → ExportAsync / ImportAsync
    own checkpointing, metrics, progress events
```

---

## TeamExtensionContext

The host module supplies a concrete record implementing `IExtensionContext`. It does **not** carry a
shared module options object — each extension reads its own config:

```csharp
public sealed record TeamExtensionContext : IExtensionContext
{
    public required string Organisation { get; init; }
    public required string ProjectName { get; init; }
    public required string EntityId { get; init; }        // source team id
    public string? TargetEntityId { get; init; }          // null on export; set by orchestrator on import
    public required IPackageAccess Package { get; init; }

    public required TeamDefinition Team { get; init; }
    public required string Slug { get; init; }
    public string? SourceProjectName { get; init; }       // for path translation on import
}
```

Extensions cast the incoming `IExtensionContext` to `TeamExtensionContext`. They read from / write to
`Package` via `IPackageAccess` and must not cache package state between teams.

---

## Implementors

| Class | Implements | SupportsExport | SupportsImport | Order |
|-------|------------|:--------------:|:--------------:|------:|
| `BoardConfigTeamExtension` | `IModuleExtension` | ✅ | ✅ | 100 |

---

## Constraints

- All extensions implement `IModuleExtension` directly — no `ITeamExtension` / `I{Domain}Extension`.
- `Module` is `"Teams"` for every Teams extension; returned directly (no DIM — net481).
- `Name` must be unique within the module.
- `IsEnabled` is **parameterless** and reads the extension's **own** `IOptions<T>` — pure, no I/O, no shared module options object.
- `ExportAsync` / `ImportAsync` must be safe to call concurrently across teams; avoid shared mutable state.
- Extensions must not catch and swallow `OperationCanceledException` — propagate cancellation.

---

## Related

- [`IModuleExtension.md`](IModuleExtension.md) — the single extension contract
- [`ITeamBoardAdapter.md`](ITeamBoardAdapter.md) — adapter consumed by `BoardConfigTeamExtension` for both directions
- [`data-model.md`](../data-model.md#extension-architecture-contracts) — full C# definitions with inline commentary
- [`.agents/10-contracts/specs/execution-contract.md`](../../../.agents/10-contracts/specs/execution-contract.md) — canonical interface surface and rules
