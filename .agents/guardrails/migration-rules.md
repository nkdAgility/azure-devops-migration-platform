# .agents/guardrails/migration-rules.md

# Migration Rules – Azure DevOps Migration Platform

This file defines enforceable migration behaviour rules and invariants.

All contributors and AI agents must follow these rules.
If code or a proposal conflicts with this file, reject it.

---

# 🎯 Purpose

Ensure migrations are:

- Deterministic
- Resumable
- Portable
- Auditable
- Memory-safe (streaming)

The migration pattern is mandatory:

Source → Files → Target

---

# 🔒 Non-Negotiable Invariants

## 1) No direct Source → Target migration

- The platform MUST NOT migrate by streaming from Source to Target.
- All operations MUST go via the on-disk package.

## 2) WorkItems layout is canonical

WorkItems MUST be stored exactly as:

```

WorkItems/
yyyy-MM-dd/ <ticks>-<workItemId>-<revisionIndex>/
revision.json <attachment files>

````

- This structure MUST NOT be changed.
- Import order MUST be derived from lexicographical enumeration of these paths.

## 3) Attachments are owned by WorkItems

- Attachments MUST be stored beside the revision that introduced them.
- There MUST NOT be a global attachments module or top-level attachments folder.

## 4) Import must be streaming

- Import MUST process one revision folder at a time.
- Import MUST NOT materialise all revisions or work items in memory.
- Import MUST NOT perform global sorting beyond directory enumeration.

## 5) Resume is cursor-based

- Resume MUST be forward-only via a cursor.
- Cursor MUST be stored under `Checkpoints/`.
- Hidden state elsewhere is prohibited.

## 6) Determinism is mandatory

- Re-running Export with the same inputs MUST produce stable ordering and a compatible package layout.
- Re-running Import MUST be idempotent (safe to retry) using checkpoints and mapping.

---

# 📦 Package Layout Rules

Package root MUST contain:

- `manifest.json`
- `WorkItems/`
- `Checkpoints/`
- `Logs/`
- Optional module folders:
  - `Teams/`
  - `Permissions/`
  - `Builds/`
  - `Git/`
  - `Identities/`

Zip packaging MUST preserve the internal structure exactly.

---

# 🧾 Manifest Rules

`manifest.json` MUST include, at minimum:

- packageVersion
- toolVersion
- runId
- configHash
- source metadata (type, org/collection, project, apiVersion if relevant)
- includedTypes
- schemaVersions per included type

Manifest MUST be written before import begins.
Manifest MUST NOT be required for streaming import, but MUST be present for validation and compatibility checks.

---

# 🧠 Checkpointing Rules

## Cursor file

Each module MUST store its cursor in:

- `Checkpoints/<module>.cursor.json`

WorkItems cursor example:

```json
{
  "lastProcessed": "WorkItems/2026-02-25/638760123456789012-12345-17",
  "stage": "Completed",
  "updatedAt": "2026-02-25T18:12:34Z"
}
````

Rules:

* lastProcessed MUST be a relative path inside the package.
* stage MUST be one of:

  * CreatedOrUpdated
  * AppliedFields
  * AppliedLinks
  * UploadedAttachments
  * Completed
* updatedAt MUST be UTC.

Resume semantics:

* On restart, importer MUST skip folders <= lastProcessed (lexicographically).
* If stage is not Completed, importer MUST resume within that folder’s stage safely.

## No per-work-item watermark databases

* SQLite watermark stores are prohibited in the canonical package model.
* Any per-item progress tracking MUST be derived from cursor + id mapping.

---

# 🧩 Module Rules

Each module MUST implement:

* ExportAsync
* ImportAsync
* ValidateAsync
* DependsOn (explicit dependencies)

Modules MUST:

* Write only through IArtefactStore
* Read only through IArtefactStore
* Persist state only through IStateStore
* Declare dependencies explicitly

Modules MUST NOT:

* Access another module’s folder directly
* Persist state outside Checkpoints/
* Perform ad-hoc file IO
* Perform live migrations

---

