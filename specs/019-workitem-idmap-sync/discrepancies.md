# Architecture Discrepancies

**Feature**: Work Item ID Map — Integrity, Rebuild, and Sync Support
**Flagged by**: speckit.specify
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### 1. idmap.db described as PostgreSQL but implemented as SQLite

- **Source doc**: `.agents/context/identity-and-mapping.md`
- **Section**: "ID Mapping (Work Item IDs)" — `idmap.db (PostgreSQL Portable binary in Local/Dedicated Server topology, PostgreSQL Flexible Server in Cloud topologies; preferred for large datasets)`
- **Issue**: The identity-and-mapping doc describes `idmap.db` as backed by PostgreSQL Portable, but the actual implementation (`SqliteIdMapStore.cs`) uses SQLite via `Microsoft.Data.Sqlite`. The checkpointing doc also references PostgreSQL for `idmap.db`.
- **Suggested update**: Update `.agents/context/identity-and-mapping.md` and `.agents/context/checkpointing-summary.md` to reflect the actual SQLite-backed implementation: `idmap.db (SQLite — package-local indexed storage, not a control-plane database)`.

### 2. checkpointing-summary.md describes idmap.db as PostgreSQL-backed

- **Source doc**: `.agents/context/checkpointing-summary.md`
- **Section**: "Per-Module Cursors" — `idmap.db (ID map — source workItemId → target workItemId; backed by PostgreSQL Portable binary in Local/Dedicated Server topology or PostgreSQL Flexible Server in Cloud topologies)`
- **Issue**: Same as discrepancy 1 — the doc says PostgreSQL but the implementation is SQLite.
- **Suggested update**: Change to `idmap.db (SQLite — source workItemId → target workItemId mapping; package-local indexed storage)`.

### 3. No CLI command for ID map rebuild or integrity check

- **Source doc**: `docs/cli-guide.md`
- **Section**: (no existing section)
- **Issue**: This spec introduces ID map rebuild and integrity check capabilities (FR-005, FR-010, FR-011) but no CLI command exists to trigger them explicitly. The rebuild currently happens implicitly at import startup via `IWorkItemResolutionStrategy.SeedAsync`. A future explicit `rebuild-idmap` or `check-idmap` CLI command may be needed.
- **Suggested update**: Defer to planning phase — determine whether these should be explicit CLI commands or remain implicit import-startup behaviour. If CLI commands are added, document them in `docs/cli-guide.md` and `.agents/context/cli-commands.md`.

### 4. No documentation of revision-level tracking in ID map

- **Source doc**: `.agents/context/checkpointing-summary.md`
- **Section**: "ID Map"
- **Issue**: The spec introduces `last_revision_index` tracking per work item (FR-009). The current documentation only describes the ID map as storing `source_id → target_id` mappings and attachment mappings. Revision-level tracking is a new capability.
- **Suggested update**: Add a note to the checkpointing doc's ID Map section: `The work_item_map table also tracks last_revision_index per source work item, enabling revision-level skip logic during sync/rerun imports.`

### 5. Rerun/sync scenario not documented in import-streaming.md

- **Source doc**: `.agents/context/import-streaming.md`
- **Section**: "Staged Import Semantics"
- **Issue**: The spec describes re-export followed by re-import (User Story 3), where the export adds new revision folders and the import processes only the delta. The current import-streaming doc assumes a single forward-only pass. The interaction between export cursor and import cursor for multi-pass scenarios is undocumented.
- **Suggested update**: Add a "Rerun / Sync Import" section to `import-streaming.md` describing: (a) how the export cursor enables delta export, (b) how the import cursor enables delta import, and (c) how `idmap.db` revision-level tracking enables per-work-item skip logic for already-applied revisions.
