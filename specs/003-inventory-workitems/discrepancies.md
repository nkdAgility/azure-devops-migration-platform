# Architecture Discrepancies

**Feature**: Work Items Inventory Command  
**Flagged by**: speckit.specify  
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### configuration.md source section missing authentication fields

- **Source doc**: `docs/configuration-reference.md`
- **Section**: "Full Schema" — `source` block
- **Issue**: The `source` section schema shows only `type`, `orgOrCollection`, `project`, and `apiVersion`. There is no `authentication` block. The spec introduces a required `authentication` sub-object with `type` and `accessToken` (for PAT, where the value is a literal or a `$ENV:VARNAME` prefixed string) or `type: Windows` (for TFS integrated auth). Without this, operators have no documented way to provide credentials in the config file and would be forced to use CLI arguments, which violates the coding standards.
- **Suggested update**: Add an `authentication` block to the `source` (and symmetrically `target`) example in `docs/configuration-reference.md`, documenting the `Pat` and `Windows` types. Add a description row to the Top-Level Fields table clarifying that credentials MUST be in the config file, not CLI arguments. Also document the three-layer token resolution order: IConfiguration `__`-separator env var override → `$ENV:VARNAME` prefix → literal value.

---

### configuration.md missing `organisations` top-level key

- **Source doc**: `docs/configuration-reference.md`
- **Section**: "Full Schema" and "Top-Level Fields" table
- **Issue**: The spec introduces a second config mode (`organisations` array) for multi-org, multi-project tooling operations. This key does not appear anywhere in `docs/configuration-reference.md`. The mutual exclusion rule (cannot have both `organisations` and `source`) is also undocumented.
- **Suggested update**: Add an `organisations` section to the Full Schema example and the Top-Level Fields table. Add a note explaining the two mutually exclusive config modes and the validation rules that govern them. Document the `organisations` entry fields (`type`, `url`, `projects`, `apiVersion`, `authentication`, `enabled`).

---

### inventory command missing from CLI docs

- **Source doc**: `docs/cli-guide.md`
- **Section**: "Commands" table
- **Issue**: The `devopsmigration discovery inventory` command does not appear in the CLI commands table. The spec introduces this as a new operator-facing command under the existing `discovery` branch. The command currently inventories work items; it is designed to expand to repos and pipelines in future iterations.
- **Suggested update**: Add a row `| discovery inventory | Count all work items (and in future, repos and pipelines) per project from the configured source. Read-only pre-flight operation. |` to the Commands table. Also add a usage example: `devopsmigration discovery inventory --config migration.json`.

---

### inventory mode not described in source-types docs

- **Source doc**: `docs/capabilities-guide.md`
- **Section**: Entire document (covers AzureDevOpsServices and TeamFoundationServer in export context only)
- **Issue**: `source-types.md` describes how each source type is used during export. It does not mention inventory. The spec requires both source types to support the inventory operation.
- **Suggested update**: Add an "Inventory" subsection under each source type describing the counting strategy and authentication requirements for that source.

---

### TFS subprocess inventory capability not documented

- **Source doc**: `docs/tfs-exporter.md`
- **Section**: Purpose / subprocess architecture
- **Issue**: The TFS subprocess (`DevOpsMigrationPlatform.CLI.TfsMigration`) is only documented as an export agent. The spec adds an inventory subcommand to that binary. The isolation model, NDJSON protocol, and `ExternalToolRunner` bridge are reused but the inventory capability is not mentioned.
- **Suggested update**: Add an "Inventory Mode" section to `tfs-exporter.md` describing that the subprocess accepts an `inventory` command, performs date-windowed work item counting with progressive half-window fallback, and emits `InventoryProgressEvent` records as NDJSON on stdout.

---

### source-types.md references .NET 9 (pre-existing typo)

- **Source doc**: `docs/capabilities-guide.md`
- **Section**: AzureDevOpsServices requirements
- **Issue**: The doc states "Uses the Azure DevOps REST API natively from .NET 9." The platform targets .NET 10.
- **Suggested update**: Replace ".NET 9" with ".NET 10".

- **Source doc**: `docs/cli-guide.md`
- **Section**: "Architecture" — the CLI delegates to control plane via HTTP
- **Issue**: The CLI architecture doc emphasises that all commands delegate execution to the control plane. `devopsmigration discovery inventory` is a read-only pre-flight command that does NOT submit a job to the control plane. This distinction needs to be captured. Note: `prepare` does interact with the control plane (it computes `configHash` and validates against it), so it must NOT be grouped with inventory in this note.
- **Suggested update**: Add a note in the CLI architecture section clarifying that read-only pre-flight discovery commands (`discovery inventory` and future `discovery *` commands) operate without submitting a `MigrationJob`; they read from the source directly (or via subprocess for TFS) and produce only console output or local files.
