---
name: update-docs
description: After implementation, scans what changed and updates every canonical doc in /docs and /.agents/30-context that describes the affected features, CLI commands, configuration, modules, or architecture. Fails if any doc named in a doc-task in tasks.md is not updated.
---

# Skill: Update Docs After Implementation

Scan the completed implementation and propagate every observable behaviour change into the canonical documentation in `/docs` and `/.agents/30-context`. This skill is a mandatory post-implementation gate — it ensures that documentation remains a first-class engineering asset and never drifts from the code.

**Invocation modes:**

| Mode | How to invoke | Input | Output |
|---|---|---|---|
| **SpecKit hook** | Automatic via `after_implement` in `.specify/extensions.yml` | The feature directory being implemented | Updated `/docs` and `/.agents/30-context` files; updated tasks.md doc-tasks marked `[x]` |
| **Manual (incremental)** | `/update-docs` or `/update-docs --feature <dir>` | Feature directory or current working spec | Same as hook mode — uses `git diff` to scope changes |
| **Reconcile (full audit)** | `/update-docs --reconcile` | Entire solution root | Walks every canonical doc against the actual codebase; no git diff dependency. Use once to bring existing docs up to date. |

---

## Role

When this skill is active, you are a technical writer with full knowledge of the codebase. Your job is to ensure the documentation is an accurate, complete, and up-to-date description of the system — not a historical record of what was planned, but a description of what is actually implemented.

**You MUST NOT:**
- Leave any canonical doc that describes affected behaviour unchanged if the implementation modified that behaviour.
- Add documentation for behaviour that was not implemented (no speculative docs).
- Duplicate content that already exists in another doc (link instead).
- Write prose where a reference table or code example is clearer.

---

## Preconditions

Before executing, read the following to understand what changed:

1. `.agents/00-entry/manifest.yaml`, `.agents/00-entry/task-profiles.yaml`, and `.agents/00-entry/reading-order.md` — decision-system loading contract.
2. `.agents/10-contracts/change-classes.yaml` and `.agents/10-contracts/consent-policy.yaml` — governance for contract/surface-impacting documentation changes.
3. `specs/<feature>/tasks.md` — identify all completed `[x]` tasks and any doc-tasks (tasks whose description references a `/docs` or `/.agents` file)
4. `specs/<feature>/spec.md` — the user-visible feature description and acceptance criteria
5. `specs/<feature>/plan.md` — the technical design, including new types, interfaces, CLI changes, config keys
6. Any `specs/<feature>/contracts/*.md` — interface contracts and schema definitions

Then read the canonical docs that cover the same topic area. Use the mapping table below to identify which docs to read.

---

## Doc Mapping Table

Use this table to decide which docs to open and potentially update based on what the implementation touched:

| If the implementation added or changed… | Read and potentially update… |
|----------------------------------------|------------------------------|
| A CLI command or flag | `docs/cli-guide.md`, `.agents/30-context/domains/cli-commands.md` |
| A configuration key or options class | `docs/configuration-reference.md` |
| A module (`IModule`) | `docs/module-development-guide.md` |
| A job or worker | `docs/agent-hosting.md`, `.agents/30-context/domains/job-lifecycle.md` |
| A connector (Simulated/ADO/TFS) | `docs/capabilities-guide.md` |
| Package layout or artefact paths | `.agents/30-context/domains/migration-package-concept.md` |
| WorkItems folder structure | `.agents/30-context/domains/workitems-format-summary.md` |
| Streaming import behaviour | `.agents/30-context/domains/import-streaming.md` |
| Checkpointing or cursor state | `.agents/30-context/domains/checkpointing-summary.md` |
| `IArtefactStore` or `IStateStore` | `.agents/30-context/domains/package-manager.md` |
| Telemetry, metrics, or OTel spans | `.agents/30-context/domains/telemetry-model.md` |
| Identity mapping service | `.agents/30-context/domains/identity-and-mapping.md` |
| Aspire integration | `docs/development-setup.md` |
| Control plane API | `docs/control-plane.md` |
| Orchestration logic | `docs/migration-process-guide.md` |
| TUI panels | `docs/tui-guide.md` |
| Validation logic | `docs/validation.md` |
| Packaging/zip | `docs/package-format-reference.md` |
| Architecture decisions | `docs/architecture.md` |
| Schema generation | `docs/configuration-reference.md` (schema section) |

