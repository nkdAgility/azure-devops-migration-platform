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
  specific PR with the `evidence-waived` label (recorded on the PR timeline);
  the job verifies via the PR's label events that the maintainer in
  `.agents/OWNERS` applied it.
- `activity`, `assumptions`, `deviations` and `risks` are required (an empty
  array is an explicit "none"); Class C records also need a non-empty
  `consent_reference`; `outcome: SUCCESS` needs at least one suite with
  `run: true`.
- An `outcome: SUCCESS` claim with any `failed > 0` suite, or with a suite
  claimed `run: true` but no counts, fails validation — evidence must be
  consistent with itself.
- Evidence must describe the diff it ships with: every `src/**` or `tests/**`
  path changed in the PR must match a `files_changed` entry (exact path or
  glob such as `tests/Foo/**`) in one of the PR's evidence files. Free-text
  entries like `tests/** (13 files)` do not match anything.

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
