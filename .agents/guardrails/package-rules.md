# Package Rules

These rules are mandatory for all code that reads or writes the migration package.

## Package as Source of Truth

1. The package is the source of truth for all migration state. No migration state may be held in memory, databases, or external services as the authoritative store.
2. All module code must access the package exclusively through `IArtefactStore` and `IStateStore`. Direct filesystem access is forbidden in module code.

## Path Determinism

1. Package paths must be deterministic and reproducible given the same source data. No random components in path generation.
2. All folders and files must follow the canonical naming conventions in `docs/package-format-reference.md`. Agent summaries in `.agents/context/` must stay consistent with that reference.
3. WorkItems must use the `yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` folder structure to ensure lexicographic chronological ordering.

## Zip-Friendly Layout

1. No path component may exceed the filesystem path length limits on Windows (260 characters total without long path support).
2. No special characters in path components beyond those allowed by all major filesystems.

## Binary Access

1. Binary attachments must be streamed via `IArtefactStore.WriteBinaryAsync()` and `IArtefactStore.ReadBinaryAsync()`. Buffering attachments in memory is forbidden.

## Hidden State

1. No migration state may be stored outside root `.migration/` (authoritative package state), project `/{org}/{project}/.migration/` (project-scoped cursors), or `.migration/runs/<runId>/` (run-scoped audit copies and logs). No hidden state in other locations.
2. No external databases or service-side state stores may be used as the authoritative resume mechanism.
3. Files under `.migration/runs/<runId>/` are audit artefacts only. Resume, phase gates, and orchestration MUST use root `.migration/` and project `/{org}/{project}/.migration/`, never run-scoped copies.

## Attachment Placement

1. Attachments must be stored beside the revision data in `attachments/` sub-folders within each revision directory. Global attachment dumping (all attachments in one folder) is forbidden.

## Enumeration

1. `IArtefactStore.EnumerateAsync()` returns results in lexicographic order. Do not sort the results in memory. Do not call `ToList()` or `ToArray()` on an `EnumerateAsync` result set.

## Related

- [migration-rules.md](./migration-rules.md) — migration behaviour rules
- [workitems-rules.md](./workitems-rules.md) — WorkItems-specific layout rules
- [.agents/context/migration-package-concept.md](../context/migration-package-concept.md) — package concept
- [docs/package-guide.md](../../docs/package-guide.md) — operator package guide
- [docs/package-format-reference.md](../../docs/package-format-reference.md) — canonical package format reference