---

## Execution Steps

### Step 1 — Build the change inventory

**If invoked in incremental mode (hook or `--feature`):**

Use git to find changed files scoped to the current session:

```
git diff --name-only HEAD
```

Categorise each changed file:
- **Production code** (`.cs` in `src/`) → triggers doc updates based on the mapping table above
- **Test code** (`.cs` in `tests/`) → no doc update required unless a new test fixture or scenario config was added
- **Config/schema** (`.json`, `.yml`, `.props`) → may require `docs/configuration-reference.md` or `.agents/30-context/` update
- **Feature files** (`.feature`) → no doc update required (features are self-documenting)
- **Existing doc files** (`.md` in `docs/`, `.agents/`) → already updated; verify they are accurate

**If invoked in reconcile mode (`--reconcile`):**

Do not use `git diff`. Instead, build the inventory by reading the codebase directly:

1. **Enumerate all modules** — find every class in `src/` implementing `IModule` (grep for `: IModule`). For each, note its name, options class, and which connector(s) it uses.
2. **Enumerate all CLI commands** — find every class with `[Command]` attribute or registered in a `RootCommand`/`CommandApp`. For each, note its verb, all options/arguments and their types and defaults.
3. **Enumerate all options classes** — find every class implementing `IConfigSection` (grep for `: IConfigSection`). For each, note its `SectionName` and all `init`-only properties.
4. **Enumerate all connectors** — find every class in `src/` matching `*Source`, `*Target`, `*Connector` or implementing `ISourceEndpointInfo`/`ITargetEndpointInfo`. Note the `ConnectorType` string.
5. **Enumerate all interfaces in Abstractions** — find every `public interface` in `src/DevOpsMigrationPlatform.Abstractions*/`. Note new ones that may need docs.
6. **Enumerate all telemetry metrics** — find every `IMigrationMetrics` method call site and every `ActivitySource.StartActivity` span name. Note any span names or metric names not present in `.agents/30-context/domains/telemetry-model.md`.

Then use this enumerated inventory as the change set for Steps 2–7, treating every item as "potentially underdocumented" and checking each against the canonical docs.

After the reconcile pass, produce a summary count:
- Items checked
- Docs updated (with file names)
- Items already accurately documented (no change needed)
- Items that could not be auto-documented and require human review

### Step 2 — Identify doc-tasks in tasks.md

Scan `tasks.md` for any task whose description contains:
- A file path under `docs/` or `.agents/`
- The words "Update", "Document", "Add section", "Add docs", "Write docs"

For each doc-task:
- If marked `[x]` → verify the referenced file was actually modified (check `git diff`). If not modified, open the file and verify the content matches the implementation. If it does, the `[x]` is correct. If not, update the doc and leave `[x]`.
- If marked `[ ]` → the doc update is pending. Execute it now and mark `[x]`.

### Step 3 — Apply doc updates

For each doc identified in Step 1 and Step 2:

1. **Read the current doc** from the file system.
2. **Locate or create the section** that describes the affected feature, command, option, or behaviour:
   - If a section already exists → update it in place.
   - If no section exists yet → add a new section at the appropriate location in the doc (e.g. append to a reference table, add a `##` heading after the most closely related existing section).
   - If no existing doc covers the new feature at all → create the section in the most appropriate canonical doc from the mapping table. Only create a **new doc file** if the feature is substantial enough to warrant its own page (e.g. a new top-level subsystem) and no existing doc is a reasonable home for it; in that case also add a link from `docs/architecture.md` or the appropriate parent doc.
