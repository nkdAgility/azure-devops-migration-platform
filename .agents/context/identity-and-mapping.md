# Identity and Mapping

## 8. Identity & Mapping

Identities are a cross-cutting service. They are not scoped to a single module.

### On-Disk Layout

```
Identities/
  descriptors.jsonl
  mapping.json
  unresolved.json
```

### Files

| File | Description |
|---|---|
| `descriptors.jsonl` | One JSON object per line; each line is a user or group descriptor exported from the source |
| `mapping.json` | Explicit source-to-target identity mappings provided by the operator (overrides automatic resolution) |
| `unresolved.json` | Identities encountered during import that could not be resolved; updated at runtime |

### ID Mapping (Work Item IDs)

Source work item IDs do not map 1:1 to target IDs. The ID map tracks the relationship:

```
Checkpoints/
  idmap.db      (SQLite — package-local indexed storage; source workItemId → target workItemId mapping, attachment records, and revision-level progress)
  idmap.json    (fallback for small datasets or tooling compatibility)
```

The ID map is consulted during Stage A (Create) of streaming import. It replaces the old per-work-item watermark model. The `work_item_map` table also tracks `last_revision_index` per source work item, enabling revision-level skip logic during sync/rerun imports.

### Identity Resolution Rules

1. If an explicit entry exists in `mapping.json`, use it.
2. If an identity can be matched by UPN or display name in the target, use that match.
3. If no match is found, record the identity in `unresolved.json` and proceed (do not fail the import).

### "Identity is a Cross-Cutting Service" Rule

No module should implement its own identity resolution. All modules consume the `IIdentityMappingService` injected at construction. The `IdentitiesModule` must complete export before any other module begins import.
