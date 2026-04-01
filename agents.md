# agents.md

# Azure DevOps Migration Platform – Agent Entry Point

This file is the mandatory starting point for any AI agent or contributor.

It connects:

- Human-readable architecture (`/docs`)
- Enforced guardrails (`/.agents/guardrails`)

If anything conflicts:
- `/.agents/guardrails/*.md` guardrails override implementation
- `/docs/*.md` define architectural intent

---

# 🎯 Mission

Build a deterministic, resumable, versioned migration package platform:

Source → Files → Target

Modes:
- Export
- Import
- Both

The filesystem package is the source of truth.

See:
→ docs/architecture.md

---

# 📚 Canonical Specification (Read First)

Architecture:
→ docs/architecture.md

Package layout:
→ .agents/context/package-format.md

WorkItems layout:
→ .agents/context/workitems-format.md

Streaming import:
→ .agents/context/import-streaming.md

Checkpoint model:
→ .agents/context/checkpointing.md

Module contract:
→ docs/modules.md

Configuration:
→ docs/configuration.md

Artefact store abstraction:
→ .agents/context/artefact-store.md

Job contract:
→ .agents/context/job-contract.md

Control plane:
→ docs/control-plane.md

Orchestration:
→ docs/orchestration.md

Migration Agent (worker):
→ docs/migration-agent.md

Module contract:
→ docs/modules.md

TUI:
→ docs/tui.md

CLI:
→ docs/cli.md

TFS legacy process bridge:
→ docs/tfs-exporter.md

Aspire orchestration:
→ docs/aspire-integration.md

Validation:
→ docs/validation.md

Identity and mapping:
→ .agents/context/identity-and-mapping.md

Configuration reference:
→ docs/configuration.md

Source types:
→ docs/source-types.md

Package zip/export:
→ docs/packaging-zip.md

---

# 🤖 Agent Guardrails (Enforced Rules)

All agent rules live in `/.agents/guardrails`. **Never create a `docs/agent-rules/` directory or any agent rule file under `/docs`.**

These files define what must never be violated:

## Core Architecture Constraints
→ .agents/guardrails/system-architecture.md

## WorkItems-Specific Rules
→ .agents/guardrails/workitems-rules.md

## Migration Behaviour Constraints
→ .agents/guardrails/migration-rules.md

## Aspire Integration Guardrails
→ .agents/guardrails/aspire-integration.md

## Coding Standards
→ .agents/guardrails/coding-standards.md

## New Module Requirements
→ .agents/guardrails/module-template.md

## ATDD Rules & Standards
→ .agents/guardrails/atdd-workflow.md — ATDD session lifecycle and handoff rules
→ .agents/guardrails/acceptance-test-format.md — Gherkin format, naming, prohibited patterns
→ .agents/guardrails/testing-standards.md — Reqnroll + MSTest conventions

## ATDD Agent Profiles (GitHub Custom Agents)
→ .github/agents/specification-agent.agent.md
→ .github/agents/test-generator.agent.md
→ .github/agents/implementer.agent.md
→ .github/agents/reviewer.agent.md
→ .github/agents/orchestrator.agent.md

## Session Lifecycle Skills
→ .github/skills/start-session/SKILL.md — assemble context and invoke Specification Agent
→ .github/skills/review/SKILL.md — invoke Reviewer Agent and record verdict
→ .github/skills/end-session/SKILL.md — verify gates, finalise log, signal commit-ready
→ .github/skills/fix/SKILL.md — resume a failed or interrupted session
→ .github/skills/parse-criteria/SKILL.md — parse Gherkin feature files into a structured test plan
→ .github/skills/test-templates/SKILL.md — generate Reqnroll step definition files from a test plan
→ .github/skills/session-hooks/SKILL.md — manage session lifecycle events and phase transitions
→ .github/skills/refactor-patterns/SKILL.md — assess code quality and apply safe refactoring patterns

## Session Commands (Slash-command aliases)
→ .github/commands/start-session.md — /start-session
→ .github/commands/review.md — /review
→ .github/commands/end-session.md — /end-session
→ .github/commands/fix.md — /fix

## Acceptance Test Feature Files
All Gherkin `.feature` files live under `/features`, organised by operation and module:

```
features/
  cli/            ← CLI-triggered operations (export, inventory, …)
  export/         ← Export module features
  import/         ← Import module features
  inventory/      ← Inventory module features
  platform/       ← Cross-cutting platform concerns (checkpointing, validation)
  services/       ← Shared services (identity-mapping, …)
```

If code conflicts with these, reject the change.

---

# 🔒 Non-Negotiable Summary

1. WorkItems layout is canonical and chronological.
2. Import must be streaming and memory-safe.
3. Resume must use cursor-based checkpointing.
4. Attachments must live beside revision.json.
5. No direct Source → Target migration.
6. Modules must be isolated.
7. All persistence goes through IArtefactStore and IStateStore.
8. Determinism is mandatory.

Detailed logic is in `/.agents/guardrails`.

---

# 🚨 Reject Conditions

Reject any proposal that:

- Breaks chronological folder ordering.
- Introduces global attachment storage.
- Requires loading all revisions into memory.
- Adds hidden state outside `Checkpoints/`.
- Couples modules directly.
- Performs live streaming migration.
- Violates coding standards.
- Adds migration execution logic to the control plane.
- References a concrete artefact store implementation inside module code.
- Sorts `EnumerateAsync` results in memory.
- Creates agent rule files under `/docs` instead of `/.agents/guardrails`.
---

# 🧭 Development Flow

When implementing:

1. Read relevant `/docs` file.
2. Apply constraints from `/.agents/guardrails`.
3. Implement via module abstraction.
4. Add tests.
5. Update schemas if required.

---

# Final Principle

`/docs` explains the architecture.
`/.agents/guardrails` enforces the architecture.
`agents.md` binds the two.

Preserve:
- Determinism
- Streaming
- Portability
- Clarity