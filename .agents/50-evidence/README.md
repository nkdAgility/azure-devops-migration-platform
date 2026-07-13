# Session Evidence

Machine-readable evidence for every unit of work (ADR-0033). This closes the
gap the readiness assessment called C1: the session-log mandate existed since
the test-first workflow was written, but no session log had ever been produced
and nothing checked for one.

## The contract

- Every change to `src/**` or `tests/**` lands with at least one evidence file
  at `Logs/atdd-sessions/<session-id>.json` added or updated in the same
  change.
- Evidence files validate against
  [`session-evidence.schema.json`](session-evidence.schema.json).
- `scripts/guardrails/validate-evidence.py` validates locally; the
  `Session Evidence` job in `.github/workflows/governance.yml` blocks PRs
  without valid evidence. The maintainer may waive the requirement for a
  specific PR with the `evidence-waived` label (recorded on the PR timeline).
- An `outcome: SUCCESS` claim with any `failed > 0` suite, or with a suite
  claimed `run: true` but no counts, fails validation — evidence must be
  consistent with itself.

## Who writes it

The `end-session` skill emits the file as its step 2 (its JSON template *is*
this schema's shape). Sessions that do not use the tests-first pipeline —
hotfixes, harness changes, remediation passes — write the file directly before
requesting a commit; `nkda-core-definition-of-done` checks it.

## What goes in `suites`

Record what was actually executed after the last change, with pass/fail
counts from the run output. A suite that was not run is listed with
`run: false` and a `not_run_reason` (e.g. "SystemTest_Live: environment-gated,
runs in CI"). Never claim a suite you did not run — the failing-tests
workflow's Iron Law applies: no completion claim without fresh output.
