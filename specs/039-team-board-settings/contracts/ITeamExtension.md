# Contract: ITeamExtension

**Namespace**: `DevOpsMigrationPlatform.Abstractions.Agent.Teams`  
**Assembly**: `DevOpsMigrationPlatform.Abstractions.Agent`  
**Layer**: Abstractions  
**Spec**: [spec.md](../spec.md) | [plan.md](../plan.md) | [data-model.md](../data-model.md)

---

## Purpose

Teams-specific per-entity extension contract. Every board config, settings sync,
or other per-team capability is implemented as an `ITeamExtension`.

Export and import are **capabilities on one extension**, not separate types.
A single implementation class provides both `ExportAsync` and `ImportAsync`.
This mirrors the structure of `IModule`, which carries both `ExportAsync` and
`ImportAsync` on a single interface.

---

## Contract

```csharp
/// <summary>
/// Extension that participates in per-team export and/or import.
/// Export and import are capabilities on a single extension — not separate types.
/// </summary>
public interface ITeamExtension : IModuleExtension
{
    /// <summary>
    /// Default implementation binds the Module property to "Teams".
    /// Implementors must not override this.
    /// </summary>
    string IModuleExtension.Module => "Teams";

    /// <summary>
    /// Returns true when this extension is active under the current options.
    /// Called by TeamsModule before passing the extension to the orchestrator.
    /// </summary>
    bool IsEnabled(TeamsModuleExtensionsOptions options);

    /// <summary>
    /// Executes the export phase for one team.
    /// Called once per team by TeamsOrchestrator when SupportsExport == true
    /// and IsEnabled returns true.
    /// </summary>
    Task ExportAsync(TeamExtensionContext context, CancellationToken ct);

    /// <summary>
    /// Executes the import phase for one team.
    /// Called once per team by TeamsOrchestrator when SupportsImport == true
    /// and IsEnabled returns true.
    /// </summary>
    Task ImportAsync(TeamExtensionContext context, CancellationToken ct);
}
```

---

## Default Interface Method Behaviour

When `SupportsExport == false`, the implementation should declare:

```csharp
public Task ExportAsync(TeamExtensionContext context, CancellationToken ct)
    => Task.CompletedTask;
```

When `SupportsImport == false`, the implementation should declare:

```csharp
public Task ImportAsync(TeamExtensionContext context, CancellationToken ct)
    => Task.CompletedTask;
```

These are declared on the **implementation class**, not as interface default methods,
to keep compatibility with .NET 4.8.1 targets.

---

## Module → Orchestrator Seam

`TeamsModule` owns enablement filtering and ordering. It:

1. Resolves `IEnumerable<ITeamExtension>` from DI.
2. Filters to `IsEnabled(options)`.
3. Filters to `SupportsExport` (for export runs) or `SupportsImport` (for import runs).
4. Sorts by `Order`.
5. Passes the resulting `IReadOnlyList<ITeamExtension>` to `TeamsOrchestrator`.

`TeamsOrchestrator` owns the per-team loop. It:

1. Iterates teams from the package.
2. Builds a `TeamExtensionContext` for each team.
3. Calls `ExportAsync` or `ImportAsync` on every extension in the filtered list.
4. Handles checkpointing, metrics, and progress events at the orchestrator level.

---

## TeamExtensionContext

```csharp
public sealed record TeamExtensionContext(
    string Organisation,
    string Project,
    string SourceProject,
    TeamDefinition Team,
    string Slug,
    IPackageAccess Package,
    TeamsModuleExtensionsOptions Extensions,
    IProgressSink? ProgressSink);
```

Extensions read from `Package` (import) or write to `Package` (export) via
`IPackageAccess`. They must not cache package state between teams.

---

## Implementors

| Class | SupportsExport | SupportsImport | Order |
|-------|:--------------:|:--------------:|------:|
| `BoardConfigTeamExtension` | ✅ | ✅ | 100 |

---

## Constraints

- `Module` is always `"Teams"` (enforced by the default interface method).
- `Name` must be unique across all registered `ITeamExtension` implementations.
- `IsEnabled` must be a pure function of `options` — no I/O, no state.
- `ExportAsync` and `ImportAsync` must be safe to call concurrently across teams (the orchestrator may introduce per-team parallelism in future); avoid shared mutable state.
- Extensions must not catch and swallow `OperationCanceledException` — propagate cancellation.

---

## Related

- [`IModuleExtension.md`](IModuleExtension.md) — base marker interface
- [`ITeamBoardAdapter.md`](ITeamBoardAdapter.md) — adapter consumed by `BoardConfigTeamExtension` for both export and import
- [`data-model.md`](../data-model.md#extension-architecture-contracts) — full C# definitions with inline commentary
