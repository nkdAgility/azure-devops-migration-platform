# Module Architecture

## 7. Module Architecture

Each data type is implemented as a module conforming to the `IDataTypeModule` contract. Modules are the only extension point for adding new data types.

### IDataTypeModule Contract

```csharp
interface IDataTypeModule
{
    string Name { get; }
    IReadOnlyList<string> DependsOn { get; }

    Task ExportAsync(ExportContext context, CancellationToken ct);
    Task ImportAsync(ImportContext context, CancellationToken ct);
    Task ValidateAsync(ValidationContext context, CancellationToken ct);
}
```

### Contract Invariants

- `Name` is unique across all registered modules.
- `DependsOn` declares ordering constraints. The orchestrator resolves the dependency graph before execution; circular dependencies are a fatal configuration error.
- `ExportAsync` must write only via `IArtefactStore`.
- `ImportAsync` must read only via `IArtefactStore` and write state only via `IStateStore`.
- `ValidateAsync` must be side-effect free.
- Modules must never call source or target APIs directly — only through injected services.

### Dependency Graph Rules

- Dependencies are resolved topologically before execution begins.
- A module that depends on another module will not execute until the dependency completes successfully.
- Modules with no declared dependencies may execute in any order (or in parallel, if the orchestrator supports it in a future version).
- `IdentitiesModule` has no dependencies (`DependsOn` is empty) but must complete before any module that performs identity mapping. Any module that maps identities must include `"IdentitiesModule"` in its own `DependsOn` list. Failure to do so is a dependency graph error that the orchestrator must detect and reject at startup.

### Storage Rule

> Modules only use `IArtefactStore` and `IStateStore`. Direct filesystem access outside of these interfaces is forbidden.

### Module Responsibilities

| Module | Responsibility |
|---|---|
| `WorkItemsModule` | High-fidelity work item revision export/import, including fields, links, attachments, comments, and embedded images. Orchestrates `CommentsSubModule` (fetch comments from the ADO Comments API) and `EmbeddedImagesSubModule` (download and rewrite inline images from HTML/Markdown fields and comments). |
| `IdentitiesModule` | Export user/group descriptors; provide identity mapping service to all other modules |
| `TeamsModule` | Export and import team membership and settings |
| `PermissionsModule` | Export and import project and repository access control lists |
| `BuildsModule` | Export build pipeline definitions |
| `GitModule` | Export Git repository structure and optionally pack contents |

### Adding a New Module

See [.agents/guardrails/module-template.md](../.agents/guardrails/module-template.md) for the full checklist.