# 👤 Identity & Mapping Rules

Identity is cross-cutting.

Package MUST include:

Identities/

* descriptors.jsonl
* mapping.json
* unresolved.json

Rules:

* mapping.json is user-editable overrides.
* unresolved.json is produced during validation/import when identities cannot be resolved.
* Modules MUST request identity resolution via the shared identity service, not implement their own rules.

ID mappings MUST be stored under:

Checkpoints/

* idmap.db OR idmap.json

Rules:

* id mapping MUST be deterministic.
* id mapping MUST be used by import for links and references.
* id mapping MUST be append-only or transactionally safe.

---

# 🧰 Source Type Rules

## AzureDevOpsServices (REST)

Export MUST:

* Respect per-module extensions (e.g., WIQL query for WorkItems is at module level; sub-operations are controlled via named extensions).
* Produce package output only.
* Record source metadata in manifest.

Import MUST:

* Consume package only.
* Use mapping and identity services.
* Be resumable via cursor.

## TeamFoundationServer (Legacy)

When source.type == "TeamFoundationServer":

* Export MUST be performed by the `TfsExportCommand` in `DevOpsMigrationPlatform.CLI.Migration`, which uses `ExternalToolRunner` to spawn `DevOpsMigrationPlatform.CLI.TfsMigration` as an isolated subprocess and `TfsExporterProcessAdapter` to translate its stdout into progress events. No other .NET 10 class may contain TFS-specific logic.
* The .NET 10 host MUST NOT link against any .NET Framework assembly.
* Communication with the subprocess MUST follow the process bridge protocol defined in [docs/tfs-exporter.md](../../docs/tfs-exporter.md): stdin JSON, stdout NDJSON progress, stderr errors, cancellation sentinel file, exit code.
* Credentials MUST be passed via stdin JSON — never as command-line arguments.
* The export output MUST be validated after the subprocess exits.
* If the legacy output does not match the canonical package format, a normalisation step MUST convert it.

The external exporter is an extraction backend only.

## Simulated (Testing and Development Only)

When `source.type == "Simulated"` or `target.type == "Simulated"`:

* The simulated source and target are **for testing and development only** — never for production migrations.
* The simulated source MUST implement `IModule` using the same export abstraction as real sources. It MUST NOT bypass module architecture or `IArtefactStore`.
* The simulated source MUST generate work items deterministically from `seed` + `workItemCount`. Same inputs = same package output, byte for byte.
* Simulated work item field values MUST be prefixed with `[SIMULATED]` to prevent confusion with real export packages.
* The simulated target MUST accept all items presented during import without writing to any external system.
* Both simulated source and target MUST emit `ProgressEvent` records through `IProgressSink` at the same granularity as real implementations.
* Identity mapping MUST still run: the simulated source generates a fixed set of synthetic user identities and `IdentitiesModule` processes them in the normal order.
* The `discovery inventory` command with `source.type: Simulated` MUST return counts derived from configuration without any WIQL windowing.



WorkItems export MUST:

* Be scopeable by WIQL.
* Export revisions in increasing revisionIndex order.
* Write each revision folder using:

  * yyyy-MM-dd from revision changed date (UTC)
  * ticks from revision changed time (UTC ticks)
  * workItemId and revisionIndex in folder name

revision.json MUST contain:

* workItemId
* revisionIndex
* changedDate
* fields (delta only — the source API reports only fields changed in this revision; no full-snapshot recomputation is performed)
* links (external/related/hyperlinks)
* attachments metadata (if enabled)
* embeddedImages (array — may be empty; populated when inline images are discovered and downloaded)

Attachments:

* MUST be stored beside revision.json in that revision folder.
* Each attachment entry MUST include:

  * originalName
  * relativePath
  * sha256
  * size

If attachments are disabled by scope, attachments MUST be an empty list or omitted consistently.

Comments:

