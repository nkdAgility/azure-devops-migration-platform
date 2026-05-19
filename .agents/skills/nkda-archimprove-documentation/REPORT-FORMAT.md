# Documentation Structure Report Format

Use this format when reporting documentation architecture findings.

## Header

```markdown
# Documentation Structure Report

## Scope

Included:

- `.agents/20-guardrails`
- `.agents/30-context`
- `docs`

Excluded:

- `.agents/skills`
- `.github/agents`
- `.github/commands`
- `.specify`
- `features`
- `specs`
```

## Executive Summary

Keep this short.

Include:

- the main structural issue
- the main audience issue
- the main token-surface issue
- the highest-value next change

## Established from Current Docs

Use this section for facts directly observed in files.

Example:

```markdown
- `docs/control-plane.md` currently explains Control Plane responsibilities, lease protocol, progress reporting, authentication, authorisation, and data storage.
- `docs/client-integration-guide.md` contains client startup sequence, live data channels, reconnect rules, data schemas, and reject conditions.
```

## Synthesis

Use this section for analysis based on the observed files.

Example:

```markdown
The current client integration guide is contributor-facing because it defines implementation contracts and reject conditions for client code. It should not be treated as an operator guide, although operator-facing CLI/TUI guides may link to it as a reference.
```

## Current Documentation Map

Use a table.

```markdown
| File | Current audience | Current authority | Issue | Recommendation |
|---|---|---|---|---|
| `docs/example.md` | Mixed | Mixed | Combines operator workflow and contributor API details | Split into operator guide plus contributor reference |
```

## Deepening Opportunities

Use this exact structure for each candidate.

```markdown
### 1. <Opportunity name>

- **Files**: `<file>`, `<file>`
- **Current problem**: <problem>
- **Audience impact**: <who is hurt>
- **Authority problem**: <explains, compresses, constrains, or decides in wrong place>
- **Proposed change**: <change>
- **Token impact**: <decrease, stable, increase with reason>
- **Human value impact**: <value>
- **Risk**: <risk>
- **Confidence**: High | Medium | Low
```

## Recommended Sequence

Order the work so that authority and navigation are stabilised before broadening content.

Suggested order:

1. README and navigation fixes.
2. Guardrail extraction.
3. Context compression.
4. Operator guide deepening.
5. Advanced operator guide deepening.
6. Contributor guide deepening.
7. Reference and ADR work.

## Open Questions

List only questions that block safe restructuring.

Do not ask questions that can be answered by reading existing files.

## Proposed File Changes

When implementation is requested, list:

- files to create
- files to move
- files to split
- files to update
- files to deprecate

## Completion Check

Include this checklist when changes are made:

```markdown
- [ ] Relevant source docs were re-read.
- [ ] Audience and authority were preserved or corrected.
- [ ] `.agents/30-context` token surface was not expanded unnecessarily.
- [ ] `.agents/20-guardrails` remains imperative and testable.
- [ ] `/docs` remains human-facing and navigable.
- [ ] Links and related-document sections were updated.
- [ ] ADR impact was checked.
- [ ] Open questions were recorded.
```

