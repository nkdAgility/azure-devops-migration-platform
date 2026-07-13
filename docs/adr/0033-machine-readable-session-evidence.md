# ADR 0033 — Machine-Readable Session Evidence

## Status

Accepted — executes Phase 3 of `analysis/agentic-maturity-level4-plan.md`, implemented at the maintainer's instruction (2026-07-13)

## Context

The tests-first workflow has mandated a session log at `Logs/atdd-sessions/<session-id>` since it was written, and the `end-session` skill has always specified a JSON template for it. The readiness assessment's most severe finding (C1) was that across ~39 delivered features, **no session log had ever been produced** — the directory did not exist — and nothing checked for one. Completion claims lived only in chat transcripts, which are not reviewable artifacts. Another engineer could not reconstruct what a change asked for, assumed, ran, or proved.

The gap was not the mandate; it was that the mandate had no schema (so "evidence" had no defined shape), no emitter that treated it as blocking, and no gate that noticed its absence.

## Decision

1. **The session-evidence format is a schema**, `.agents/50-evidence/session-evidence.schema.json`, formalizing the shape the `end-session` skill already used and adding the governance fields: `change_class`, `consent_reference`, `activity`, `files_changed`, per-suite `run/passed/failed` records, `assumptions`, `deviations`, `risks`.
2. **Evidence must be internally consistent**: `scripts/guardrails/validate-evidence.py` rejects `outcome: SUCCESS` with any failing suite, `run: true` without counts, and `run: false` without a `not_run_reason`. Suites record only what was executed after the last change (the failing-tests Iron Law, now machine-checked at the edges).
3. **The `Session Evidence` CI gate** (`.github/workflows/governance.yml`) blocks any PR touching `src/**` or `tests/**` that does not add or update a `Logs/atdd-sessions/*.json` evidence file, and validates every evidence file the PR touches. The maintainer may waive per-PR with the `evidence-waived` label.
4. **Emitters**: `end-session` step 2 now produces the schema-conformant file; Definition of Done gains section 9 (Session Evidence, ⛔ MANDATORY) covering sessions that do not use the tests-first pipeline; test-first-workflow rule 11 names the JSON alongside the markdown narrative log.
5. **A PR template** (`.github/PULL_REQUEST_TEMPLATE.md`) mirrors the evidence fields so reviewers see the summary without opening the file.

## Alternatives Considered

**Evidence as PR artifacts instead of committed files.** Rejected: artifacts expire, are invisible to `git log`, and cannot be drift-checked; the mandate already names a repo path, and committed evidence makes history self-describing.

**Cross-checking evidence counts against CI TRX results.** Deferred: the evidence gate runs in parallel with the build job and cannot see its TRX output without job-ordering changes; the consistency rules above catch the self-contradiction class now, and TRX cross-checking is a Phase 4 hardening candidate.

**A new evidence format instead of the end-session shape.** Rejected: the skill's existing template was already right in outline; formalizing it avoids two competing formats.

## Consequences

- A change to product or test code cannot merge silently without a reviewable record of what was run — or a visible, maintainer-attributed waiver.
- Chat-transcript claims stop being the only completion record; `git log` plus `Logs/atdd-sessions/` reconstructs any change.
- The maintainer must create the `evidence-waived` label in GitHub settings alongside `class-c-approved`.
- Evidence files are small JSON documents; the directory grows one file per session, which is the point.
