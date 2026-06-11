# Identity and Mapping

## 8. Identity & Mapping

Identities are a cross-cutting service. They are not scoped to a single module.

### On-Disk Layout

```
Identities/
  descriptors.jsonl
  mapping.json
  unresolved.json
  prepare-report.json
```

### Files

| File | Description |
|---|---|
| `descriptors.jsonl` | One JSON object per line; each line is a user or group descriptor exported from the source |
| `mapping.json` | Explicit source-to-target identity mappings provided by the operator (overrides automatic resolution). Never modified by Prepare. |
| `unresolved.json` | Identities encountered during import that could not be resolved; updated at runtime |
| `prepare-report.json` | Written by `IdentitiesModule.PrepareAsync`. Contains auto-matched identities (by UPN/display name) and unresolved identities that require operator attention. Overwritten on each Prepare run. |

### ID Mapping (Work Item IDs)

Source work item IDs do not map 1:1 to target IDs. The ID map is stored as a SQLite database in the package:

```
.migration/Checkpoints/
  idmap.db     ← SQLite database; tables: work_item_map, attachment_map
                 Locked exclusively by the agent via agent.lock during execution.
```

The `work_item_map` table stores `(source_id, target_id, revision_index)` rows. The `attachment_map` table stores `(source_attachment_id, target_attachment_url)` rows. Both tables use `INSERT OR IGNORE` for idempotent writes.

The ID map is consulted during Stage A (`CreatedOrUpdated`) of streaming import. It is seeded at import startup from the target system when the `WorkItemResolutionStrategy` extension is enabled (strategies: `TargetField`, `TargetHyperlink`).

An `agent.lock` file is written beside `idmap.db` while the agent holds the lease. Attempting to run a second agent against the same package will detect the lock and fail fast (concurrent write protection).

### Identity Resolution Rules

1. If an explicit entry exists in `mapping.json`, use it.
2. If an identity can be matched by UPN or display name in the target, use that match.
3. If no match is found, record the identity in `unresolved.json` and proceed (do not fail the import).

### Prepare Phase Identity Handling

During `PrepareAsync`, the `IdentitiesModule`:

1. Reads all entries from `descriptors.jsonl`.
2. Queries the target system for matching identities (by UPN, display name, or email).
3. Writes `prepare-report.json` with:
   - **auto-matched**: identities that were automatically resolved against the target.
   - **unresolved**: identities that could not be matched and require operator attention.
4. Does NOT modify `mapping.json` — that file is operator-owned.
5. The operator reviews `prepare-report.json`, then edits `mapping.json` to add explicit mappings for unresolved identities (or adds skip annotations).
6. Any identity remaining unresolved after operator review is a blocking issue for Import.

### "Identity is a Cross-Cutting Service" Rule

No module should implement its own identity resolution. All modules consume the cross-cutting identity seam injected at construction. The `IdentitiesModule` must complete export before any other module begins import.

### Identity Resolution Architecture (spec 038)

The resolution pipeline is composed of four collaborating abstractions:

| Abstraction | Role | Lives in |
|---|---|---|
| `IIdentityTranslationTool` | **Tool seam** consumed by all modules (`TeamsOrchestrator` via `ITeamExtension` implementations, `RevisionFolderProcessor`, `WorkItemsModule`, `IdentitiesModule`). `Translate(source)` is **synchronous** and reads cached Prepare-phase results only. Exposes `IsEnabled` and `DefaultIdentity`. Configured under `MigrationPlatform:Tools:IdentityTranslation`. (Renamed from the former `IIdentityLookupTool`; `Resolve()` → `Translate()`.) | `Abstractions.Agent/Tools` |
| `IIdentitiesOrchestrator` | Owns the phase lifecycle (`PrepareAsync`/`ExportAsync`/`ImportAsync`/`ValidateAsync`). `PrepareAsync` performs the live matching, caches results (`source → target descriptor`), and writes `prepare-report.json`. `ResolvePrepared(source)` is the synchronous cache read the tool delegates to. | `Abstractions.Agent/Modules` |
| `IIdentityAdapter` | **Connector-specific** query of the live **target** tenant (`FindByUpnAsync`, `FindByDisplayNameAsync`). Called by `PrepareAsync` only. Implementations: `SimulatedIdentityAdapter`, `AzureDevOpsIdentityAdapter` (SDK `IdentityHttpClient`), `TfsIdentityAdapter` (reduced capability — empty + Warning). Dispatched by `CompositeIdentityAdapter` on the target connector type. | `Abstractions.Agent/Identity` (+ connector projects) |
| `IIdentityMatchingStrategy` | **Pure** ordered match step. `UpnIdentityMatchingStrategy` (step 2) then `DisplayNameIdentityMatchingStrategy` (step 3, Unicode-NFC, case-insensitive). Returns `IdentityMatch(Descriptor, MatchCount)`; ambiguity (`MatchCount > 1`) is logged by the orchestrator. | `Abstractions.Agent/Identity` |

**Four-step order** (executed in PrepareAsync for steps 2–3; in the tool for 1 & 4): (1) explicit `mapping.json` override → (2) UPN match → (3) display-name match → (4) configured `DefaultIdentity`, else source pass-through. Ambiguous matches and adapter failures are **not** treated as resolved: PrepareAsync logs a structured warning, records them as **unresolved** in `prepare-report.json`, and they remain subject to the import-blocking rule above (line 59). The step-4 default/pass-through applies only at translation time for identities the operator has not subsequently mapped.

`IIdentityMappingService` remains the override/default resolver the orchestrator consults internally; modules do not call it directly — they go through `IIdentityTranslationTool`.




