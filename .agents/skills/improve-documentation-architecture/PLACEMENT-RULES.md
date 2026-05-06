# Documentation Placement Rules

Use these rules when deciding where content belongs.

## Authority Order

When content conflicts, apply this order:

1. `docs/adr/` for recorded architectural decisions.
2. `.agents/guardrails` for mandatory agent constraints.
3. `/docs` for human explanation, guides, references, and current design intent.
4. `.agents/context` for compressed agent-facing summaries.

If `.agents/context` conflicts with `/docs` or ADRs, update `.agents/context`.

If implementation conflicts with `.agents/guardrails`, the implementation is wrong unless the guardrail is deliberately amended.

## Put Content in `.agents/guardrails` When

Use `.agents/guardrails` when violation would make the implementation unacceptable.

Content belongs here when it can be written as:

- must
- must not
- reject
- required before completion
- prohibited

Examples:

- The Control Plane must not execute migration logic.
- The CLI must not write package artefacts.
- Import must not load all revisions into memory.
- Client code must not read counters from `ProgressEvent.Metrics`.

Guardrails should be short, imperative, and testable.

## Put Content in `.agents/context` When

Use `.agents/context` when an agent needs compact background to reason correctly.

Content belongs here when it answers:

- What is this concept?
- What terms should the agent use?
- What design assumptions are already settled?
- What does the agent need to know before changing code?

Context must not become long-form documentation.

Context should summarise and link to `/docs` or ADRs where full explanation is needed.

## Put Content in `/docs` When

Use `/docs` when a human needs to understand, operate, host, extend, diagnose, or verify the system.

Content belongs here when it needs:

- workflow steps
- examples
- screenshots or diagrams
- troubleshooting
- configuration examples
- reference tables
- exact schemas
- operational guidance
- contribution guidance
- design explanation

## Put Content in `docs/adr/` When

Use ADRs when future maintainers need to know why a decision was made.

Content belongs here when it records:

- accepted architectural decisions
- rejected alternatives
- consequences
- superseded decisions
- decisions that should not be repeatedly re-litigated

## Audience Placement

### Agents/AI

Primary folders:

- `.agents/guardrails`
- `.agents/context`

Needs:

- minimal-token context
- clear constraints
- explicit reject conditions
- small files
- links to canonical docs

Does not need:

- tutorials
- operator walkthroughs
- long examples
- historical explanation except as ADR summary

### Operators

Primary folder:

- `/docs`

Needs:

- how to run the tooling
- what process the migration follows
- what capabilities exist
- how to configure common scenarios
- how to inspect the package
- how to diagnose failures

Does not need:

- SDK details
- connector implementation details
- API pagination internals
- retry implementation internals
- contributor-only contracts

### Advanced Operators

Primary folder:

- `/docs`

Needs:

- hosting model
- running many jobs
- Control Plane operation
- Agent hosting
- observability
- security
- data sovereignty
- scaling and operational diagnostics

### Contributors

Primary folder:

- `/docs`

Needs:

- development setup
- testing model
- module development
- connector development
- client/API integration
- telemetry development
- architecture decisions
- contribution rules and expectations

## Mixed Content Resolution

When a file mixes audiences:

1. Preserve the canonical content.
2. Identify the dominant audience.
3. Move non-dominant content to the proper guide, reference, guardrail, context file, or ADR.
4. Replace moved content with a short summary and a link.
5. Update navigation.

## Client Integration Rule

Client integration material is contributor-facing unless it is limited to how an operator connects an existing client.

Place these in contributor docs or references:

- external client API usage
- SDK usage
- authentication flows for client implementation
- pagination
- retries
- rate limits
- UI live-data protocol
- reject conditions for client code
- connector implementation boundaries

Do not place those details in operator guides unless the operator needs them to run a migration.
