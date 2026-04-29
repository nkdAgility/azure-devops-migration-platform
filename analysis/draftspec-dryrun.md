# Draft Specification: ExecutionMode — DryRun / Live

**Status**: Draft — open questions below must be resolved before promotion to `specs/`  
**Created**: 2026-04-29  
**Author**: Initial draft from design discussion

---

## Summary

Add a first-class `ExecutionMode` concept to the platform with two values: `DryRun` (default) and `Live`.

`ExecutionMode` is a **safety guardrail**, not an operator tool. It is set in the config file but its enforcement is performed by the platform at import time — the operator cannot suppress or bypass the effects by overriding `NodeStructure` mappings or `FieldTransform` rules. It makes the system **safe by default**: if an operator forgets to set it or creates a new config file, the import cannot corrupt the live project tree.

### DryRun effects (applied automatically by the platform at import time)

| What | How |
|---|---|
| All work items are placed under a root node `DryRun` in the area/iteration tree | Platform injects a terminal `DryRun\` prefix step **after** all operator `NodeStructure` mappings resolve |
| All work item titles are prefixed with `[DryRun] ` | Platform injects a terminal `PrependString` transform on `System.Title` **after** all operator `FieldTransform` groups complete |
| The `DryRun` root node is auto-created if it does not already exist | Platform forces `AutoCreateNodes` equivalent for this single node |

### Live effects

No platform-injected changes. The operator's config runs as-is.

---

## Scope

Affects:

- `MigrationOptions` (config model — new `ExecutionMode` property)
- `MigrationJob` (job contract — carries `ExecutionMode` to the agent; operator cannot modify on the agent side)
- `WorkItemsModule.ImportAsync` (enforces title prefix)
- `NodesModule.ImportAsync` and `NodeEnsurer` (enforces path prefix and `DryRun` node creation)
- `TeamsModule.ImportAsync` — **open question Q3**
- `QueueCommand` (copies `ExecutionMode` from config to job contract)
- `MigrationOptionsValidator` (emits structured `Warning` when `Live` is set)
- `docs/configuration.md` (new top-level field documentation)
- `.agents/context/job-contract.md` (new contract field)

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
    "ExecutionMode": "DryRun",
    "Mode": "Import",
    ...
  }
}
```

| Field | Type | Default | Description |
|---|---|---|---|
| `ExecutionMode` | `string` | `"DryRun"` | `DryRun` or `Live`. Omitting the field is equivalent to `DryRun`. |

---

## Implementation Design

### 1. New enum in `DevOpsMigrationPlatform.Abstractions`

```csharp
// Abstractions/Options/ExecutionMode.cs
public enum ExecutionMode
{
    DryRun,
    Live
}
```

### 2. `MigrationOptions` — add property

```csharp
// Default to DryRun (safe by default)
public ExecutionMode ExecutionMode { get; set; } = ExecutionMode.DryRun;
```

### 3. `MigrationJob` — add property (carries to agent; init-only)

```csharp
// Abstractions/Jobs/MigrationJob.cs
public ExecutionMode ExecutionMode { get; init; } = ExecutionMode.DryRun;
```

### 4. `QueueCommand.BuildModules` / job construction

Copy `config.ExecutionMode` → `job.ExecutionMode` when constructing the `MigrationJob`. The CLI does not enforce effects — it is a transparent pass-through.

### 5. Node path enforcement (`NodesModule` / `NodeEnsurer`)

When `job.ExecutionMode == DryRun`:

