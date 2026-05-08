---
name: fix
description: Resumes a failed or interrupted tests-first session from the last successful phase.
---

# Skill: Fix

Use this skill when a session has been interrupted, a phase has failed, or the Reviewer has returned `"Rejected"`.

## Steps

1. **Locate the active session log** in `Logs/atdd-sessions/<session-id>.json`.
   If no log exists, start a fresh session with `/start-session` instead.

2. **Identify the last successful phase** from `"completed_phases"` in the session log.

3. **Resume from the next phase:**

   | Last completed phase | Resume at |
   |---|---|
   | _(none)_ | Specification Agent |
   | `"specification"` | Spec Hardening |
   | `"spec-hardening"` | Test Generation Agent |
   | `"test-generation"` | Implementation Agent |
   | `"implementation"` | `/review` |
   | `"review"` | Documentation Sync |
   | Review rejected | Implementation Agent (with Reviewer's `required_changes`) |

4. **Do not restart from Phase 1** unless the feature definition itself is wrong. A rejection from the Reviewer returns to Implementation only. If specification intent changes, re-run Spec Hardening before continuing.

5. **Update the session log** to reflect the resumed phase before continuing.

## Early Exit Conditions

- No session log found → use `/start-session` instead.
- Rejected more than twice → stop and escalate to a human reviewer; do not loop indefinitely.
- Feature definition is wrong → restart from Phase 1 only after human confirms the spec change is needed.