* MUST be exported per-comment, per-version into individual comment sub-folders inside the date folder corresponding to the comment's `createdDate` (original) or `modifiedDate` (each edit).
* Comment folder naming MUST be `<ticks>-<workItemId>-c<commentId>/` with a `comment.json` inside.
* Comment sub-folders sort chronologically alongside revision sub-folders within the same `WorkItems/yyyy-MM-dd/` date folder.
* Each exported comment MUST be stored by the Comments extension inside `WorkItemsModule` — comments are NOT a separate top-level `IModule`.
* Deleted comments MUST be excluded by default; configurable via `modules.workItems.extensions[Comments].parameters.includeDeleted`.

Embedded Images:

* MUST be downloaded from ADO-hosted URLs found in HTML `<img src>` tags and Markdown `![](url)` patterns in field values and comments.
* MUST be stored **beside their parent document** — inside the revision folder (beside `revision.json`) for revision-field images, and inside the comment folder (beside `comment.json`) for comment images.
* MUST be named by the SHA-256 hash of their content with the extension inferred from the HTTP `Content-Type` response header.
* Non-ADO image URLs MUST NOT be downloaded; original URL MUST be preserved and a warning written to the package log.
* Inaccessible images MUST NOT fail the export; original URL is preserved and a warning is written.
* Embedded image handling MUST be implemented as `IEmbeddedImageExportService` called from within `WorkItemsModule`.

---

# 🧷 WorkItems Import Rules (Staged)

WorkItems import MUST proceed as a staged, idempotent process.

For each revision folder in stream order:

Stage A: CreatedOrUpdated

* Ensure the work item exists on target.
* Record source→target ID mapping if created.

Stage B: AppliedFields

* Apply revision field changes (or apply snapshot if that is your chosen semantics).
* Maintain determinism of update order.

Stage C: AppliedLinks

* Apply links only after IDs exist.
* Use id mapping to translate related work item IDs.

Stage D: UploadedAttachments

* Upload attachment binaries.
* Add attachment relations after upload.
* Verify file hash matches metadata before upload.

Stage Completed

* Update cursor to Completed for that folder.
* Advance lastProcessed.

Rules:

* Stage transitions MUST be persisted to the cursor.
* If a crash occurs mid-folder, resume MUST restart at the correct stage.
* Import MUST NOT assume the ability to replay everything cheaply, it MUST be resumable and incremental.

---

# ✅ Validation Rules

Validation MUST exist as both:

## Pre-flight validation (before import)

* manifest exists and is readable
* required folders exist for selected modules
* cursor files are readable or initialised
* revision.json is valid JSON and schema-compatible
* attachment files exist where referenced
* sha256 and size match recorded metadata
* identity mapping completeness is assessed
* permissions/scopes are validated (where possible)

## Post-flight validation (after import)

* Work item counts match within expected bounds (as configured)
* sample verification of revisions applied
* sample verification of links and attachments
* unresolved identities recorded if any
* failures recorded with enough detail to reproduce

If validation fails:

* Fail-fast unless explicitly configured otherwise.

---

# 🚫 Prohibited Behaviours

Reject any design or implementation that:

* Writes attachments outside the revision folder.
* Introduces a global attachments store/module.
* Stores comments in a flat `<workItemId>-comments.json` file at the date-folder level instead of per-comment sub-folders.
* Stores embedded images in a global shared directory instead of beside their parent document.
* Implements comment export or embedded-image export as a separate top-level `IModule` (these are sub-services of `WorkItemsModule`).
* Loads all WorkItems or all revisions into memory.
* Requires building a full graph before processing.
* Introduces hidden progress state outside Checkpoints/.
* Performs direct source-to-target migration.
* Breaks deterministic folder naming.
* Bypasses identity mapping or id mapping services.

---

# 🔍 Review Checklist

Before accepting a change, verify:

* WorkItems folder structure unchanged.
* Import enumerates lexicographically and streams one folder at a time.
* Cursor checkpoint exists and is updated per folder and per stage.
* Attachments are beside revision.json and hash-validated.
* Links are applied using id mapping.
* No direct source-to-target path exists.
* Legacy TFS export remains external-process only.
* All persistence is via IArtefactStore/IStateStore.
* Determinism and idempotency preserved.
