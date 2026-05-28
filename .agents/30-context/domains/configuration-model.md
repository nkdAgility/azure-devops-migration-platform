# Configuration Model

Compressed configuration model for agents. See `docs/configuration-reference.md` for the full schema.

## Top-Level Structure

```json
{
  "MigrationPlatform": {
    "ConfigVersion": "1.0",
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
| `ConfigVersion` | Schema version; required; triggers upgrade check at startup |
| `Mode` | What phase(s) to run: Inventory, Export, Prepare, Import, Validate, Migrate |
| `Package.WorkingDirectory` | Where the package lives on disk |
| `Source` | Source system type, URL, project, authentication |
| `Target` | Target system type, URL, project, authentication |
| `Modules` | Per-module `Enabled` flags and module-specific settings |
| `Environment.Type` | Deployment topology: `Standalone` or `Hosted` |

## Authentication Conventions

- `AccessToken` uses `$ENV:VARNAME` syntax for environment variable resolution.
- `Authentication.Type` values: `AccessToken`, `Windows`, `ManagedIdentity`.
- `Windows` is TFS-only.

## Rules

- All config accessed through `IOptions<T>`. No direct `IConfiguration` access in modules.
- Breaking changes require a `ConfigVersion` bump and an upgrader.
- New properties must be added to `migration.schema.json`.
- No undocumented properties.


