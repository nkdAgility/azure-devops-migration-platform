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

Source work item IDs do not map 1:1 to target IDs. The ID map tracks the relationship:

```
Checkpoints/
  idmap.db      (PostgreSQL Portable binary in Local/Dedicated Server topology, PostgreSQL Flexible Server in Cloud topologies; preferred for large datasets)
  idmap.json    (fallback for small datasets or tooling compatibility)
```

The ID map is consulted during Stage A (Create) of streaming import. It replaces the old per-work-item watermark model.

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

No module should implement its own identity resolution. All modules consume the `IIdentityMappingService` injected at construction. The `IdentitiesModule` must complete export before any other module begins import.
