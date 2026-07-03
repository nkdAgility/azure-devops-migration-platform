# Package Format (Summary)

Compressed agent context for the migration package. Canonical layout, scope
semantics, manifest fields, and zip rules:
[`docs/package-format-reference.md`](../../../docs/package-format-reference.md).
Operator guidance: [`docs/package-guide.md`](../../../docs/package-guide.md).
Binding rules: [`package-rules.md`](../../20-guardrails/domains/package-rules.md).

## Core Concepts

- `PackageRoot` is the configured `Package.WorkingDirectory`. Project artefacts live under `<org-folder>/<project>/` inside it — never directly under the root. Org folder name comes from the last URL path segment (`contoso`, `DefaultCollection`).
- Four state scopes: root `.migration/` (package-wide, shared across runs), `/{org}/.migration/` (org resume state), `/{org}/{project}/.migration/` (project resume state), `.migration/runs/<runId>/` (run-scoped audit output only — never source of truth).
- Cursor files are action-qualified: `<action>.<module>.cursor.json` (e.g. `export.workitems.cursor.json`), so inventory/export/import never collide. Read precedence: project → org → package (→ legacy root checkpoints). Writes target the most-specific resolved scope.
- WorkItems layout is canonical and immutable: `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` (revisions) and `<ticks>-<workItemId>-c<commentId>/` (comments). Folder names sort lexicographically in chronological order — this invariant enables streaming import with no global index and trivial resume. Attachments and embedded images live beside the `revision.json`/`comment.json` that references them.
- `manifest.json` (project-scoped) carries `packageVersion`, `toolVersion`, `runId`, `configHash`, `source`, `includedTypes`, `schemaVersions`. Not needed for streaming import; required for validation, compatibility checks, upgrade safety, and zip portability.
- Run logs (`progress.ndjson`, `diagnostics.ndjson`) are append-only NDJSON written via `IPackageAccess.AppendLogAsync`; they are the durable audit record — the Control Plane ring buffer is ephemeral.
