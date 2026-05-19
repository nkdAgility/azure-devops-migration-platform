# Architecture Discrepancies

**Feature**: Work Item ID Map — Integrity, Rebuild, and Sync Support
**Flagged by**: speckit.specify
**Status**: Partially resolved (reconciled 2026-05-16)

## Discrepancies

### 1. idmap.db described as PostgreSQL but implemented as SQLite

- **Source doc**: `.agents/30-context/domains/identity-and-mapping.md`
- **Status**: Resolved (covered by T031)
- **Reconciliation note**: Current identity-and-mapping context reflects SQLite-backed package-local storage.

### 2. checkpointing-summary.md describes idmap.db as PostgreSQL-backed

- **Source doc**: `.agents/30-context/domains/checkpointing-summary.md`
- **Status**: Partially resolved (T032 incomplete)
- **Reconciliation note**: Base PostgreSQL wording has been corrected, but required notes for `last_revision_index` and package-lock behavior are still missing.

### 3. No CLI command for ID map rebuild or integrity check

- **Source doc**: `docs/cli-guide.md`
- **Status**: Open (T034 incomplete)
- **Reconciliation note**: CLI guide still lacks explicit operator-facing note that rebuild/integrity are implicit import-startup behaviors.

### 4. No documentation of revision-level tracking in ID map

- **Source doc**: `.agents/30-context/domains/checkpointing-summary.md`
- **Status**: Open (T032 incomplete)
- **Reconciliation note**: `last_revision_index` behavior is implemented in code but not yet documented in checkpointing summary.

### 5. Rerun/sync scenario not documented in import-streaming.md

- **Source doc**: `.agents/30-context/domains/import-streaming.md`
- **Status**: Open (T033 incomplete)
- **Reconciliation note**: Required rerun/sync section (export cursor + import cursor + revision watermark interplay) is still missing.
