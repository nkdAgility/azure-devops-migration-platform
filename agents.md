# agents.md

# Azure DevOps Migration Platform – Agent Entry Point

This file is the mandatory starting point for any AI agent or contributor.

It connects:

- Human-readable architecture (`/docs`)
- Enforced guardrails (`/agents`)

If anything conflicts:
- `/agents/*.md` guardrails override implementation
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
→ docs/package-format.md

WorkItems layout:
→ docs/workitems-format.md

Streaming import:
→ docs/import-streaming.md

Checkpoint model:
→ docs/checkpointing.md

Module contract:
→ docs/modules.md

Configuration:
→ docs/configuration.md

Artefact store abstraction:
→ docs/artefact-store.md

Job contract:
→ docs/job-contract.md

Control plane:
→ docs/control-plane.md

Migration Agent (worker):
→ docs/migration-agent.md

TUI:
→ docs/tui.md

TFS legacy process bridge:
→ docs/tfs-exporter.md

Aspire orchestration:
→ docs/aspire-integration.md

Validation:
→ docs/validation.md

---

# 🤖 Agent Guardrails (Enforced Rules)

All agent rules live in `/agents`. **Never create a `docs/agent-rules/` directory or any agent rule file under `/docs`.**

These files define what must never be violated:

## Core Architecture Constraints
→ agents/system-architecture.md

## WorkItems-Specific Rules
→ agents/workitems-rules.md

## Migration Behaviour Constraints
→ agents/migration-rules.md

## Aspire Integration Guardrails
→ agents/aspire-integration.md

## Coding Standards
→ agents/coding-standards.md

## New Module Requirements
→ agents/module-template.md

## ATDD Rules & Standards
→ agents/README.md — index of all agent rule files
→ agents/atdd-workflow.md — ATDD session lifecycle and handoff rules
→ agents/acceptance-test-format.md — Gherkin format, naming, prohibited patterns
→ agents/testing-standards.md — Reqnroll + MSTest conventions

## ATDD Agent Profiles (GitHub Custom Agents)
→ .github/agents/specification-agent.agent.md
→ .github/agents/test-generator.agent.md
→ .github/agents/implementer.agent.md
→ .github/agents/reviewer.agent.md
→ .github/agents/orchestrator.agent.md

## Session Lifecycle Skills
→ .github/skills/start-session.md — assemble context and invoke Specification Agent
→ .github/skills/review.md — invoke Reviewer Agent and record verdict
→ .github/skills/end-session.md — verify gates, finalise log, signal commit-ready
→ .github/skills/fix.md — resume a failed or interrupted session

## Session Commands (Slash-command aliases)
→ .github/commands/start-session.md — /start-session
→ .github/commands/review.md — /review
→ .github/commands/end-session.md — /end-session
→ .github/commands/fix.md — /fix

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

Detailed logic is in `/agents`.

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
- Creates agent rule files under `/docs` instead of `/agents`.
---

# 🧭 Development Flow

When implementing:

1. Read relevant `/docs` file.
2. Apply constraints from `/agents`.
3. Implement via module abstraction.
4. Add tests.
5. Update schemas if required.

---

# Final Principle

`/docs` explains the architecture.
`/agents` enforces the architecture.
`agents.md` binds the two.

Preserve:
- Determinism
- Streaming
- Portability
- Clarity