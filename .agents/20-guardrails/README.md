# Agent Guardrails

This folder contains mandatory constraints for Agents/AI.

Guardrails define what must be true, what must not change, and what must be rejected.

## Layout

- `core/` - foundational architecture and seam governance
- `domains/` - domain-specific runtime constraints
- `workflow/` - tests-first, completion, and documentation process constraints
  - `workflow/backlog-item-workflow.md`, `workflow/completion-protocol.md`, `workflow/backlog-item-dod.md` - Backlog Item flow (one task = one PR = one worktree, pre-start assessment, completion protocol, item-level DoD; ADR-0034)
  - `workflow/live-test-environment.md` - live E2E organisations, Entra authentication and per-run isolation

Core includes:

- `.agents/20-guardrails/core/architecture-perspectives-ethos.md`

## Index

See `.agents/90-index/guardrails-index.md`.

## Authority

Guardrails have higher authority than context and docs for implementation decisions.


