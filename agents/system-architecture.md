# System Architecture — Hard Guardrails

These rules are non-negotiable. They are distilled from the full reference set in [docs/](../docs/). In any conflict between these rules and any documentation in `/docs`, **these rules win**. The docs define architectural intent; the `/agents` guardrails enforce it. The binding entry point is [agents.md](../agents.md).

## Absolute Rules

1. **WorkItems chronological layout is canonical.**
   The folder structure `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` must not be altered. No renaming, reordering, or flattening.

2. **Import must be streaming.**
   Load and process one revision folder at a time. Loading all revisions into memory before processing is forbidden.

3. **No global in-memory sort.**
   Enumeration order is determined by lexicographic folder traversal. Sorting in memory defeats the purpose of the layout and breaks memory safety for large datasets.

4. **Cursor-based checkpoints are required.**
   Every module must maintain a cursor file under `Checkpoints/`. Watermark tables, databases, or in-memory progress tracking are not acceptable substitutes.

5. **Attachments are stored beside revision.json.**
   Attachment files live in the same folder as their `revision.json`. There is no global `Attachments/` root and no mandatory blob store.

6. **No source-to-target direct migration.**
   The system is a package platform. Source data is always written to the package first. Import always reads from the package. Direct source-to-target calls in any module are forbidden.

7. **Modules only through IArtefactStore and IStateStore.**
   Modules must not access the filesystem directly, call source/target APIs outside of the export/import context, or share state through globals.

8. **Identity is a cross-cutting service.**
   No module implements its own identity resolution. All modules use `IIdentityMappingService`. `IdentitiesModule` must complete before any module that maps identities.

9. **Config and schema versioning with upgrader.**
   Breaking changes to config or package schema require a version increment and a corresponding upgrader. There is no backwards compatibility without an upgrader.

10. **Validate before import.**
    In `Both` mode, a validation pass runs after export and before import. Import must not begin on a package that fails validation. Post-flight validation must also run after import. See [docs/validation.md](../docs/validation.md) for the full check list and configuration.

## If in Doubt

Consult [docs/architecture.md](../docs/architecture.md). If the answer is not there, the safest default is to preserve the package layout, maintain streaming behaviour, and write state only through the defined interfaces.
