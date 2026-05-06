# Draft Specification: ImportType — DryRun / ProductionRun

**Status**: Draft — all questions resolved; ready for promotion to `specs/`  
**Created**: 2026-04-29  
**Author**: Initial draft from design discussion; decisions incorporated 2026-04-29

---

## Summary

Add a first-class `ImportType` concept to the platform with two values: `DryRun` (default) and `ProductionRun`.

`ImportType` is a **safety guardrail**, not an operator tool. It is set in the config file but its enforcement is performed by the platform at import time — the operator cannot suppress or bypass the effects by overriding `NodeStructure` mappings or `FieldTransform` rules. It makes the system **safe by default**: if an operator forgets to set it or creates a new config file, the import cannot corrupt the live project tree.

### DryRun effects (applied automatically by the platform at import time)

| What | How |
|---|---|
| All work items are placed under a root node `DryRun` in the area/iteration tree | Platform injects a terminal `DryRun\` prefix step **after** all operator `NodeStructure` mappings resolve |
| All work item titles are prefixed with `[DRY RUN] ` | Platform injects a terminal `PrependString` transform on `System.Title` **after** all operator `FieldTransform` groups complete |
| The `DryRun` root node is auto-created if it does not already exist | Platform forces `AutoCreateNodes` equivalent for this single node |
| Teams area/iteration path assignments use `DryRun\`-prefixed paths | Applied in `TeamsModule.ImportAsync` consistently with `WorkItemsModule` |

### ProductionRun effects

No platform-injected changes. The operator's config runs as-is. No confirmation field or CLI flag required — setting `ImportType: ProductionRun` is sufficient.

---

## Scope

Affects:

- `MigrationOptions` (config model — new `ImportType` property)
- `MigrationJob` (job contract — carries `ImportType` to the agent; operator cannot modify on the agent side)
- `WorkItemsModule.ImportAsync` (enforces `[DRY RUN] ` title prefix)
- `NodesModule.ImportAsync` and `NodeEnsurer` (enforces `DryRun\` path prefix and root node creation)
- `TeamsModule.ImportAsync` (enforces `DryRun\` area/iteration path assignments)
- `QueueCommand` (copies `ImportType` from config to job contract)
- `MigrationOptionsValidator` (emits structured `Warning` when `ProductionRun` is set)
- `docs/configuration-reference.md` (new top-level field documentation)
- `.agents/context/job-lifecycle.md` (new contract field)

Does **not** affect:

- Export path — `ExecutionMode` is an import-time concept only
- `FieldTransformOptions` — no operator-visible change; injection is internal
- `NodeStructureOptions` — no operator-visible change; injection is a post-processing step

---

## Configuration

### Schema addition to `MigrationOptions`

```json
{
  "MigrationPlatform": {
    "ImportType": "DryRun",
    "Mode": "Import",
    ...
  }
}
```

To run a production import:

```json
{
  "MigrationPlatform": {
    "ImportType": "ProductionRun",
    "Mode": "Import",
    ...
  }
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `ImportType` | `string` | `"DryRun"` | `DryRun` or `ProductionRun`. Omitting the field is equivalent to `DryRun`. No additional confirmation field is required. |

---

## Implementation Design

### 1. New enum in `DevOpsMigrationPlatform.Abstractions`

```csharp
// Abstractions/Options/ImportType.cs
public enum ImportType
{
    DryRun,
    ProductionRun
}
```

### 2. `MigrationOptions` — add property

```csharp
// Default to DryRun (safe by default)
public ImportType ImportType { get; set; } = ImportType.DryRun;
```

### 3. `MigrationJob` — add property (carries to agent; init-only)

```csharp
// Abstractions/Jobs/MigrationJob.cs
public ImportType ImportType { get; init; } = ImportType.DryRun;
```

### 4. `QueueCommand` / job construction

Copy `config.ImportType` → `job.ImportType` when constructing the `MigrationJob`. The CLI does not enforce effects — it is a transparent pass-through.

### 5. Node path enforcement (`NodesModule` / `NodeEnsurer`)

When `job.ImportType == DryRun`:

- Inject a post-processing step in `NodeEnsurer.EnsureReferencedPathsAsync` and `NodeEnsurer.ReplicateSourceTreeAsync` that wraps every target path as `DryRun\{translatedPath}`.
- The `DryRun` root node itself must be created if absent (idempotent call to `INodeCreator.EnsureExistsAsync`).
- This wrapping happens **after** `INodeStructureTool.TranslatePath` has already been called — the operator mappings run first, then the platform appends the `DryRun\` prefix to the result.
- Log at `Information` level: `[DryRun] Wrapping target path '{original}' → '{dryRunPath}'`.

### 6. Work item title enforcement (`WorkItemsModule` / `RevisionFolderProcessor`)

When `job.ImportType == DryRun`:

- After the operator `FieldTransformTool.ApplyTransforms` result is obtained, apply a platform-owned `PrependStringTransform("[DRY RUN] ", "System.Title")` to the output.
- `PrependStringTransform` is a new, simple internal transform (not exposed to operator configuration) that prepends a literal string to a string field. It is idempotent: if the field already starts with the prefix it is not applied again.
- Log at `Debug` level per work item: `[DryRun] Prepended title prefix for WI {id}`.

**Idempotency requirement**: If the operator re-runs in `DryRun` mode, titles must not accumulate `[DRY RUN][DRY RUN]` prefixes. The transform checks whether `System.Title` already starts with `[DRY RUN] ` before prepending.

### 7. TeamsModule path enforcement

When `job.ImportType == DryRun`, `TeamsModule.ImportAsync` applies the same `DryRun\` prefix to all area path and iteration path assignments for teams, using the same terminal injection pattern as `NodeEnsurer`.

### 8. Validator warning

`MigrationOptionsValidator` emits a structured `Warning` log when `ImportType == ProductionRun` (never an error — `ProductionRun` is a valid choice):

```
WARN [ImportType] ImportType is 'ProductionRun'. Work items will be imported without the DryRun prefix and under the real area/iteration tree. Ensure you have reviewed the prepared mapping reports before proceeding.
```

### 9. Observability

| Metric / Span | Where | Description |
|---|---|---|
| `migration.import_type` tag | All import spans | Low-cardinality tag: `dry_run` or `production_run` |
| Structured log at `Information` on import start | `WorkItemsModule.ImportAsync` | `[ImportType] Running import in {mode} mode.` |
| Structured log at `Warning` when `ProductionRun` | `MigrationOptionsValidator` | See §8 above |

---

## Open Questions

None — all questions resolved. See Decisions table above.

---


---

## Decisions (recorded)

| # | Decision |
|---|---|
| D1 | Title prefix is `[DRY RUN] ` (fixed, not configurable) |
| D2 | DryRun root node name is `DryRun` (fixed, not configurable) |
| D3 | `TeamsModule` applies `DryRun\` path prefix consistently with `WorkItemsModule` |
| D4 | `Prepare` mode is unaffected — validates operator paths only, not DryRun-prefixed paths |
| D5 | `ProductionRun` requires only setting `ImportType: ProductionRun` — no additional confirmation field |
| D7 | `ImportType` is silently ignored on Export-only runs (no validation warning) |
| D8 | Idempotency check for `[DRY RUN] ` is on the revision value from `revision.json`, not the live target state |

## Assumptions (to be validated)

1. `ImportType` applies only when `Mode` is `Import` or `Migrate` (import leg). Export-only runs ignore it entirely.
2. The `DryRun` root node is created at the project root of the **target** project, not the source project name.
3. `PrependStringTransform` is internal — not registered in `FieldTransformFactory`, not documented in the `FieldTransform` configuration schema, not visible to operators.
4. The `MigrationJob.ImportType` field is included in the `configHash` computation so a change from `DryRun` to `ProductionRun` produces a distinct hash.
5. The existing `--dry-run` flag on `BaseCommandSettings` is currently unused. This spec repurposes it as a CLI override: `--dry-run` forces `ImportType.DryRun` regardless of what is in the config file.

---

## Deferred / Out of Scope

- Cleanup command to delete the `DryRun\` sub-tree from the target after a dry run
- Per-module `ImportType` override (e.g. `DryRun` for `WorkItems` but `ProductionRun` for `Teams`)
- UI/TUI indication of `ImportType` in the job dashboard
- `ImportType` on `Export` — export has no write side-effects on the target system
