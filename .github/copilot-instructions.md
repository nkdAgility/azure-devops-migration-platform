# Copilot Instructions

This repository is the **Azure DevOps Migration Platform** — a versioned migration package platform with streaming chronological replay. It is not a live migration tool.

## Primary References

- **Architecture & non-negotiables:** [docs/architecture.md](../docs/architecture.md)
- **Hard agent guardrails:** [agents/system-architecture.md](../agents/system-architecture.md)
- **Full documentation:** [docs/](../docs/)

## What This System Does

- Exports Azure DevOps (REST) or TFS (.NET 4 OM subprocess) data to a portable file package.
- Imports that package into Azure DevOps Services using streaming chronological replay.
- Supports Export, Import, and Both modes.

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

## Agent Roles

- **Planner:** [.github/agents/planner.agent.md](agents/planner.agent.md)
- **Implementer:** [.github/agents/implementer.agent.md](agents/implementer.agent.md)
- **Reviewer:** [.github/agents/reviewer.agent.md](agents/reviewer.agent.md)
