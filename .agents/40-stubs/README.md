# Nested Agent Stubs

Each `*.md` file here is a directory-local `AGENTS.md` that `.agents/configure.ps1`
hardlinks into one or more `src/` or `tests/` directories. AI harnesses that pick
up nested `AGENTS.md` files (Codex, Claude Code, Cursor, and others) see these
rules at the moment they edit code in that subtree — no routing protocol required.

Rules for stubs:

- **Blocking rules only.** A stub carries the ⛔ rules for its subtree plus pointers. Explanations live in `/docs`; the full rule set lives in `.agents/20-guardrails`.
- **Repo-root paths only.** Stubs are hardlinked into multiple locations, so never use relative markdown links — write plain `` `.agents/...` `` / `` `docs/...` `` paths.
- **Keep them under ~25 lines.** These live inside every agent session that touches the folder.
- **Adding/removing a stub** requires updating the `$stubs` map in `.agents/configure.ps1` in the same change, then running the script.

Edit stubs here (or at their linked location — hardlinks share content on a
configured machine). Git tracks every linked path, so fresh clones have the
stubs even before `configure.ps1` runs; run the script to restore local link
sync after tools rewrite files.

## Exempt projects (no stub, by decision)

These `src/` projects intentionally carry no `AGENTS.md`. Every project must
either appear in the `$stubs` map or in this list — an unlisted, uncovered
project is a gap to fix.

| Project | Rationale |
| --- | --- |
| `DevOpsMigrationPlatform.AppHost` | Aspire dev-host wiring only; no domain rules beyond the constitution. |
| `DevOpsMigrationPlatform.Infrastructure` | Shared base infrastructure; governed by the core guardrails loaded via routing, no subtree-specific blocking rules. |
| `DevOpsMigrationPlatform.Infrastructure.ControlPlane` | Covered by the control-plane rules; the `controlplane.md` stub sits on the ControlPlane/Host projects where edits happen. |
| `DevOpsMigrationPlatform.SchemaGenerator` | Build-time tool; its one blocking rule (references all three connectors) is CI-enforced in `main.yml`. |
| `DevOpsMigrationPlatform.ServiceDefaults` | Aspire service-defaults boilerplate; no migration logic permitted or present. |
