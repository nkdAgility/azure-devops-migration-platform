---
name: end-session
description: Verifies all gates have passed, finalises the session log, and signals readiness to commit.
---

# Skill: End Session

Use this skill after the Reviewer Agent has returned `"verdict": "Approved"` **and** Phase 5 (Documentation Sync) is complete.

## Steps

1. **Verify all gates are clear:**
   - Reviewer Agent output: `"verdict": "Approved"`.
   - All Reqnroll scenarios passing.
   - All unit tests passing.
   - No TODOs in production code paths.
   - **Documentation sync gate** (check EACH item before proceeding):
     - [ ] All items in `specs/<feature>/discrepancies.md` are marked `Resolved` or `N/A`
     - [ ] Every doc-task (e.g. `T0xx`) in `specs/<feature>/tasks.md` that names a `/docs/*.md` file is marked `[X]`
     - [ ] Every doc-task that names a `.agents/context/*.md` file is marked `[X]`
     - [ ] If a new CLI command was added: `docs/cli-guide.md` Commands table updated; `.vscode/launch.json` entry exists
     - [ ] If a new config field was added: `docs/configuration-reference.md` updated
     - [ ] If a new source/target type was added: `docs/capabilities-guide.md` updated; `.agents/context/job-lifecycle.md` updated
     - [ ] If package layout changed: `.agents/context/migration-package-concept.md` updated; `.agents/context/workitems-format-summary.md` updated
     - [ ] If a new module abstraction or interface was added: `docs/module-development-guide.md` updated; `docs/architecture.md` updated
     - [ ] If checkpointing behaviour changed: `.agents/context/checkpointing-summary.md` updated
     - [ ] `analysis/pending-actions.md` reviewed: any newly completed items removed

   Any unchecked item MUST be resolved before the session can close. Return to Phase 5 if items remain open.

2. **Finalise the session log** at `Logs/atdd-sessions/<session-id>.json`:

   ```json
   {
     "session_id": "<session-id>",
     "started_at": "<ISO 8601>",
     "completed_at": "<ISO 8601>",
     "requirement": "<one-sentence description>",
     "feature_file": "<path>",
     "scenario": "<title>",
     "phase": "complete",
    "completed_phases": ["specification", "spec-hardening", "test-generation", "implementation", "review", "doc-sync"],
     "doc_sync": {
       "discrepancies_resolved": true,
       "doc_tasks_checked": true,
       "pending_actions_updated": true,
       "no_change_justification": "<text or null>"
     },
     "outcome": "SUCCESS",
     "commit": "<sha or PR reference>"
   }
   ```

3. **Signal readiness to commit.** Do not commit automatically. Present the session summary and await human confirmation.

4. **On human confirmation**, the session is closed. The next scenario requires a fresh `/start-session`.

## Early Exit Conditions

- Any gate in step 1 is not clear → do not signal commit-ready; return to the failing phase.
- Documentation sync gate has unchecked items → do not signal commit-ready; return to Phase 5.
- Reviewer has not yet approved → stop, run `/review` first.
