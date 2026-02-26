```instructions
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

### ATDD Pipeline Agents
Agents are sequenced by the Orchestrator. One acceptance scenario per session.

```
Specification Agent → Test Generation Agent → Implementation Agent → Reviewer Agent
        ↑                                                                    |
        └──────────────── Orchestrator manages handoffs ────────────────────┘
```

- **Specification Agent:** [.github/agents/specification-agent.agent.md](agents/specification-agent.agent.md) — converts user stories to Gherkin `.feature` files
- **Test Generation Agent:** [.github/agents/test-generator.agent.md](agents/test-generator.agent.md) — produces failing Reqnroll step definitions (red stage)
- **Implementation Agent:** [.github/agents/implementer.agent.md](agents/implementer.agent.md) — writes production code to pass the tests
- **Reviewer Agent:** [.github/agents/reviewer.agent.md](agents/reviewer.agent.md) — verifies guardrail compliance and approves or rejects
- **Orchestrator:** [.github/agents/orchestrator.agent.md](agents/orchestrator.agent.md) — manages session lifecycle and enforces one-scenario-per-session discipline

## ATDD Infrastructure

- **Acceptance tests:** [tests/acceptance/](../tests/acceptance/) — Gherkin `.feature` files by functional area
- **Agent-rules:** [docs/agent-rules/](../docs/agent-rules/) — testing standards, acceptance test format, ATDD workflow
- **Skills:** [skills/](../skills/) — reusable instruction bundles loaded by agents
  - `skills/parse-criteria/` — parse Gherkin and produce structured test plans
  - `skills/test-templates/` — Reqnroll step definition and context templates
  - `skills/refactor-patterns/` — code quality and refactoring patterns (green → refactor stage)
  - `skills/session-hooks/` — session lifecycle logging and CI gate signals

## ATDD Workflow (One Scenario Per Session)

See [docs/agent-rules/atdd-workflow.md](../docs/agent-rules/atdd-workflow.md) for the full session discipline.
```