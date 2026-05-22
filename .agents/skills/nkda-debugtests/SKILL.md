---
name: nkda-debugtests
description: Mandatory procedure for diagnosing and fixing failing tests. For each failing test-  validate it fails, read test results and logs as evidence, state root cause, apply minimal fix, verify. Then widen sequentially to unit, original failing tests, simulated tests, and live tests. Completion is blocked until all required suites are green after the last code, configuration, test, script, or workflow change, with fresh evidence in the response.
---

# NKDA Debug Tests

## When to Use

Use this skill whenever:
- Any test is failing
- A fix was applied but tests have not been verified
- A test suite run returned non-zero exit code
- You are about to claim "tests pass" without fresh output
- You are about to claim completion after changing code, configuration, tests, scripts, or workflow files

## The Full Procedure

The complete step-by-step workflow is in:

```text
.agents/20-guardrails/workflow/failing-tests-workflow.md
```

Read it. Follow every step. Do not improvise.

The summary below is not a substitute for the full workflow.

---

## Non-Negotiable Completion Rule

A test pass is only valid as completion evidence if it was produced after the last code, configuration, test, script, or workflow change.

Historical passing output is not completion evidence.

If anything changes after a test pass, that passing output is stale. Stale output may be mentioned as history, but it must not be used to claim completion.

Step 7 is blocked unless fresh passing evidence exists for:
- Step 3, all Unit Tests
- Step 5, all Simulated tests
- Step 6, all Live tests

Task scope does not override this rule.

---

## Summary

Work through this checklist in order.

Do not advance to the next step without completing the current one.

Do not claim Step 7 unless the response contains fresh passing test results and logs for Steps 3, 5, and 6, all produced after the last code, configuration, test, script, or workflow change.

### Phase A, Fix each failing test

Repeat Phase A until every failing test from the current failing list passes.

```text
[ ] 1. Run the last relevant suite and collect every failing test by name
[ ] 2.1 For each failing test, validate it still fails by running it in isolation
[ ] 2.2 Read .otel-diagnostics logs and test results, DO NOT GUESS
[ ] 2.3 State root cause with evidence from logs or test output before changing anything
[ ] 2.4 Apply the smallest possible single fix
[ ] 2.5 Run that one test again
       - Passes: move to the next failing test
       - Fails: return to 2.2 with the new evidence
```

### Phase B, Widen verification

Run these gates in order.

Any failure returns to Phase A Step 1.

Any code, configuration, test, script, or workflow change invalidates all previous completion evidence and requires Phase B to restart from Step 3.

```text
[ ] 3. Run all Unit Tests
       REQUIRED before Step 4:
       - passing Unit Test result
       - relevant logs or test output
       - confirmation this was run after the last change

[ ] 4. Run the original failing tests again
       REQUIRED before Step 5:
       - passing result for every originally failing test
       - confirmation this was run after the last change

[ ] 5. Run all Simulated tests
       REQUIRED before Step 6:
       - passing Simulated test result
       - relevant logs or test output
       - confirmation this was run after the last change

[ ] 6. Run all Live tests
       REQUIRED before Step 7:
       - passing Live test result
       - relevant logs or test output
       - confirmation this was run after the last change

[ ] 7. Claim victory
       ONLY valid if Steps 3, 5, and 6 have fresh passing evidence in this response
```

Skipping Steps 3 or 5 and claiming Step 7 is a violation, regardless of the task scope given.

---

## Verification Ledger

Maintain this ledger while working. Update it after every test command and after every change.

A historical pass is not enough. The gate must be valid after the last change.

```text
| Gate | Command | Latest result | Ran after last change? | Valid completion evidence? |
|------|---------|---------------|-------------------------|-----------------------------|
| Step 3, Unit Tests | pwsh ./build.ps1 Test | Not run | No | No |
| Step 5, Simulated Tests | pwsh ./build.ps1 SystemTest_Simulated | Not run | No | No |
| Step 6, Live Tests | pwsh ./build.ps1 SystemTest_Live | Not run | No | No |
```

Step 7 is allowed only when all three rows have:
- Latest result = Pass
- Ran after last change = Yes
- Valid completion evidence = Yes
- Matching output appears in the response

If any row is No, Step 7 is blocked.

---

## Evidence Freshness and Invalidation Rules

A gate is not a permanent pass.

Any new failure or new change can invalidate earlier evidence.

### If any test command fails after Step 3 passed

- Step 3 remains historically passed
- Step 3 is no longer valid completion evidence
- Steps 4, 5, 6, and 7 are blocked
- After the fix, Step 3 must be rerun before continuing

### If any test command fails after Step 5 passed

- Step 5 remains historically passed
- Step 5 is no longer valid completion evidence
- Steps 6 and 7 are blocked
- After the fix, Step 5 must be rerun before continuing

### If any test command fails after Step 6 passed

- Step 6 remains historically passed
- Step 6 is no longer valid completion evidence
- Step 7 is blocked
- After the fix, Step 6 must be rerun

### If any code, configuration, test, script, or workflow file changes

- All previous completion evidence is stale
- Step 7 is blocked
- Phase B must restart from Step 3
- The final response must only use test output produced after the last change

---

## Required Blocked-Completion Response

If completion is blocked, say so directly.

Use this format:

```text
Completion is blocked.

Missing fresh evidence:
- Step 3 Unit Tests: <missing / stale / failed / not run>
- Step 5 Simulated Tests: <missing / stale / failed / not run>
- Step 6 Live Tests: <missing / stale / failed / not run>

Next required action:
- <exact command to run next>
```

Do not soften this. Do not claim partial completion as completion.

---

## Evidence Locations

| What | Where |
|------|-------|
| OTel logs | `.output\workingtests\<TestName>\.otel-diagnostics\*-logs.log` |
| OTel traces | `.output\workingtests\<TestName>\.otel-diagnostics\*-traces.json` |
| Raw payloads | `.output\workingtests\<TestName>\.otel-diagnostics\inbox\` |
| Package output | `.output\workingtests\<TestName>\<org>\<project>\` |
| Test stdout | Test runner console output |

---

## Hard Rules

- Build passing does not mean the fix works. Always run the relevant test.
- No root cause stated means no fix is allowed. Read evidence first.
- No evidence means no conclusion. Do not infer from intent.
- One fix at a time. No bundled refactoring.
- Three failed fixes means stop. Raise an architectural concern before trying again.
- No completion claim without fresh test results and logs in the response.
- Step 7 requires fresh Step 3, Step 5, and Step 6 output in the response.
- Task scope does not override the verification gate.
- "The task only asked me to fix one test" is not an excuse to skip Steps 3 and 5.
- Historical passing output is stale if anything changed afterwards.
- A stale pass may be reported as history, but never as completion evidence.
- **`Assert.Inconclusive` is banned** unless explicitly operator-approved. The only permitted exception is a live test targeting infrastructure known to be absent in all CI environments. It must name the missing infra in a test-body comment, be documented in the suite, and have operator sign-off. Any other use is a defect — replace with `Assert.Fail`.
- **Missing prerequisites must fail the test, not skip it.** If a required env var, credential, or external resource is absent, call `Assert.Fail` with a clear message. `Assert.Inconclusive` or any skip mechanism is not permitted without operator approval.

---

## Related

- Full workflow: `.agents/20-guardrails/workflow/failing-tests-workflow.md`
- Verification gate: `.agents/skills/verification-before-completion/SKILL.md`
- Deep debugging: `.agents/skills/systematic-debugging/SKILL.md`
- Mandatory test-fix loop: `.agents/20-guardrails/workflow/testing-rules.md`
