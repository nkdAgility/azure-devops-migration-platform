# Failing Tests Workflow — Completion Gate

Mandatory gate when any test is failing. The full step-by-step procedure,
verification ledger, and diagnostic evidence locations live in
[`docs/failing-tests-workflow.md`](../../../docs/failing-tests-workflow.md).
Follow that procedure exactly; this file defines what is enforced.

## Iron Law

```text
NO COMPLETION CLAIM WITHOUT FRESH PASSING TEST OUTPUT IN THE RESPONSE
```

A green build is not evidence. Test output is evidence only if produced after
the last code, configuration, test, script, or workflow change.

## Completion Gate

Completion is allowed only when ALL of the following are true:

- Unit tests (`pwsh ./build.ps1 Test`) passed after the last change.
- Simulated system tests (`pwsh ./build.ps1 SystemTest_Simulated`) passed after the last change.
- Live system tests (`pwsh ./build.ps1 SystemTest_Live`) passed after the last change.
- The passing output for all three suites appears in the response.
- No later failure or change invalidated the evidence.

If any condition is false, do not claim completion. Report which evidence is
missing, stale, failed, or not run, and the exact next command.

## Mandatory Rules

1. Build a concrete named list of failing tests before fixing anything.
2. No fix without a stated root cause backed by evidence (log line, trace, file, or test output). Read `.trx`/stdout/stderr first, then OTel diagnostics, then package files.
3. One minimal fix at a time. No bundled refactoring, no speculative edits, no stacked fixes. Third failed attempt on the same test → stop and raise an architectural concern.
4. Any code, configuration, test, script, or workflow change invalidates ALL previous completion evidence; rerun the suites.
5. A skipped, inconclusive, or ignored test does not satisfy a gate. Missing prerequisites must `Assert.Fail`, not skip.
6. `Assert.Inconclusive` is banned without written operator approval per the documented exception conditions.
7. Live-test timeout increases are allowed only under the five conditions in the documented procedure; treat any timeout change as a code change.

## Reject Conditions

Reject any completion claim that:

- omits fresh suite output for unit, simulated, and live gates
- uses "build succeeded" or historical output as correctness evidence
- proposes or applies a fix before stating an evidenced root cause
- treats a skip as a pass, or leaves skip counts unexplained
- skips suite-level verification because "only one test was in scope"
