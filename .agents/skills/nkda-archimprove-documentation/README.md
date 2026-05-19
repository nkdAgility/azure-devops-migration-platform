# Improve Documentation Architecture Skill

This package contains a documentation architecture skill for reviewing and restructuring:

- `.agents/20-guardrails`
- `.agents/30-context`
- `docs`

The skill is designed to deepen and broaden human documentation while controlling the token surface for agent-facing files.

## Files

| File | Purpose |
|---|---|
| `SKILL.md` | Main skill instructions. |
| `DOCUMENTATION-MAP.md` | Target documentation architecture and file purposes. |
| `PLACEMENT-RULES.md` | Rules for placing content by audience and authority. |
| `TOKEN-BUDGETS.md` | Guidance for controlling `.agents` token surface. |
| `REPORT-FORMAT.md` | Standard report format for documentation structure reviews. |
| `ADR-FORMAT.md` | ADR template and ADR proposal rules. |

## Intended Use

Use this skill when you want an agent to:

- inspect current docs
- identify audience and authority problems
- propose restructuring opportunities
- split operator, advanced operator, contributor, context, guardrail, and ADR content
- reduce duplication and drift
- keep `.agents/30-context` concise
- keep `.agents/20-guardrails` enforceable
- deepen `/docs` for humans

