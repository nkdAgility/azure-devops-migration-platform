---
name: start-session
description: Assembles the context needed to begin a single-scenario ATDD session and invokes the Specification Agent.
---

# Skill: Start Session

Use this skill at the beginning of every ATDD session. One session = one scenario.

## Steps

1. **Confirm the requirement is scoped to one thin vertical slice.**
   If it implies more than one independently deliverable behaviour, stop and ask the human to split it first.

2. **Assemble session context** by loading:
   - The project context: [.github/copilot-instructions.md](../copilot-instructions.md)
   - The architectural guardrails: [agents/system-architecture.md](../../agents/system-architecture.md)
   - The ATDD workflow: [agents/atdd-workflow.md](../../agents/atdd-workflow.md)
   - Relevant existing acceptance tests in [tests/acceptance/](../../tests/acceptance/) for naming reference.

3. **Generate a session ID** in the format `<feature-slug>-<scenario-slug>-<YYYYMMDD-HHmmss>`.

4. **Write the session start log** to `Logs/atdd-sessions/<session-id>.json`:

   ```json
   {
     "session_id": "<session-id>",
     "started_at": "<ISO 8601>",
     "requirement": "<one-sentence description>",
     "phase": "specification",
     "completed_phases": []
   }
   ```

5. **Invoke the Specification Agent** with the human's draft intent description.
   The session does not advance to Phase 2 until the Specification Agent returns `"human_approved": true` in its output JSON.

## Early Exit Conditions

- Requirement covers more than one scenario → stop, ask to split.
- A passing test already exists for this scenario → stop, this scenario is already done.
- An active session log already exists for this scenario → stop, resume with `/fix` instead.
