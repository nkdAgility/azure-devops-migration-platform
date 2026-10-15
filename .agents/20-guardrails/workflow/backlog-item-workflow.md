# Backlog Item Workflow — Tasks, Pre-start Assessment and Parents

> **Status:** Ratified by the operator on 2026-10-15 (ADR-0034). Effective once merged to `main`.

This guardrail extends [definition-of-done.md](definition-of-done.md); it does not replace it.

All work is tracked as **Backlog Items**. Questions that need a maintainer ruling are tracked as **Decisions**.

| Gate | When | Defined in |
| --- | --- | --- |
| **Pre-start assessment** | Before any work starts on a Backlog Item | §2 below |
| **L1 Change DoD** | Every code change | [definition-of-done.md](definition-of-done.md) |
| **L2 Completion Protocol** | Every PR or session that claims "done" | [completion-protocol.md](completion-protocol.md) |
| **L3 Backlog Item DoD** | Before a Backlog Item is closed as done | [backlog-item-dod.md](backlog-item-dod.md) |
| **Parent close** | Before closing a broken-down Backlog Item | §2.5 below |

No gate waives another.

---

## 1. One task = one PR = one worktree ⛔ MANDATORY

- **Separate items get separate sessions.** Every Backlog Item is delivered by its own session, in its own fresh git worktree, through its own PR.
  - Do not deliver a second item inside a session or worktree that is already working on another one.
  - If an unrelated problem appears mid-task (for example a broken `main`), raise it as a new Backlog Item and hand it to a new task.
- **The manager session** owns the backlog flow when several items are in progress:
  - It spawns one task per item.
  - It gives each task the full Definition of Done (L1 + L2 + applicable L3).
  - It checks in on every running task **at least every 20 minutes**, so that none stalls silently.
  - It unblocks the task, or surfaces the decision to the operator.
  - It reports an item as done only after verifying that the item's DoD evidence is real.
- A task that is waiting on the operator says so explicitly. It does not idle.

## 2. Pre-start Assessment ⛔ MANDATORY

Assess every Backlog Item before work starts. Record the result as a comment on the issue, using the *Pre-start assessment* section of the template.

### 2.1 Ready?

- [ ] **Outcome:** the issue states the outcome for an operator, and every acceptance criterion is testable.
- [ ] **Open questions:** none remain. Any open question goes into a linked Decision issue, and the item is **Blocked** until that Decision is ruled.
- [ ] **Scope:** the connectors, phases and agent placement (net481 `TfsMigrationAgent` vs net10 `MigrationAgent`) are named, and the azure-devops-migration-tools equivalent is identified where one exists.
- [ ] **Dependencies:** every dependency is closed as completed, **and** its PR is merged into `main`.

If any check fails, the item is **not ready**. Fix the issue text or raise a Decision; do not start.

### 2.2 Right-sized?

An item is the right size only if **all** of these hold:

- [ ] It fits in **one PR** that can meet the full DoD, including the live E2E run.
- [ ] It touches **one concern**: one module, one tool, or one cross-cutting fix.
- [ ] It has **no more than 5 acceptance criteria**.
- [ ] It can be **tested end to end and shipped on its own**, without leaving the product broken or half-wired.
- [ ] The expected effort is **about 2 days or less**.

### 2.3 Outcome and labels

Each verdict replaces the previous verdict label. An item never carries `ready` and `blocked` at the same time.

| Result | Labels after assessment |
| --- | --- |
| Ready and right-sized | add `ready`; remove `needs-assessment` and `blocked` |
| Not ready | add `blocked`; remove `needs-assessment` and `ready`. Link the Decision issue, or say what is missing. |
| Too big | add `broken-down`; remove `needs-assessment`, `ready` and `blocked`. Break it down as described in §2.4. |

### 2.4 Breaking down

1. Split the item into **vertical slices**, each delivering working, testable behaviour.
   - Good split points: by connector, by phase, by data type, or happy path before edge cases.
   - Never split by layer.
2. Create each slice as a Backlog Item and add it as a **sub-issue** of the original (`gh issue create --parent <n>`).
   - Each sub-issue carries over its share of the acceptance criteria, its scope, and the Migration Tools mapping.
3. Check that every acceptance criterion of the original appears in at least one sub-issue. Anything deliberately dropped is listed with a reason.
4. **Keep the original open** as the parent and comment on it with:
   - `Broken down into #a, #b, #c`
   - the coverage mapping from step 3
5. Each sub-issue gets its own pre-start assessment. It may be broken down again.
6. Re-running a breakdown reuses existing sub-issues and linked Decisions; it never duplicates them.

### 2.5 Parent items

- No code work is done against a parent.
- A parent closes **only when all of its sub-issues are closed**. Before closing it:
  - check that every criterion was met by a sub-issue closed as done, or was deliberately dropped;
  - run a final live end-to-end migration of the whole outcome;
  - confirm that the README parity table and module matrix reflect it;
  - record all of this in a closing comment.
- If a sub-issue closes as *not planned*, either cover its criteria in a new sub-issue or record them as dropped on the parent.

## 3. Automation

| Stage | Skill / workflow |
| --- | --- |
| Create items | `nkda-backlog-item-create` |
| Pre-start assessment and breakdown | `nkda-backlog-item-prestart`, `nkda-backlog-groom-workflow` |
| Deliver ready items | `nkda-backlog-deliver-workflow` (one worktree per item, independent verify, auto-merge rule) |
