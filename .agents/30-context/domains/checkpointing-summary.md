# Checkpointing Summary

Compressed agent context for resume semantics.

Detailed architecture and examples:
- [docs/adr/0003-cursor-based-checkpointing.md](../../../docs/adr/0003-cursor-based-checkpointing.md)
- [docs/adr/0010-plan-driven-dag-execution.md](../../../docs/adr/0010-plan-driven-dag-execution.md)
- [docs/validation.md](../../../docs/validation.md)

Enforced constraints:
- [../../20-guardrails/migration-rules.md](../../20-guardrails/domains/migration-rules.md)
- [../../20-guardrails/module-rules.md](../../20-guardrails/domains/module-rules.md)
- [../../20-guardrails/package-rules.md](../../20-guardrails/domains/package-rules.md)

## Core Model

- Resume state is package-backed, never in-memory.
- Root `.migration/` stores package-wide orchestration markers.
- `/{org}/{project}/.migration/` stores project-scoped action/module cursors.
- `.migration/runs/<runId>/` is audit-only and never authoritative for resume decisions.

## Cursor Contract

Location:
`/{org}/{project}/.migration/{action}.{module}.cursor.json`

Shape:
```json
{
  "lastProcessed": "WorkItems/2026-02-25/638760123456789012-12345-17",
  "stage": "Completed",
  "updatedAt": "2026-02-25T18:12:34Z"
}
```

Canonical stage values:
- `CreatedOrUpdated`
- `AppliedFields`
- `AppliedLinks`
- `UploadedAttachments`
- `Completed`

## Resume Semantics

- Enumeration is lexicographic and forward-only.
- Skip entries `<= lastProcessed`.
- If stage is not `Completed`, resume the same folder at the next stage.
- Write cursor after each stage boundary.

## Export and Import Notes

- Export marks each completed revision folder with `stage: "Completed"`.
- Import uses staged replay with stage-level resume.
- Force-fresh resets cursor and phase marker files while preserving shared id mapping state.

## Migrate Mode Phase Record

Root phase record:
`/.migration/job.phase.json`

Tracks completed phases (`exportCompleted`, `prepareCompleted`, `importCompleted`) so reruns skip completed phases safely.

## Continuation Tokens

Resumable fetch paths may persist continuation tokens and query fingerprints.
If fingerprint mismatch occurs, resume is rejected to avoid replaying against incompatible query semantics.




