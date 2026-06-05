# agents.md

# Azure DevOps Migration Platform - Agent Entry Point

## Mission

Build a deterministic, resumable, versioned migration package platform:

**Source -> Files -> Target**

Pipeline:
**Inventory -> Export -> Prepare -> Import -> Validate**

The filesystem package is the source of truth.

---

## Routing Is Contract-Driven

Do not route by intuition.

1. Load `.agents/00-entry/manifest.yaml`.
2. Load `.agents/10-contracts/routing-catalog.yaml`.
3. Classify the task by activity trigger.
4. Inspect matched `first_surfaces` before any cross-domain search.
5. If no activity matches, stop and ask the operator.

Route-first is fail-closed and enforced by:
- `.agents/00-entry/reading-order.md`
- `.agents/00-entry/task-profiles.yaml`
- `.agents/20-guardrails/core/change-governance.md`

---

## Contract and Guardrail Authority

- `/.agents/10-contracts/*` defines canonical contracts and routing.
- `/.agents/20-guardrails/*` enforces behavior.
- `/docs/*` explains architectural intent.

If anything conflicts, guardrails win.

---

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at `specs/038-close-dsl-gaps/plan.md`.
<!-- SPECKIT END -->
