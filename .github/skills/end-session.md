---
name: end-session
description: Verifies all gates have passed, finalises the session log, and signals readiness to commit.
---

# Skill: End Session

Use this skill after the Reviewer Agent has returned `"verdict": "Approved"`.

## Steps

1. **Verify all gates are clear:**
   - Reviewer Agent output: `"verdict": "Approved"`.
   - All Reqnroll scenarios passing.
   - All unit tests passing.
   - No TODOs in production code paths.
   - Documentation updated if behaviour changed.

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
     "completed_phases": ["specification", "test-generation", "implementation", "review"],
     "outcome": "SUCCESS",
     "commit": "<sha or PR reference>"
   }
   ```

3. **Signal readiness to commit.** Do not commit automatically. Present the session summary and await human confirmation.

4. **On human confirmation**, the session is closed. The next scenario requires a fresh `/start-session`.

## Early Exit Conditions

- Any gate in step 1 is not clear → do not signal commit-ready; return to the failing phase.
- Reviewer has not yet approved → stop, run `/review` first.
