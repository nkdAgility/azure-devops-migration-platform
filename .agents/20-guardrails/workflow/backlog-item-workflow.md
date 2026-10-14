# Backlog Item Workflow — Pre-start Assessment and Definition of Done

> **Status: DRAFT, pending operator approval. This is a Class C governance change.**

This document extends [definition-of-done.md](definition-of-done.md); it does not replace it.

All work is tracked as **Backlog Items**. Questions that need a maintainer ruling are tracked as **Decisions**.

| Gate | When | Defined in |
| --- | --- | --- |
| **Pre-start assessment** | Before any work starts on a Backlog Item | §0 below |
| **L1 Change DoD** | Every code change | [definition-of-done.md](definition-of-done.md) §1–§9 |
| **L2 Completion Protocol** | Every PR or agent session that claims "done" | §A and §B below |
| **L3 Backlog Item DoD** | Before a Backlog Item is closed as done | §C below |
| **Parent close** | Before closing a Backlog Item that was broken down | §0.5 below |

No gate waives another. A Backlog Item is closed as done only when its PR(s) met L1 and L2 and the item meets every L3 criterion that applies to it. A broken-down parent closes only when all of its sub-issues are closed (§0.5).

---

## 0. Pre-start Assessment ⛔ MANDATORY

Assess every Backlog Item before work starts. Record the result as a comment on the issue, using the *Pre-start assessment* section of the template.

### 0.1 Ready?

- [ ] **Outcome:** the issue states the outcome for an operator, and every acceptance criterion is testable.
- [ ] **Open questions:** none. Any open question goes into a linked Decision issue, and the item is **Blocked** until that Decision is ruled.
- [ ] **Scope:**
  - The connectors and phases in scope are named.
  - The azure-devops-migration-tools equivalent is identified, where one exists.
- [ ] **Dependencies:** any dependency on another item is linked, and that item is done.

If any of these fail, the item is **not ready**. Fix the issue text or raise a Decision; do not start.

### 0.2 Right-sized?

An item is a reasonable size only if **all** of these hold:

- [ ] It fits in **one PR** that can meet the full L1 + L2 + applicable L3 DoD, including the live E2E run.
- [ ] It touches **one concern**: one module, one tool, or one cross-cutting fix.
- [ ] It has **no more than 5 acceptance criteria**.
- [ ] It can be **independently tested end to end** and **independently shipped** without leaving the product broken or half-wired.
- [ ] Expected effort is **no more than about 2 days** of focused work.

### 0.3 Outcome

| Result | Action |
| --- | --- |
| Ready and right-sized | Label `ready` and start work. |
| Not ready | Label `blocked` and link the Decision issue or explain what is missing. |
| Too big | Break it down as described in §0.4. Keep the original open as the parent, labelled `broken-down`. |

### 0.4 Breaking down

1. Split it into **vertical slices**. Each slice delivers working, testable behaviour on its own.
   - Good split points: one connector, one phase, one data type (fields, then links, then attachments), or happy path first and edge cases next.
   - Do **not** split by layer (for example "model", then "API", then "tests"). Those pieces are not independently done.
2. Create each slice as a new Backlog Item using the template, and add it as a **sub-issue** of the original. Each sub-issue carries over:
   - the relevant acceptance criteria
   - the in-scope connectors and phases
   - the azure-devops-migration-tools mapping rows
3. Check that every acceptance criterion of the original appears in at least one sub-issue. Nothing may be dropped silently. Anything deliberately dropped is listed with the reason.
4. **Keep the original open** as the parent. Label it `broken-down` (replacing `needs-assessment`/`ready`), and add a comment:
   - `Broken down into #a, #b, #c`
   - the coverage mapping from step 3
5. Each sub-issue goes through its own pre-start assessment, and may be broken down again, giving a deeper tree.

Create sub-issues with `gh issue create --parent <original> ...`. For an existing issue, use the REST `POST /repos/{owner}/{repo}/issues/{n}/sub_issues` endpoint.

### 0.5 Parent items

- No code work is done against a parent. Its sub-issues are the work, and the parent tracks progress toward the whole outcome.
- A parent is **closed only when all of its sub-issues are closed**. Then:
  - Check that every acceptance criterion of the parent is met by a closed-as-done sub-issue, or was deliberately dropped with a reason.
  - Run a final live end-to-end migration of the whole outcome.
  - Confirm the README parity table and module matrix reflect it.
  - Record this as a closing comment.
- If a sub-issue is closed as *not planned*, either re-cover its criteria in a new sub-issue or record them as dropped on the parent.

---

## A. Completion Protocol (L2) ⛔ MANDATORY

Before reporting done, every step below must pass, in order. Any failed step means the work is not done.

0. **Up to date with main.** Sync with the base branch first.
   - In an app-managed worktree, use `sync_with_base_branch`. Otherwise merge `main`.
   - Resolve conflicts, then re-run steps 1–2 on the merged result.
1. **Builds successfully.** As in L1 §1:
   - `dotnet build --no-incremental` in both Debug and Release, with 0 errors.
   - `./build.ps1 install` succeeds.
