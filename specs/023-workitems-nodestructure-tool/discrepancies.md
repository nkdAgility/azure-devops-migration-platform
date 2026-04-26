# Architecture Discrepancies

**Feature**: WorkItemsModule — NodeStructure Tool (M2 / T2)  
**Flagged by**: speckit.specify  
**Status**: Pending rectification (resolve in speckit.implement)

---

## Discrepancies

### NodeStructure tool missing from docs/configuration.md

- **Source doc**: `docs/configuration.md`
- **Section**: `## Tools` (line 501+)
- **Issue**: The `Tools` section documents only the `FieldTransform` tool. The `NodeStructure` tool type, its JSON schema, and all configuration fields (`areaMap`, `iterationMap`, `areaLanguageOverride`, `iterationLanguageOverride`, `createMissingNodes`, `skipRevisionWithInvalidAreaPath`, `skipRevisionWithInvalidIterationPath`, `replicateAllExistingNodes`) are not documented.
- **Suggested update**: Add a `### NodeStructure Tool` subsection to `docs/configuration.md` under `## Tools`, mirroring the `FieldTransform` section structure. Include the schema table and a JSON example matching the config format defined in `analysis/proposed-features.md` M2.

---

### NodeStructureTool missing from docs/modules.md Tool Resolution section

- **Source doc**: `docs/modules.md`
- **Section**: `### Tool Resolution` (line 86–91)
- **Issue**: The Tool Resolution section states that "For the full tool schema and available tool types, see docs/configuration.md — Tools" but lists no concrete tool types. Once `NodeStructureTool` is implemented it should be listed, and the `WorkItemsModule` responsibility row should be updated to note that the `Revisions` extension optionally consumes `INodeStructureTool`.
- **Suggested update**: Add `NodeStructureTool` as a known tool type reference; update the `WorkItemsModule` module responsibility description to mention `NodeStructureTool` as an optional tool loaded by the `Revisions` extension.

---

### classification-nodes.json package artifact not in canonical package layout

- **Source doc**: `docs/architecture.md` / `.agents/context/package-format.md`
- **Section**: Package layout specification
- **Issue**: The spec introduces a new export artifact `WorkItems/classification-nodes.json` (FR-015a). This artifact is not documented in the canonical package layout. The package format docs need to list it alongside `revision.json` as a valid top-level artifact under `WorkItems/`.
- **Suggested update**: Add `classification-nodes.json` to the `WorkItems/` section of the package layout documentation, with its schema (`{ "areaNodes": [...], "iterationNodes": [...] }`) and the condition under which it is written (`replicateAllExistingNodes: true`).

---

### INodeStructureTool interface not yet defined in Abstractions

- **Source doc**: `.agents/guardrails/system-architecture.md`
- **Section**: Rule 21 — Mandatory reuse of existing architecture and patterns
- **Issue**: The spec introduces `INodeStructureTool` as a new injectable interface. This interface does not yet exist in `DevOpsMigrationPlatform.Abstractions`. Per rule 21, new abstractions must be defined in `Abstractions` and must be used by at least two independent modules (`WorkItemsModule` and `TeamsModule` are the two planned consumers, satisfying the rule). The interface must be designed with both consumers in mind before it is finalised.
- **Suggested update**: Document `INodeStructureTool` in `DevOpsMigrationPlatform.Abstractions` during implementation. The plan phase must document the anticipated `TeamsModule` calling contract to validate the interface is sufficiently general before it is frozen.

