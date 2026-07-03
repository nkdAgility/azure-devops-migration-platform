# Configuration Model

Compressed configuration model for agents. See `docs/configuration-reference.md` for the full schema.

## Top-Level Structure

```json
{
  "MigrationPlatform": {
    "ConfigVersion": "2.0",
    "Mode": "Export",
    "Package": { ... },
    "Source": { ... },
    "Target": { ... },
    "Modules": { ... },
    "Environment": { ... }
  }
}
```

## Key Sections

| Section | Purpose |
|---|---|
| `ConfigVersion` | Schema version; required; must be `"2.0"` — v1 files are rejected at load and at ValidateOnStart with a rewrite recipe (ADR-0028) |
| `Mode` | What phase(s) to run: Inventory, Export, Prepare, Import, Validate, Migrate |
| `Package.WorkingDirectory` | Where the package lives on disk |
| `Source` | Source system type, URL, project, authentication |
| `Target` | Target system type, URL, project, authentication |
| `Modules` | Per-module `Enabled` flag plus the three-aspect anatomy below |
| `Environment.Type` | Deployment topology: `Standalone` or `Hosted` |

## Module Anatomy (ConfigVersion 2.0)

Every module (`WorkItems`, `Teams`, `Nodes`, `Identities`) expresses its options as exactly
three aspects (ADR-0028, module-anatomy contract):

| Aspect | Meaning | Examples |
|---|---|---|
| `Selection` | What to migrate | `WorkItems.Selection.Query`, `Teams.Selection.Scope`/`Filter` |
| `Data` | What to carry | `WorkItems.Data.Revisions`/`Comments`/`EmbeddedImages`, `Teams.Data.TeamSettings`/`TeamIterations`/`TeamMembers`/`TeamCapacity` |
| `Processing` | How to execute | `WorkItems.Processing.WorkItemResolutionStrategy`, `Teams.Processing.AlwaysExport`/`NodeTranslation`/`IdentityLookup`/`BoardConfig` |

The legacy v1 keys `Scope` and `Extensions` were removed in 2.0. The load-time gate
(`ConfigurationService.EnsureConfigVersion2`) and the ValidateOnStart gate
(`MigrationPlatformOptionsValidator`) reject:

- any `ConfigVersion` other than `"2.0"` (or a missing one), and
- stray `Scope`/`Extensions` keys under any `Modules.*` entry,

each with a step-by-step rewrite message. There is no legacy shim and no dual-read path.

## Authentication Conventions

- `AccessToken` uses `$ENV:VARNAME` syntax for environment variable resolution.
- `Authentication.Type` values: `AccessToken`, `Windows`, `ManagedIdentity`.
- `Windows` is TFS-only.

## Rules

- All config accessed through `IOptions<T>`. No direct `IConfiguration` access in modules.
- Breaking changes require a `ConfigVersion` bump; v2 was a hard cutover with actionable errors, not an upgrader.
- New properties must be added to `migration.schema.json` (generated from the options types at build).
- No undocumented properties.
- New modules must declare their anatomy via `IModuleContract`; `Scope`/`Extensions` must not reappear.
