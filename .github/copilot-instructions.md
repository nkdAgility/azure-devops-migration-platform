# agents.md

# Azure DevOps Migration Platform - Agent Entry Point

## Mission

Build a deterministic, resumable, versioned migration package platform:

**Source -> Files -> Target**

Pipeline:
**Inventory -> Export -> Prepare -> Import -> Validate**

The filesystem package is the source of truth.

---

## The Constitution — ⛔ BLOCKING, always in force

These invariants apply to every change, in every runtime, even if you read
nothing else. Violating one makes the work unacceptable.

1. **Source → Files → Target.** Source and target systems never communicate directly. Export writes the package; import reads it. (ADR-0001/0002)
2. **Only agents write the package.** CLI, TUI, Control Plane, and hosts are read-only toward the package. (ADR-0005)
3. **The Control Plane coordinates; it never executes migration logic** and never caches package data. (ADR-0004)
4. **Stream, never materialize.** Unbounded datasets are processed one item at a time; loading all revisions into memory or global in-memory sorting is forbidden.
5. **Everything resumes.** Progress is cursor-based; re-running any step is safe and skips completed work. (ADR-0003/0010)
6. **One canonical seam per concern.** No parallel runtime entry points; concern engines live once behind the seam; adapters/extensions are thin policy. (ADR-0017)
7. **Failing test first.** RED → GREEN → REFACTOR for every behaviour change; completion claims require fresh full-suite evidence in the response.
8. **Touch = Tag, Touch = Convert.** Every touched test file carries canonical dual `[TestCategory]` tags; behavioural edits to legacy Reqnroll trigger DSL migration.
9. **All agent telemetry flows through the unified worker-event channel** (`POST /workers/{workerId}/events`); deleted per-signal endpoints must not reappear. (ADR-0020)
10. **Three connectors, fully implemented.** Simulated, AzureDevOpsServices, and TeamFoundationServer — no stubs or placeholders; net481 features are implemented, never guarded away. (ADR-0013/0018)
11. **Never run `git commit` or `git push`** unless the operator explicitly asks.
12. **When unsure, stop and ask.** No matching route, conflicting rules, or a Class C change without consent means stop — do not improvise.

Directory-local `AGENTS.md` files inside `src/` and `tests/` add the blocking
rules for that folder. They are authoritative for code in their subtree.

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

If anything conflicts, guardrails win. The Constitution above is a summary of
the highest-severity guardrails, not a replacement for them.

---

<!-- SPECKIT START -->
For additional context about technologies to be used, project structure,
shell commands, and other important information, read the current plan
at `specs/039-team-board-settings/plan.md`.
<!-- SPECKIT END -->
