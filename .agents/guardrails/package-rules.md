# Package Rules

These rules are mandatory for all code that reads or writes the migration package.

## Package as Source of Truth

1. The package is the source of truth for all migration state. No migration state may be held in memory, databases, or external services as the authoritative store.
2. All module code must access the package exclusively through `IArtefactStore` and `IStateStore`. Direct filesystem access is forbidden in module code.

## Path Determinism

3. Package paths must be deterministic and reproducible given the same source data. No random components in path generation.
4. All folders and files must follow the canonical naming conventions in `.agents/context/migration-package-concept.md`.
5. WorkItems must use the `yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` folder structure to ensure lexicographic chronological ordering.

## Zip-Friendly Layout

6. No path component may exceed the filesystem path length limits on Windows (260 characters total without long path support).
7. No special characters in path components beyond those allowed by all major filesystems.

## Binary Access

8. Binary attachments must be streamed via `IArtefactStore.WriteBinaryAsync()` and `IArtefactStore.ReadBinaryAsync()`. Buffering attachments in memory is forbidden.

## Hidden State

9. No migration state may be stored outside `.migration/Checkpoints/` (cursors), `.migration/State/` (state stores), or `.migration/Logs/` (logs). No hidden state in other locations.
10. No external databases or service-side state stores may be used as the authoritative resume mechanism.

## Attachment Placement

11. Attachments must be stored beside the revision data in `attachments/` sub-folders within each revision directory. Global attachment dumping (all attachments in one folder) is forbidden.

## Enumeration

12. `IArtefactStore.EnumerateAsync()` returns results in lexicographic order. Do not sort the results in memory. Do not call `ToList()` or `ToArray()` on an `EnumerateAsync` result set.

## Related

- [migration-rules.md](./migration-rules.md) — migration behaviour rules
- [workitems-rules.md](./workitems-rules.md) — WorkItems-specific layout rules
- [.agents/context/migration-package-concept.md](../context/migration-package-concept.md) — package concept
- [docs/package-guide.md](../docs/package-guide.md) — operator package guide
- [docs/package-format-reference.md](../docs/package-format-reference.md) — format reference