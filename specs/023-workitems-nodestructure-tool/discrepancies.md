# Architecture Discrepancies

**Feature**: WorkItemsModule — NodeStructure Tool (M2 / T2)  
**Flagged by**: speckit.specify  
**Status**: All resolved

---

## Discrepancies

### NodeStructure tool missing from docs/configuration-reference.md

- **Status**: Resolved — `### NodeStructure Tool` section added to `docs/configuration-reference.md` under `## Tools`.

---

### NodeStructureTool missing from docs/module-development-guide.md Tool Resolution section

- **Status**: Resolved — `NodeStructureTool` added to the Tool Resolution table in `docs/module-development-guide.md`.

---

### Nodes/ package artifacts not in canonical package layout

- **Status**: Resolved — `Nodes/` folder with `source-tree.json` and `referenced-paths.json` added to `.agents/context/migration-package-concept.md`.

---

### INodeStructureTool interface not yet defined in Abstractions

- **Status**: Resolved — `INodeStructureTool` defined in `DevOpsMigrationPlatform.Abstractions` with pure path-mapping contract.

---

### Config schema conflict: proposed-features.md uses array-with-id; canonical pattern is keyed-object

- **Status**: Resolved in spec (array-with-id removed); `proposed-features.md` pattern deferred.

---

### NodeStructureTool violates documented tool purity contract

- **Status**: Resolved in plan (split architecture). `INodeStructureTool` is pure; I/O handled by `INodeCreator` infrastructure service.

---

### RevisionFolderProcessorFactory does not pass IFieldTransformTool (pre-existing)

- **Status**: N/A for this spec — factory integration addressed as part of tool wiring in this implementation.


---

## Discrepancies

### NodeStructure tool missing from docs/configuration-reference.md

- **Source doc**: `docs/configuration-reference.md`
- **Section**: `## Tools` (line 501+)
- **Issue**: The `Tools` section documents only the `FieldTransform` tool. The `NodeStructure` tool type, its JSON schema, and all configuration fields (`areaMap`, `iterationMap`, `areaLanguageOverride`, `iterationLanguageOverride`, `createMissingNodes`, `skipRevisionWithInvalidAreaPath`, `skipRevisionWithInvalidIterationPath`, `replicateAllExistingNodes`) are not documented.
- **Suggested update**: Add a `### NodeStructure Tool` subsection to `docs/configuration-reference.md` under `## Tools`, mirroring the `FieldTransform` section structure. Include the schema table and a JSON example matching the config format defined in `analysis/proposed-features.md` M2.

---

### NodeStructureTool missing from docs/module-development-guide.md Tool Resolution section

- **Source doc**: `docs/module-development-guide.md`
- **Section**: `### Tool Resolution` (line 86–91)
- **Issue**: The Tool Resolution section states that "For the full tool schema and available tool types, see docs/configuration-reference.md — Tools" but lists no concrete tool types. Once `NodeStructureTool` is implemented it should be listed, and the `WorkItemsModule` responsibility row should be updated to note that the `Revisions` extension optionally consumes `INodeStructureTool`.
- **Suggested update**: Add `NodeStructureTool` as a known tool type reference; update the `WorkItemsModule` module responsibility description to mention `NodeStructureTool` as an optional tool loaded by the `Revisions` extension.

---

### Nodes/ package artifacts not in canonical package layout

- **Source doc**: `docs/architecture.md` / `.agents/context/migration-package-concept.md`
- **Section**: Package layout specification
- **Issue**: The spec introduces new export artifacts `Nodes/source-tree.json` and `Nodes/referenced-paths.json`. These artifacts are not documented in the canonical package layout. The package format docs need to add `Nodes/` as a top-level module folder alongside `WorkItems/`, `Teams/`, etc.
- **Suggested update**: Add `Nodes/` to the package layout documentation with its two artifacts (`source-tree.json` with area/iteration tree schema, `referenced-paths.json` with discovered paths schema) and note that both are always written on export.

---

### INodeStructureTool interface not yet defined in Abstractions

- **Source doc**: `.agents/guardrails/architecture-boundaries.md`
- **Section**: Rule 21 — Mandatory reuse of existing architecture and patterns
- **Issue**: The spec introduces `INodeStructureTool` as a new injectable interface. This interface does not yet exist in `DevOpsMigrationPlatform.Abstractions`. Per rule 21, new abstractions must be defined in `Abstractions` and must be used by at least two independent modules (`WorkItemsModule` and `TeamsModule` are the two planned consumers, satisfying the rule). The interface must be designed with both consumers in mind before it is finalised.
- **Suggested update**: Document `INodeStructureTool` in `DevOpsMigrationPlatform.Abstractions` during implementation. The plan phase must document the anticipated `TeamsModule` calling contract to validate the interface is sufficiently general before it is frozen.

---

### Config schema conflict: proposed-features.md uses array-with-id; canonical pattern is keyed-object

- **Source doc**: `analysis/proposed-features.md` M2, `docs/configuration-reference.md` — Tools section
- **Section**: `MigrationPlatform.Tools` config root
- **Issue**: `analysis/proposed-features.md` proposes a new array-based tool declaration schema: `"tools": [{ "id": "nodes-default", "type": "NodeStructure", ... }]` with extension references via `{ "ref": "<id>", "overrides": { ... } }`. The established canonical pattern (used by `FieldTransform`) is a **keyed object**: `"Tools": { "NodeStructure": { ... } }` where the key is the type name and there is no `id` or `ref` field. The spec was initially drafted using the array-with-id pattern. This was **incorrect** — the spec has been corrected to use the keyed-object pattern. However, the conflict in `proposed-features.md` remains: if the `ref`/`overrides` mechanism is desired in the future (to allow per-extension overrides), that is a separate config schema evolution feature and must not be introduced here without a dedicated spec.
- **Status**: Resolved in spec (array-with-id removed); `proposed-features.md` pattern deferred.

---

### NodeStructureTool violates documented tool purity contract

- **Source doc**: `docs/module-development-guide.md`
- **Section**: `### Tool Resolution` (line 86–91)
- **Issue**: The Tool Resolution section states: "Tools are pure transformations or lookup services — they perform no I/O and carry no mutable state." The NodeStructureTool as specified requires ADO API calls (node creation) and `IStateStore` writes (checkpointing). This violates the documented purity contract.
- **Resolution**: The plan splits the tool into two concerns: (1) `INodeStructureTool` — a **pure** path-mapping interface (no I/O, no state), consistent with the documented contract; (2) `INodeCreator` — a separate **infrastructure service** for node creation I/O, consumed at the orchestration layer (not inside the per-revision tool call). The `docs/module-development-guide.md` Tool Resolution section does NOT need amendment — the `INodeStructureTool` interface complies as-is. The `INodeCreator` is not a "tool" — it is an infrastructure service.
- **Status**: Resolved in plan (split architecture).

---

### RevisionFolderProcessorFactory does not pass IFieldTransformTool (pre-existing)

- **Source doc**: Implementation code
- **Section**: `RevisionFolderProcessorFactory.Create()`
- **Issue**: The factory does not currently pass `IFieldTransformTool` to `RevisionFolderProcessor` (constructor has optional `fieldTransformTool` parameter, factory passes `null`). This is a pre-existing integration gap. The NodeStructureTool will need the same factory integration path.
- **Suggested update**: Extend `IRevisionFolderProcessorFactory.Create()` to accept optional tool parameters, or inject tools via factory constructor.
- **Status**: Pending — to be addressed during implementation (factory must be updated for both `IFieldTransformTool` and `INodeStructureTool`).


