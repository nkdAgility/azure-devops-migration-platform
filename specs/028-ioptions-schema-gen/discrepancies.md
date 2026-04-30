# Architecture Discrepancies

**Feature**: Schema Generation from IOptions DI Registrations  
**Flagged by**: speckit.specify  
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### SchemaOptionsEntry registry pattern not documented

- **Source doc**: `docs/configuration.md`
- **Section**: Section 10 — Configuration Model
- **Issue**: The spec introduces a `SchemaOptionsEntry` registry as the mechanism by which DI registrations drive schema generation. `docs/configuration.md` contains no description of this registry, how options classes register themselves, or how the schema is generated from registrations. The doc describes only the JSON structure of the config file, not how the schema is produced or kept accurate.
- **Suggested update**: Add a new subsection to `docs/configuration.md` (e.g. "Schema Generation") describing: (1) the `SectionName` constant pattern, (2) `SchemaOptionsEntry` registration in `Add*Services()`, (3) how the schema generator resolves all entries from the DI container, and (4) how to register a new options class.

---

### IMigrationJobContext not documented

- **Source doc**: `docs/configuration.md`
- **Section**: Section 10 — Configuration Model (or docs/modules.md)
- **Issue**: The spec introduces `IMigrationJobContext` as the canonical way for modules to access cross-cutting scalar job values (Mode, PackagePath, ConfigVersion) without injecting the full platform options graph. No existing documentation describes this service, its contract, or the rule that modules must use it instead of options navigation.
- **Suggested update**: Add a subsection to `docs/configuration.md` or `docs/modules.md` describing `IMigrationJobContext`: its properties, its lifecycle (scoped to a job), and the rule that modules must use it for Mode/PackagePath/ConfigVersion rather than `IOptions<MigrationOptions>`.

---

### ISourceEndpointInfo / ITargetEndpointInfo not documented

- **Source doc**: `docs/architecture.md` and/or `docs/modules.md`
- **Section**: Tools section / Connector registration
- **Issue**: The spec introduces connector-registered endpoint info services (`ISourceEndpointInfo`, `ITargetEndpointInfo`) so that modules can access the resolved source/target URL and project name without depending on connector-specific options. These abstractions are not described in any existing doc.
- **Suggested update**: Add a brief entry in `docs/architecture.md` (Tools or Components section) and/or `docs/modules.md` describing the endpoint info services, their connector-registered lifecycle, and the rule that modules must use them rather than injecting connector-specific options.

---

### VS Code json.schemas integration not documented

- **Source doc**: `docs/configuration.md`
- **Section**: N/A (not present)
- **Issue**: The spec requires that the committed schema file be registered in `.vscode/settings.json` so that VS Code automatically applies it to `migration.json` files. This is not described anywhere in the documentation.
- **Suggested update**: Add a brief note in `docs/configuration.md` (e.g. "IDE Integration") explaining the `json.schemas` registration and where the committed schema file is located.

---

### Monolithic MigrationOptions binding pattern described without deprecation notice

- **Source doc**: `docs/configuration.md`
- **Section**: Section 10 — Configuration Model (implicit — the JSON shape implies a single monolithic bind)
- **Issue**: The existing documentation describes the configuration structure in terms of the monolithic `MigrationOptions` type graph. After this feature, the canonical pattern is flat per-slice `IOptions<T>` registration; `MigrationOptions` is a write-time DTO only. The docs make no mention of this distinction.
- **Suggested update**: Add a note to `docs/configuration.md` clarifying that runtime config injection uses per-slice `IOptions<T>` and that `MigrationOptions` is a serialisation-only DTO retained for config file write operations. Mark any description of `MigrationOptions` as a runtime injection target as deprecated.
