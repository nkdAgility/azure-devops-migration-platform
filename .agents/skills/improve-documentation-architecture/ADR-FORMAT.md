# ADR Format

Use this format when proposing or creating Architecture Decision Records under `docs/adr/`.

## File Naming

Use:

```text
docs/adr/NNNN-short-kebab-case-title.md
```

Examples:

```text
docs/adr/0001-source-files-target.md
docs/adr/0002-filesystem-package-as-source-of-truth.md
docs/adr/0003-cursor-based-checkpointing.md
docs/adr/0004-control-plane-does-not-execute-migrations.md
docs/adr/0005-agent-only-package-write-access.md
```

## ADR Template

```markdown
# ADR-NNNN: <Title>

## Status

Accepted | Proposed | Superseded | Deprecated

## Context

Describe the situation and forces that made the decision necessary.

## Decision

State the decision clearly.

## Consequences

Describe the positive, negative, and neutral consequences.

## Options Considered

### Option 1: <Name>

- Summary:
- Pros:
- Cons:

### Option 2: <Name>

- Summary:
- Pros:
- Cons:

## Related Documents

- `<path>`
```

## When to Propose an ADR

Propose an ADR when:

- a rule is repeated across multiple files but the underlying decision is not recorded
- a documentation split depends on a stable architectural decision
- a user rejects a recommendation for a reason future agents would otherwise rediscover
- an existing guide contains decision rationale that should be permanent
- a guardrail needs a recorded explanation

Do not propose an ADR for:

- temporary implementation preference
- unresolved speculation
- simple documentation placement
- facts already captured by an accepted ADR
