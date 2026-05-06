# Architecture Discrepancies

**Feature**: IdentitiesModule & TeamsModule — Identity Mapping Pipeline and Team Migration
**Flagged by**: speckit.specify
**Status**: Resolved

## Discrepancies

### TeamsModule not documented in modules.md detail section
- **Source doc**: `docs/module-development-guide.md`
- **Section**: Module Responsibilities table (line 47) and beyond
- **Issue**: TeamsModule is listed in the Module Responsibilities table with a one-line description ("Export and import team membership and settings") but has no detailed section comparable to the WorkItemsModule ADO Export section. No config schema, extension list, scope types, or dependency graph entry is documented.
- **Suggested update**: Add a `### TeamsModule` section documenting: scope types (`teams`, `all`), four extensions (`TeamSettings`, `TeamIterations`, `TeamMembers`, `TeamCapacity`), dependency on `IdentitiesModule`, NodeStructureTool usage for iteration/area path resolution, and the requirement that it runs before `WorkItemsModule`.
- **Status**: ✓ Resolved in speckit.implement

### TeamsModule config not in configuration.md example
- **Source doc**: `docs/configuration-reference.md`
- **Section**: Full Schema (line 81–100, `modules` array)
- **Issue**: The `modules` array example only shows `WorkItems`. TeamsModule is not represented, so operators have no config reference for declaring the Teams module with its scopes and extensions.
- **Suggested update**: Add a `Teams` module entry to the `modules` array example showing `scopes` (type: `all`) and `extensions` (TeamSettings, TeamIterations, TeamMembers, TeamCapacity) with their enabled flags.
- **Status**: ✓ Resolved in speckit.implement

### Teams/ folder contents not documented in migration-package-concept.md
- **Source doc**: `.agents/context/migration-package-concept.md`
- **Section**: Package Structure (line 25–45)
- **Issue**: The `Teams/` folder is listed in the package tree but its internal structure (file naming, schema) is not documented. The spec defines five key entities (Team, TeamIteration, TeamMember, TeamCapacity, TeamAreaPath) that need a defined on-disk layout.
- **Suggested update**: Add a `### Teams/` subsection documenting the folder structure: one JSON file per team containing settings, iterations, members, capacity, and area path assignments. Define the file naming convention and schema version.
- **Status**: ✓ Resolved in speckit.implement

### TeamsModule dependency ordering not in modules.md dependency graph
- **Source doc**: `docs/module-development-guide.md`
- **Section**: Dependency Graph Rules (line 30–36)
- **Issue**: The dependency graph rules mention `IdentitiesModule` as a prerequisite for identity-mapping modules but do not explicitly state that `TeamsModule` must run before `WorkItemsModule`. The spec requires this ordering so that team board configurations are in place before work items are imported.
- **Suggested update**: Add to the Dependency Graph Rules section: "`TeamsModule` should be ordered after `IdentitiesModule` and `NodeStructureModule`, and before `WorkItemsModule`. Module execution order is controlled by the operator via configuration — there is no `DependsOn` property. The operator must ensure prerequisite modules complete before dependent modules run."
- **Status**: ✓ Resolved in speckit.implement

### TeamsModule cursor file not listed in migration-package-concept.md
- **Source doc**: `.agents/context/migration-package-concept.md`
- **Section**: Package Structure — `.migration/Checkpoints/` (line 38–40)
- **Issue**: The checkpoint listing shows `workitems.cursor.json`, `idmap.db`, and `export_progress.db` but does not list `teams.cursor.json`.
- **Suggested update**: Add `teams.cursor.json` to the `.migration/Checkpoints/` listing with a brief description: "cursor-based checkpoint for TeamsModule export/import resume."
- **Status**: ✓ Resolved in speckit.implement

### IdentitiesModule not yet implemented
- **Source doc**: `docs/module-development-guide.md`
- **Section**: Module Responsibilities table (line 46)
- **Issue**: `IdentitiesModule` is listed in the Module Responsibilities table ("Export user/group descriptors; provide identity mapping service to all other modules") but has no implementation in the codebase. `WorkItemsModule` already declares `DependsOn: ["Identities"]` against a non-existent module. TeamsModule has the same dependency. `IIdentityMappingService` interface exists but the module that populates identity mappings does not.
- **Suggested update**: IdentitiesModule is now included in this spec (User Story 0, FR-I01–FR-I12). Must be implemented as part of this feature. Add `### IdentitiesModule` detail section to `docs/module-development-guide.md` documenting: no dependencies, `Identities/` package folder, `descriptors.jsonl`/`mapping.json`/`unresolved.json` files, cross-cutting `IIdentityMappingService` singleton.
- **Status**: ✓ Resolved in speckit.implement

### IdentitiesModule config not in configuration.md example
- **Source doc**: `docs/configuration-reference.md`
- **Section**: Full Schema (line 81–100, `modules` array)
- **Issue**: The `modules` array example does not include an `Identities` module entry. Operators have no config reference.
- **Suggested update**: Add an `Identities` module entry to the `modules` array example. It has no scopes or extensions — it operates on the entire source project identity set.
- **Status**: ✓ Resolved in speckit.implement

### IdentitiesModule cursor not in migration-package-concept.md
- **Source doc**: `.agents/context/migration-package-concept.md`
- **Section**: Package Structure — `.migration/Checkpoints/`
- **Issue**: The checkpoint listing does not include `identities.cursor.json`.
- **Suggested update**: Add `identities.cursor.json` to the `.migration/Checkpoints/` listing.
- **Status**: ✓ Resolved in speckit.implement

