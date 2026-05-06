# Token Budgets

Use this file to keep agent-facing documentation small while allowing human documentation to deepen.

These are guidance limits, not hard line-count rules. Break them only when safety or correctness requires it.

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
