# Documentation Rules

## Folder Authority

| Folder | Purpose |
|---|---|
| `docs/` | Explains the system to humans (operators, contributors) |
| `.agents/context/` | Compresses concepts for agents — minimal tokens, no tutorials |
| `.agents/guardrails/` | Enforces constraints on agents — imperative, rejection-focused |

The same topic may appear in all three with a different purpose. That is correct and expected.

## Naming Conventions

| Suffix | Use for |
|---|---|
| `-guide.md` | Operator or contributor how-to (e.g. `cli-guide.md`) |
| `-reference.md` | Complete reference material (e.g. `configuration-reference.md`) |
| `-rules.md` | Agent guardrail file |
| `-summary.md` | Agent context file that compresses a longer doc |

Files without a suffix are architectural anchors (`architecture.md`, `control-plane.md`).

## Reject Conditions

Reject any change that:

- Deletes a doc file instead of renaming it — always use `git mv`
- Renames a file without updating every cross-reference in the same commit
- Renames a file without updating `agents.md` and `.github/copilot-instructions.md`
- Puts implementation detail (class names, method signatures, SDK calls) in `.agents/context/terminology.md` — terminology is concepts only, one sentence per term
- Puts long-form tutorials or walkthroughs in `.agents/context/` — keep context files under ~100 lines
- Puts operator guidance in `.agents/guardrails/` — guardrails are for agents, not humans
- Creates a new guardrail file without adding it to `.agents/guardrails/README.md`
- Creates a new context file without adding it to `.agents/context/README.md`
- Creates a new `docs/` file without adding it to `docs/README.md`
- Adds a term to `terminology.md` that includes a class name, interface name, or code reference
