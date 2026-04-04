```instructions
# Copilot Instructions

This repository is the **Azure DevOps Migration Platform** — a versioned migration package platform with streaming chronological replay. It is not a live migration tool.

## Technology

- **Language:** C# 10+, .NET 9/10 (production); .NET 4.8 (TFS exporter subprocess only)
- **Test framework:** MSTest + Reqnroll (`Reqnroll.MSTest`)
- **Mocking:** Moq (`MockBehavior.Strict`)
- **CLI:** PowerShell 7+ for scripts and tooling

## Directory Structure

```
src/                        ← production code, one project per layer
tests/                      ← test projects mirroring src structure
features/                   ← Gherkin .feature files by operation/connector/module
.agents/guardrails/         ← architectural guardrails, coding standards, testing standards
.agents/context/            ← canonical reference docs (package format, streaming, checkpointing, etc.)
.agents/skills/             ← session lifecycle skills (start-session, review, end-session, fix)
docs/                       ← architecture documentation (source of truth for design)
.github/agents/             ← GitHub custom agents (YAML frontmatter + rules)
.github/commands/           ← slash-command aliases invoking skills
```

## Non-Negotiable Rules

- The `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` folder layout is canonical. Do not rename, flatten, or reorder it.
- Import must be streaming. Never load all revisions into memory.
- All modules use cursor-based checkpointing under `Checkpoints/`. No watermark tables.
- Attachments are stored beside `revision.json`. There is no global `Attachments/` root.
- No direct source-to-target migration. The package is always the intermediary.
- Modules write only via `IArtefactStore` and `IStateStore`. No direct filesystem access in modules.
- Identity resolution is a shared service (`IIdentityMappingService`). No per-module identity resolution.

## Reject These Patterns

- Any code that loads all work item revisions into a list or array before processing.
- Any code that creates an `Attachments/` root directory at the package level.
- Any code that calls target APIs from within export logic, or source APIs from within import logic.
- Any module that accesses the filesystem directly instead of using `IArtefactStore`.
- Any migration path that skips writing to the package and goes source-to-target directly.
- Any change to the WorkItems folder naming format.
- Any agent rule file created under `docs/` — all agent rules live in `.agents/guardrails/`.

## Key References

**IMPORTANT:** All agents and contributors MUST load both `/.agents/guardrails/` AND
`/.agents/context/` before producing any code, review, or plan output. Skipping
either directory is a constitution violation.

### Guardrails (enforced rules — read ALL files)
- [.agents/guardrails/system-architecture.md](../.agents/guardrails/system-architecture.md)
- [.agents/guardrails/coding-standards.md](../.agents/guardrails/coding-standards.md)
- [.agents/guardrails/atdd-workflow.md](../.agents/guardrails/atdd-workflow.md)
- [.agents/guardrails/testing-standards.md](../.agents/guardrails/testing-standards.md)
- [.agents/guardrails/acceptance-test-format.md](../.agents/guardrails/acceptance-test-format.md)
- [.agents/guardrails/workitems-rules.md](../.agents/guardrails/workitems-rules.md)
- [.agents/guardrails/migration-rules.md](../.agents/guardrails/migration-rules.md)
- [.agents/guardrails/aspire-integration.md](../.agents/guardrails/aspire-integration.md)
- [.agents/guardrails/module-template.md](../.agents/guardrails/module-template.md)

### Context (canonical reference — read relevant files)
- [.agents/context/package-format.md](../.agents/context/package-format.md)
- [.agents/context/workitems-format.md](../.agents/context/workitems-format.md)
- [.agents/context/import-streaming.md](../.agents/context/import-streaming.md)
- [.agents/context/checkpointing.md](../.agents/context/checkpointing.md)
- [.agents/context/artefact-store.md](../.agents/context/artefact-store.md)
- [.agents/context/job-contract.md](../.agents/context/job-contract.md)
- [.agents/context/identity-and-mapping.md](../.agents/context/identity-and-mapping.md)

### Architecture (design intent)
- [docs/architecture.md](../docs/architecture.md)
- [agents.md](../agents.md) — repository entry point (start here)
```
```
