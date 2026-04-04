# Architecture Discrepancies

**Feature**: Inventory Command — Config-Driven, Multi-Source, Paginated  
**Flagged by**: speckit.specify  
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### `inventory` command missing from CLI command table

- **Source doc**: `docs/cli.md`
- **Section**: Commands table (listing `prepare`, `export`, `import`, etc.)
- **Issue**: The `inventory` command under `discovery` is not listed in the command table. The spec introduces `migrate discovery inventory` as a first-class command.
- **Suggested update**: Add a row to the Commands table: `| \`discovery inventory\` | Discover all projects and count work items across one or more organisations or collections. |`

---

### `inventory` section missing from config schema

- **Source doc**: `docs/configuration.md`
- **Section**: Full Schema JSON block and Top-Level Fields table
- **Issue**: The config schema has no `inventory` key. The spec adds an `inventory` section with a `sources` array (each entry having `type`, `orgOrCollection`, optional `project`, and `token`).
- **Suggested update**: Add the `inventory` key to the Full Schema example and a row to the Top-Level Fields table: `| \`inventory\` | No | Inventory source connections used by \`discovery inventory\`. Required when running the inventory command. |`

---

### Config version not incremented to reflect new `inventory` section

- **Source doc**: `docs/configuration.md`
- **Section**: Config Versioning and Upgrader
- **Issue**: The documented schema version is `1.0`. Adding the `inventory` section is a breaking schema change (new required section for inventory commands) and requires a version increment with an upgrader.
- **Suggested update**: Document that `configVersion` advances to `2.0` when the `inventory` section is introduced, and that an upgrader exists for the `1.0 → 2.0` transition that treats a missing `inventory` section as a valid `1.0` config (no-op upgrade for migration-only configs).

---

### Token resolver utility not described anywhere

- **Source doc**: `docs/configuration.md` and `.agents/guardrails/coding-standards.md`
- **Section**: No existing section covers `$ENV:VARNAME` token resolution
- **Issue**: The spec introduces a shared `$ENV:VARNAME` syntax for token fields, to be implemented as a reusable utility. Neither the configuration docs nor the coding standards document this pattern.
- **Suggested update**: Add a "Token Resolution" subsection to `docs/configuration.md` describing the `$ENV:VARNAME` syntax, the resolver behaviour (pass-through for non-`$ENV:` values, fail-fast for missing variables), and the requirement that this utility be shared across all commands that accept a `token` field.

---

### `inventory` command not documented in CLI example usage block

- **Source doc**: `docs/cli.md`
- **Section**: CLI example invocations block at the end of the Commands section
- **Issue**: No example invocation exists for the inventory command.
- **Suggested update**: Add: `migrate discovery inventory --config migration.json` and `migrate discovery inventory --config migration.json --project MyProject --out summary.csv`

---

### `ExternalToolRunner` lacks stdin support needed for TFS inventory subprocess

- **Source doc**: `docs/tfs-exporter.md` / `.agents/guardrails/coding-standards.md`
- **Section**: TFS process bridge protocol — credentials via stdin JSON only
- **Issue**: `ExternalToolRunner.RunWithStreamingAsync` (the existing method) has no parameter for stdin content. The TFS inventory path must pass a `TfsInventoryRequest` JSON payload via subprocess stdin (credentials must never appear in CLI args per rule 19 and coding-standards). A new `RunWithStdinAsync` overload is required.
- **Plan action**: Add `RunWithStdinAsync(exePath, arguments, stdinContent, onOutput, onError, ct)` overload to `ExternalToolRunner.cs`. This overload is generic and TFS-agnostic. The existing `RunWithStreamingAsync` method is not modified (zero breaking-change risk for existing callers). Tracked in `plan.md` Design Decisions section 2.