3. **Determine the delta**: What does the implementation add, change, or remove compared to what the doc currently says?
4. **Apply the minimum edit** that makes the doc accurate:
   - Add missing CLI flags, options, or commands to reference tables
   - Update configuration key names, types, default values, and section paths
   - Add new module names, options classes, or `SectionName` values to module tables
   - Remove documentation for deleted types, commands, or options
   - Add new interface members, connector registrations, or DI patterns
   - Add new connector types, schema sections, or observability metric names
5. **Do not rewrite prose** that is still accurate — only change what is wrong or missing.
6. **Cross-link** rather than duplicate: if `docs/cli-guide.md` already documents a flag, do not re-document it in `docs/configuration-reference.md` — add a cross-reference.

### Step 4 — Verify `.agents/30-context/domains/cli-commands.md`

This file is the canonical machine-readable command reference. It is more frequently read by agents than `docs/cli-guide.md`.

For every CLI command added or changed in the implementation:
1. Open `.agents/30-context/domains/cli-commands.md`.
2. Check whether an entry for the command exists:
   - **Entry exists** → verify all flags, types, defaults, and description match the implementation. Update stale fields.
   - **Entry missing** → add a full new entry: command name, all flags with types and defaults, description of behaviour, exit codes if non-zero behaviour is defined.
3. If a new subcommand group was added (e.g. a new top-level verb), add the group header and all its commands.
4. Do not leave a new command undocumented here, even if it is already in `docs/cli-guide.md`.

### Step 5 — Verify `docs/configuration-reference.md`

For every options class (`IConfigSection` implementor) added or changed:
1. Open `docs/configuration-reference.md`.
2. Check whether a reference entry for the section exists:
   - **Entry exists** → verify all properties, types, defaults, allowed values, and required/optional status match the implementation.
   - **Entry missing** → add a new entry with: `SectionName` value (the JSON key path), a table of all properties with types, defaults, and allowed values, whether each property is required or optional, and a brief description of what the section controls.
3. If a new registration pattern was introduced (e.g. a new DI extension method or `IConfigSection` base interface), document the pattern in the *Configuration Patterns* section of `docs/configuration-reference.md` (create the section if absent).
4. Do not leave a new options class undocumented here.

### Step 6 — Produce an update report

Output a summary table of every doc that was checked:

```
| Doc file | Status | Change summary |
|----------|--------|----------------|
| docs/cli-guide.md | ✅ Updated | Added --schema-path flag to queue command |
| docs/configuration-reference.md | ✅ Updated | Added SchemaOptionsEntry pattern section |
| .agents/30-context/domains/cli-commands.md | ✅ Updated | Added queue --schema-path entry |
| docs/module-development-guide.md | ✅ No change needed | Module list already accurate |
| docs/architecture.md | ⏭ Skipped | No architectural change detected |
```

Status values:
- **✅ Updated** — doc was modified to reflect the implementation
- **✅ No change needed** — doc was read and verified accurate; no edit required
- **⏭ Skipped** — doc is not affected by this implementation (explain why)
- **❌ Unable to update** — doc requires human judgment (explain the blocker)

### Step 7 — Mark doc-tasks complete

For every doc-task in `tasks.md` that was addressed in Step 2:
- Mark it `[x]` if not already done.

### Step 8 — Final check

Re-read the Mandatory Compliance Review Loop from `agents.md`:

> After completing any unit of work, before marking it done: re-read the relevant docs — check each change against the docs line by line. If any non-compliance is found, fix it immediately and repeat.

Run this loop for every doc updated in this skill execution. A doc update that introduces new inaccuracies is worse than no update.

---

## Guardrails

- **Never add undocumented parameters or commands to docs that do not exist in the code.** Doc accuracy is bidirectional.
- **Never delete documentation for a feature without confirming the feature was removed from the code.**
- **Never update `docs/architecture.md` with implementation details** — architecture docs describe intent and constraints, not code-level specifics.
- **Always use the same terminology** as the existing doc. If the doc says "connector", do not introduce "adapter" or "provider" for the same concept.
- **Data Classification**: Never add example values, project names, org URLs, or attachment paths to docs. Use placeholders like `<project-name>`, `<org-url>`.