2. **Tests pass.** As in L1 §2: the full suite, run fresh after the last change. A targeted subset does not count.
3. **A real end-to-end migration completes correctly.** The organisations, authentication, per-run `adpe2e-<run-id>` projects, guards, and pass criteria are defined in [live-test-environment.md](live-test-environment.md), which takes precedence over the summary below.
   - Run the matching `scenarios/SystemTest-Live-*.json` scenario against the test organisation.
   - Supply the test PAT through these environment variables: `SOURCE_ORG_URL`, `SOURCE_PROJECT`, `SOURCE_PAT`, `TARGET_ORG_URL`, `TARGET_PROJECT`, `TARGET_PAT`.
   - Never write the PAT into a file, log, PR or report.
   - "Correctly" means you inspected the target state and it matches the source after mapping:
     - counts
     - a sampled item's fields, revisions, links and attachments
     - identities
   - Exit code 0 on its own is not enough.
   - A change that touches TFS also runs the matching `SystemTest-Live-*-TFS-*` scenario.
   - If live credentials are unavailable, the work is **not done**. Report `BLOCKED — live E2E not run`.
4. **Validated against the architectural norms.**
   - Run `nkda-core-implementation-architecture-compliance` over the changed scope.
   - Record pass or fail for each perspective (Modular Monolith, Clean, Hexagonal, Vertical Slice, Screaming, Deepening) and for the Constitution.
5. **Checked against the original ask.**
   - Re-read the Backlog Item and its acceptance criteria.
   - Map each criterion to evidence: a `path:line`, a test name, or an E2E observation.
   - Flag every criterion that is unmet, partly met, or reinterpreted.
6. **Cleaned up.** Remove everything you created only for the session:
   - scratch files, temporary configs and local packages
   - debug logging and throwaway tests
   - test projects and work items created in the target org
   - stray worktrees and branches

   The session evidence file is not scratch; keep it.

## B. Completion Report (L2) ⛔ MANDATORY

Report the following clearly, both in the PR description (see `.github/PULL_REQUEST_TEMPLATE.md`) and in the final response:

1. **What you did:** the outcome first, then the changes. Link the Backlog Item.
2. **How you tested it:**
   - the suites, with pass and fail counts
   - the live E2E scenario, and the target-state checks you performed
   - the RED → GREEN evidence
3. **How you updated the docs:**
   - the canonical docs you touched
   - the tooling you used (`update-docs` skill, README status matrix, parity table)
   - "N/A" is allowed only with a reason.
4. **Confidence:** a percentage, why, and which piece of evidence is weakest.
5. **What you did not do:**
   - skipped steps and unmet criteria
   - deferred follow-ups, each raised as a new Backlog Item and linked
   - known risks

   "Nothing" is a claim, so justify it.

### Auto-merge rule

| Confidence | Action |
| --- | --- |
| At least 90% **and** steps 0–6 all pass | Enable auto-merge on the PR |
| Below 90%, **or** any step failed or blocked | Disable auto-merge and state why |

This is the operator's standing instruction for PRs raised under this protocol. It is recorded as `standing_authorisations.backlog-automation` in `.agents/10-contracts/consent-policy.yaml`. It does not authorise committing, pushing or merging outside that scope (Constitution rule 11).

### Automation

| Stage | Skill / workflow |
| --- | --- |
| Create items | `nkda-backlog-item-create` |
| Pre-start assessment and breakdown | `nkda-backlog-item-prestart`, `nkda-backlog-groom-workflow` |
| Deliver ready items (parallel worktrees, independent verify, auto-merge rule) | `nkda-backlog-deliver-workflow` |

---

## C. Backlog Item DoD (L3) ⛔ MANDATORY

Apply every section that the item touches. A section that does not apply is marked `N/A` with a one-line reason; it is never left blank.

### C1. Scope

- [ ] Every acceptance criterion is met, with evidence.
- [ ] Any spec `tasks.md` this item covers is reconciled: each task is checked, or descoped with a reason and a new Backlog Item. No stale checkboxes remain.

### C2. Connectors (Constitution rule 10)

- [ ] Simulated, AzureDevOpsServices and TeamFoundationServer are each implemented for every phase in scope that the API supports.
- [ ] There is no `Null*Adapter`, guard-away, or "not supported" path unless an ADR records the API limitation. A code comment alone is not enough.
- [ ] `connector-coverage-check` passes.

### C3. Real-world proof

- [ ] A `SystemTest_Live` test covers the behaviour against real ADO, and against real TFS where TFS is in scope.
- [ ] Re-running, and interrupting then resuming, produce no duplicates (Constitution rule 5).
- [ ] Validate-phase checks cover the behaviour (ADR-0021).

### C4. Parity

- [ ] Every azure-devops-migration-tools behaviour mapped to this item is matched, improved, or deliberately dropped with a reason.
- [ ] The README *Compared with Azure DevOps Migration Tools* table and the module status matrix reflect the new state.

### C5. Operator usability

- [ ] The operator doc in `docs/` is updated: what it does, its limits, configuration, and troubleshooting.
- [ ] A working example config is in `scenarios/` and is referenced from the docs.
- [ ] New config options are in the configuration reference and the generated JSON Schema.
- [ ] CLI help matches behaviour, and the documented commands have been run exactly as written.
- [ ] Every error message an operator can trigger says what went wrong and what to do next.
- [ ] Progress is visible in `queue --follow` and in the TUI (L1 §3, O-4).

### C6. Release

- [ ] Release notes or a changelog entry are written.

### C7. Sign-off

- [ ] `nkda-core-definition-of-done` has been run against this checklist, and its report is linked from the Backlog Item.

---

## Enforcement (proposed)

- The issue templates in `.github/ISSUE_TEMPLATE/` carry the pre-start assessment and the L2 and L3 checklists.
- The PR template carries the §B report.
- *Follow-up:* add a `dod` section to `.agents/50-evidence/session-evidence.schema.json`, with one pass/fail/exempt entry per criterion, so that the `Session Evidence` CI gate rejects any failed criterion.
- *Follow-up:* add CI greps for the patterns banned in L1, and for any `Null*Adapter` without an exemption.
