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
.github/agents/             ← GitHub custom agents (YAML frontmatter + rules)
.github/skills/             ← session lifecycle skills (start-session, review, end-session, fix)
.github/commands/           ← slash-command aliases invoking skills
agents/                     ← architectural guardrails, coding standards, testing standards
docs/                       ← architecture documentation (source of truth for design)
skills/                     ← reusable instruction bundles for agents
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
- Any agent rule file created under `docs/` — all agent rules live in `agents/`.

## Key References

- Architecture & design decisions: [docs/architecture.md](../docs/architecture.md)
- Hard guardrails (read by all agents): [agents/system-architecture.md](../agents/system-architecture.md)
- ATDD workflow: [agents/atdd-workflow.md](../agents/atdd-workflow.md)
- Testing standards: [agents/testing-standards.md](../agents/testing-standards.md)
```
```