# Documentation Rules

Full placement model: [PLACEMENT-RULES.md](../skills/nkda-improve-documentation-architecture/PLACEMENT-RULES.md)
Token budgets: [TOKEN-BUDGETS.md](../skills/nkda-improve-documentation-architecture/TOKEN-BUDGETS.md)

---

## Reject Conditions

Reject any change that:

- Deletes a doc file instead of renaming it — always use `git mv`
- Renames a file without updating every cross-reference in the same commit
- Renames, adds, or removes a guardrail or context file without updating the pre-flight list in **both** `agents.md` and `.github/copilot-instructions.md` atomically — the two lists must stay identical
- Adds an inline rule to `agents.md` or `copilot-instructions.md` when that rule already exists in a guardrail — reference the guardrail instead
- Leaves an enforceable rule only in `agents.md` or `copilot-instructions.md` — move it to the appropriate guardrail first, then replace with a reference
- Places content that answers *must/must not/reject* anywhere other than `.agents/guardrails/` (see PLACEMENT-RULES.md for the full test)
- Places long-form tutorials, walkthroughs, or operator guidance in `.agents/context/`
- Places operator guidance or how-to content in `.agents/guardrails/`
- Creates a new guardrail file without adding it to `.agents/guardrails/README.md`
- Creates a new context file without adding it to `.agents/context/README.md`
- Creates a new `docs/` file without adding it to `docs/README.md`
- Puts class names, interface names, method signatures, or code references in `.agents/context/terminology.md` — terminology is concepts only, one sentence per term
- Allows a guardrail file to exceed 1 200 words without splitting it (see TOKEN-BUDGETS.md)
- Allows an agent context file to exceed 1 000 words without reducing it (see TOKEN-BUDGETS.md)
- Allows `agents.md` or `copilot-instructions.md` to exceed 400 lines without trimming inline content to guardrail references (see TOKEN-BUDGETS.md)
- Uses a non-standard file suffix that does not match the naming conventions in PLACEMENT-RULES.md
