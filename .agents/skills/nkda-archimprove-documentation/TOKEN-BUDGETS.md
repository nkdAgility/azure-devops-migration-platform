# Token Budgets

Use this file to keep agent-facing documentation small while allowing human documentation to deepen.

These are guidance limits, not hard line-count rules. Break them only when safety or correctness requires it.

## `agents.md` and `.github/copilot-instructions.md`

These are **entry-point files**, not rule files. Every agent session reads them first.

Token discipline is critical: every line in these files is read unconditionally, so bloat here costs tokens on every single task regardless of relevance.

Target size: under 300 lines each.

Maximum preferred size: 400 lines.

These files should contain:

- mandatory pre-flight read list (guardrail and context file paths only — no inline rules)
- pointers to guardrail files
- entry-point navigation for agents
- the challenge protocol (brief, link to detail)
- reject trigger summary table (brief — do not expand it)

These files must not contain:

- inline rules that already exist in a guardrail file
- verbatim copies of guardrail reject conditions
- tutorials, operator guidance, or contributor walkthroughs
- long-form examples
- content that changes frequently (keep stable; volatile content belongs in guardrails)

When a rule in `agents.md` or `copilot-instructions.md` is also present in a guardrail:

1. Keep only the guardrail version.
2. Replace the inline text in `agents.md` / `copilot-instructions.md` with a one-line reference: `see <guardrail-file>`.
3. Never delete the rule entirely — move it to the guardrail first, then remove from the entry-point file.

When a rule exists only in `agents.md` or `copilot-instructions.md` and is enforceable:

1. Move it to the appropriate guardrail file.
2. Replace the inline text with a reference.

Both files must always list the same guardrail and context paths. If they diverge, agents in different runtimes operate under different constraints.

## `.agents/guardrails`

Target size per file: 300 to 900 words.

Maximum preferred size per file: 1,200 words.

A guardrail file should contain:

- mandatory rules
- reject conditions
- completion checks
- links to canonical human docs or ADRs

A guardrail file should not contain:

- tutorials
- examples longer than a few lines
- historical explanation
- duplicated schemas
- operator walkthroughs

Split a guardrail file when it contains rules for multiple independent areas.

## `.agents/context`

Target size per file: 250 to 700 words.

Maximum preferred size per file: 1,000 words.

A context file should contain:

- compressed concept summary
- canonical terminology
- stable design assumptions
- pointers to full docs and ADRs

A context file should not contain:

- full guides
- long examples
- complete API references
- full configuration schemas
- troubleshooting sections

If an agent context file exceeds the target, reduce it by:

1. moving examples into `/docs`
2. moving mandatory rules into `.agents/guardrails`
3. moving decisions into `docs/adr/`
4. replacing detailed content with links

## `/docs`

No strict token budget.

Human documentation should be deep enough to be useful.

A human-facing doc may include:

- explanation
- examples
- workflow steps
- diagrams
- references
- troubleshooting
- operational guidance
- design rationale

Long docs should still have:

- clear table of contents
- audience statement
- purpose statement
- related documents section
- stable headings

## Duplication Budget

Duplication is acceptable only when the purpose differs.

Allowed purposeful overlap:

- `/docs` explains a topic in detail.
- `.agents/context` summarises the topic for agents.
- `.agents/guardrails` defines mandatory constraints for that topic.

Disallowed duplication:

- two docs explain the same workflow with different steps
- context repeats a guide in compressed form but still contains all details
- a guardrail embeds a full reference schema
- a rule appears in multiple guardrail files with different wording

When duplication is found, nominate one canonical source and reduce the others to summary plus link.
