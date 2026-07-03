# WorkItems Format Summary

Compressed context for WorkItems package layout and import ordering.

Canonical references:
- [../../docs/package-format-reference.md](../../../docs/package-format-reference.md)
- [../../docs/work-item-iteration-guide.md](../../../docs/work-item-iteration-guide.md)

Enforced rules:
- [workitems-rules.md](../../20-guardrails/domains/workitems-rules.md)
- [migration-rules.md](../../20-guardrails/domains/migration-rules.md)
- [architecture-boundaries.md](../../20-guardrails/core/architecture-boundaries.md)

## Canonical Layout

```text
WorkItems/
  yyyy-MM-dd/
    <ticks>-<workItemId>-<revisionIndex>/
      revision.json
      [comment.json]
      [attachment binaries]
      [embedded images]
    <ticks>-<workItemId>-c<commentId>/
      comment.json
      [embedded images]
```

## Ordering and Streaming

- Date folder `yyyy-MM-dd` + ticks-prefixed folder names preserve chronological lexicographic order.
- Import processes revision/comment folders in lexicographic order.
- No global in-memory sorting is allowed.

## Revision Record Expectations

`revision.json` carries revision identity, changed date, fields, link sets, and attachment/image metadata.

Attachment and embedded image files are colocated with the parent revision/comment folder. There is no global attachments root.

## Comments

- Comment folders use `c<commentId>` suffix.
- Multiple folders may exist for the same comment when edited over time.
- Deleted comment inclusion is controlled by module extension configuration.

## Inline Comment Capture

When enabled, revision-local `comment.json` may be written for comment edit/delete detection paths. Failures in comment API retrieval are non-fatal and surfaced through progress/diagnostics channels.

## Rule of Thumb

If a change alters WorkItems naming, ordering, stage semantics, or attachment placement, update:
1. guardrails (`workitems-rules`, `migration-rules`)
2. canonical docs (`package-format-reference`, `work-item-iteration-guide`)
3. this summary




