---
name: review
description: Passes the current diff and session context to the Reviewer Agent and records the verdict.
---

# Skill: Review

Use this skill after the Implementation Agent signals `"tests_passing": true` and `"pending_steps_remaining": 0`.

## Steps

1. **Verify pre-conditions before invoking the Reviewer Agent:**
   - All Reqnroll scenarios are passing.
   - All unit tests are passing.
   - No `PendingStepException` remains in any step definition.
   - The Implementer Agent's output JSON has `"pending_steps_remaining": 0` and `"tests_passing": true`.

2. **Assemble the review package:**
   - The git diff of all files changed in this session.
   - The Specification Agent's approved output JSON (intent, feature file, architecture constraints, acceptance criteria).
   - The Implementation Agent's output JSON.

3. **Invoke the Reviewer Agent** with the review package.

4. **On `"verdict": "Approved"`:**
   - Update the session log: set `"phase": "approved"`, append `"review"` to `"completed_phases"`.
   - Proceed to the `/end-session` command.

5. **On `"verdict": "Rejected"`:**
   - Update the session log: append the findings to a `"rejections"` array.
   - Return the Reviewer Agent's `"required_changes"` to the Implementer Agent.
   - Re-invoke Implementation Agent. Return to step 1 after implementation is complete.
   - If the Reviewer has rejected more than twice in this session, escalate to a human.

## Early Exit Conditions

- Pre-conditions in step 1 not met → do not invoke the Reviewer; return to Implementation Agent.
- More than two rejections in one session → stop and escalate to a human reviewer.
