# Completion Protocol (L2) and Completion Report

> **Status:** Ratified by the operator on 2026-10-15 (ADR-0034). Effective once merged to `main`.

This applies to every PR or session that claims a Backlog Item is done. It sits alongside [definition-of-done.md](definition-of-done.md) (L1) and [backlog-item-dod.md](backlog-item-dod.md) (L3). See [backlog-item-workflow.md](backlog-item-workflow.md) for how these fit together.

## A. Completion Protocol ⛔ MANDATORY

Every step must pass, in order. If any step fails, the work is not done.

0. **Up to date with main.**
   - Sync with the base branch first: use `sync_with_base_branch` in an app-managed worktree, otherwise merge `main`.
   - After syncing, run steps 1–2 again on the merged result.
1. **Builds successfully.**
   - `dotnet build --no-incremental` succeeds in Debug and Release with 0 errors.
   - `./build.ps1 install` succeeds.
2. **Tests pass.**
   - Run the full suite fresh after your last change; a targeted subset does not count.
   - Run each suite with a hard timeout. A run must never be left open-ended.
3. **A real end-to-end migration completes correctly.** Follow [live-test-environment.md](live-test-environment.md) for organisations, authentication, per-run projects and pass criteria.
   - **Correctly** means you inspected the target state: counts, at least one sampled item's fields, revisions, links and attachments, and identities. Exit code 0 alone is not enough.
   - Never write a credential to a file, log, PR or report.
   - A change that touches TFS also runs the matching TFS scenario.
   - If no live environment is available, the work is **not done**: report `BLOCKED — live E2E not run`.
4. **Validated against the architectural norms.**
   - Run `nkda-core-implementation-architecture-compliance` over the changed scope.
   - Record pass or fail for each perspective and for the Constitution.
5. **Checked against the original ask.**
   - Map every acceptance criterion to evidence: a `path:line`, a test name, or an E2E observation.
   - Flag any criterion that is unmet, partly met or reinterpreted.
6. **Cleaned up.** Remove everything created only for the session:
   - scratch files and temporary configs
   - debug logging
   - throwaway tests
   - test projects in the target org
   - stray processes, worktrees and branches

   Keep the session evidence file.

## B. Completion Report ⛔ MANDATORY

Report the following in the PR description (`.github/PULL_REQUEST_TEMPLATE.md`) and in the final response.

1. **What you did:** the outcome, then the changes. Link the Backlog Item.
2. **How you tested it:**
   - the suites, with pass and fail counts per `TestCategory`;
   - the live E2E scenario, with the target-state checks;
   - the RED → GREEN evidence.
3. **How you updated the docs:**
   - the canonical docs you touched;
   - the tooling you used;
   - "N/A" only with a reason.
4. **Confidence:** a percentage, why you chose it, and the weakest evidence.
5. **What you did not do:**
   - skipped steps and unmet criteria;
   - deferred follow-ups, each raised as a Backlog Item;
   - known risks.

   "Nothing" is a claim; justify it.

## C. Auto-merge rule

The author's confidence is never sufficient on its own. The decision belongs to the **independent verifier**: a separate agent or reviewer that did not write the change and has re-checked the evidence.

| Condition | Action |
| --- | --- |
| Verifier confidence ≥ 90% **and** steps 0–6 all pass **and** no unmet acceptance criteria | Enable auto-merge |
| Anything else, including a draft PR or a failed or blocked step | Disable auto-merge and state why |

This rule is the operator's standing instruction for PRs raised under this protocol. It is recorded as `standing_authorisations.backlog-automation` in `.agents/10-contracts/consent-policy.yaml`. It does not authorise committing, pushing or merging outside that scope (Constitution rule 11).