- Inject a post-processing step in `NodeEnsurer.EnsureReferencedPathsAsync` and `NodeEnsurer.ReplicateSourceTreeAsync` that wraps every target path as `DryRun\{translatedPath}`.
- The `DryRun` root node itself must be created if absent (idempotent call to `INodeCreator.EnsureExistsAsync`).
- This wrapping happens **after** `INodeStructureTool.TranslatePath` has already been called — the operator mappings run first, then the platform appends the `DryRun\` prefix to the result.
- Log at `Information` level: `[DryRun] Wrapping target path '{original}' → '{dryRunPath}'`.

### 6. Work item title enforcement (`WorkItemsModule` / `RevisionFolderProcessor`)

When `job.ExecutionMode == DryRun`:

- After the operator `FieldTransformTool.ApplyTransforms` result is obtained, apply a platform-owned `PrependStringTransform("[DryRun] ", "System.Title")` to the output.
- `PrependStringTransform` is a new, simple internal transform (not exposed to operator configuration) that prepends a literal string to a string field. It is idempotent: if the field already starts with the prefix it is not applied again.
- Log at `Debug` level per work item: `[DryRun] Prepended title prefix for WI {id}`.

**Idempotency requirement**: If the operator re-runs in `DryRun` mode, titles must not accumulate `[DryRun][DryRun]` prefixes. The transform must check before prepending.

### 7. Validator warning

`MigrationOptionsValidator` emits a structured `Warning` log when `ExecutionMode == Live` (never an error — `Live` is a valid choice):

```
WARN [ExecutionMode] ExecutionMode is 'Live'. Work items will be imported without the DryRun prefix and under the real area/iteration tree. Ensure you have reviewed the prepared mapping reports before proceeding.
```

### 8. Observability

| Metric / Span | Where | Description |
|---|---|---|
| `migration.execution_mode` tag | All import spans | Low-cardinality tag: `dry_run` or `live` |
| Structured log at `Information` on import start | `WorkItemsModule.ImportAsync` | `[ExecutionMode] Running import in {mode} mode.` |
| Structured log at `Warning` when `Live` | `MigrationOptionsValidator` | See §7 above |

---

## Open Questions

The following questions must be answered before this spec can be promoted and implementation can begin.

---

### Q1 — Title prefix string: is `[DryRun]` the right choice?

**Context**: The feature description states "prefix all work item titles with `[DryRun]`". The prefix needs to be:
- Visually obvious in Azure DevOps work item lists
- Easy to find/filter (e.g. via a WIQL `WHERE System.Title LIKE '[DryRun]%'`)
- Non-ambiguous (not a character that ADO strips or escapes)

**Options**:
- A: `[DryRun] ` (brackets, space after — as described)
- B: `[DRY RUN] `
- C: `🧪 ` (emoji — may not render everywhere or sort predictably)
- D: Make it configurable — operator can set `DryRunTitlePrefix` — but this partially undermines the "not operator-controllable" intent

**Decision needed**: Confirm A is correct. Is the prefix configurable or fixed?

---

### Q2 — DryRun root node name: is `DryRun` the right string?

**Context**: The feature description states "add root node of `DryRun`". This creates an area/iteration tree node like `MyProject\DryRun\TeamA\Sprint 1`.

**Sub-questions**:
- 2a: Is the root node name `DryRun` fixed, or should it be configurable (e.g. `DryRunRootNodeName`)? Making it configurable partially undermines the guardrail intent.
- 2b: Should the `DryRun` node be created under the **project root** (i.e. `TargetProject\DryRun\...`) or as a peer of the translated project node?
- 2c: After a DryRun import, should there be a way to delete the `DryRun` sub-tree cleanly? (Out of scope for this spec, but worth noting as a follow-up.)

---

### Q3 — Does `ExecutionMode` affect `TeamsModule` import?

**Context**: `TeamsModule` creates teams and assigns area/iteration paths to them. In `DryRun` mode, the area paths these teams reference will be `DryRun\...` paths.

**Options**:
- A: Yes — `TeamsModule` must also use `DryRun\` prefixed paths for its area/iteration assignments, so teams are created under the `DryRun` sub-tree. **This is likely correct** — otherwise team area assignments will point to non-existent real nodes.
- B: No — teams are created normally with their real path assignments. `DryRun` is work-item-only.
- C: In `DryRun` mode, `TeamsModule` is entirely skipped.

**Decision needed**.

---

### Q4 — Does `ExecutionMode` affect `IdentitiesModule` or `NodesModule` (export/replication)?

**Context**: `NodesModule.ExportAsync` reads the source tree and writes `Nodes/source-tree.json`. `IdentitiesModule.ExportAsync` reads identities. Neither of these writes to the target — they are package-side artefacts.

**Expected answer**: `ExecutionMode` is import-only. Export and package-side operations are unaffected. Confirm?

---

### Q5 — DryRun mode for `Prepare` (not just `Import`)?

**Context**: The `Prepare` mode connects to the target and validates field definitions, node existence, etc. If run in `DryRun` mode, should the prepare validation check whether the `DryRun\...` node paths are resolvable/creatable, rather than the literal translated paths?

**Options**:
- A: `Prepare` is unaffected — it validates the operator's translated paths. The `DryRun\` prefix is an import-time concern only.
- B: `Prepare` should simulate the full `DryRun` path prefixing so the validation report reflects what import will actually create.

**Decision needed**.

---

### Q6 — Config validation: should `DryRun` mode block `Import` unless explicitly acknowledged?

**Context**: The intent is "DryRun is the default — safe by default". The question is whether the platform should require an explicit acknowledgement when moving from `DryRun` to `Live`, beyond just the log warning.

**Options**:
- A: Log warning only (as in §7 above). No blocking.
- B: Require `--confirm-live` CLI flag when `ExecutionMode: Live` to prevent accidental `Live` imports from a config that had `Live` set but was run carelessly.
- C: Require an explicit `"ExecutionModeConfirmed": true` field in the config alongside `"ExecutionMode": "Live"` — validation fails if `Live` is set without this companion field.

**Decision needed**.

---

### Q7 — Idempotent re-run in DryRun mode: duplicate prefix prevention

**Context**: If the same work item is imported twice in `DryRun` mode (e.g. after a `--force-fresh`), the title in the target already has `[DryRun] `. The resolution strategy (`TargetField` / `TargetHyperlink`) would find the existing item and update it. On update, the `PrependStringTransform` would need to detect the existing prefix.

**Clarification needed**: The platform must check whether the current **target** title (i.e. the value being written in the revision) already starts with `[DryRun] ` before prepending. This is the revision value from `revision.json`, not the target state — the revision is re-applied each time. Confirm the check is on the revision value, not the live target state.

---

## Assumptions (to be validated)

1. `ExecutionMode` applies only when `Mode` is `Import` or `Migrate` (import leg). Export-only runs ignore it entirely.
2. The `DryRun` root node is created at the project root of the **target** project, not the source project name.
3. `PrependStringTransform` is internal — not registered in `FieldTransformFactory`, not documented in the `FieldTransform` configuration schema, not visible to operators.
4. The `MigrationJob.ExecutionMode` field is included in the `configHash` computation so a change from `DryRun` to `Live` produces a distinct hash.
5. The existing `--dry-run` flag on `BaseCommandSettings` is currently unused. This spec repurposes it as a CLI override: `--dry-run` forces `ExecutionMode.DryRun` regardless of what is in the config file. A `--live` flag does not exist — to run `Live` you must set it in the config file explicitly.

---

## Deferred / Out of Scope

- Cleanup command to delete the `DryRun\` sub-tree from the target after a dry run
- Per-module `ExecutionMode` override (e.g. `DryRun` for `WorkItems` but `Live` for `Teams`)
- UI/TUI indication of `ExecutionMode` in the job dashboard
- `ExecutionMode` on `Export` — export has no write side-effects on the target system
