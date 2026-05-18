# Documentation Rules

Full placement model: [PLACEMENT-RULES.md](../../skills/nkda-archimprove-documentation/PLACEMENT-RULES.md)
Token budgets: [TOKEN-BUDGETS.md](../../skills/nkda-archimprove-documentation/TOKEN-BUDGETS.md)

---

## Reject Conditions

Reject any change that:

- Deletes a doc file instead of renaming it — always use `git mv`
- Renames a file without updating every cross-reference in the same commit
- Renames, adds, or removes a guardrail or context file without updating the pre-flight list in **both** `agents.md` and `.github/copilot-instructions.md` atomically — the two lists must stay identical
- Adds an inline rule to `agents.md` or `copilot-instructions.md` when that rule already exists in a guardrail — reference the guardrail instead
- Leaves an enforceable rule only in `agents.md` or `copilot-instructions.md` — move it to the appropriate guardrail first, then replace with a reference
- Adds index, README, manifest, or entrypoint wording that implies one guardrail is more mandatory, more authoritative, or more optional than another. Discovery and scope-loading docs may explain when a guardrail applies, but they must not create a priority hierarchy among guardrails beyond the existing documentation authority model.
- Places content that answers *must/must not/reject* anywhere other than `.agents/20-guardrails/` (see PLACEMENT-RULES.md for the full test)
- Places long-form tutorials, walkthroughs, or operator guidance in `.agents/30-context/domains/`
- Places operator guidance or how-to content in `.agents/20-guardrails/`
- Creates a new guardrail file without adding it to `.agents/20-guardrails/README.md`
- Creates a new context file without adding it to `.agents/90-index/context-index.md`
- Creates a new `docs/` file without adding it to `docs/README.md`
- Puts class names, interface names, method signatures, or code references in `.agents/30-context/primers/terminology.md` — terminology is concepts only, one sentence per term
- Allows a guardrail file to exceed 1 200 words without splitting it (see TOKEN-BUDGETS.md)
- Allows an agent context file to exceed 1 000 words without reducing it (see TOKEN-BUDGETS.md)
- Allows `agents.md` or `copilot-instructions.md` to exceed 400 lines without trimming inline content to guardrail references (see TOKEN-BUDGETS.md)
- Uses a non-standard file suffix that does not match the naming conventions in PLACEMENT-RULES.md







