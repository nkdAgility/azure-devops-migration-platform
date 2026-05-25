# Failing Tests Workflow

Mandatory procedure when any test is failing.

Follow every step in order.

Do not skip steps.

Do not claim completion without fresh evidence.

---

## Iron Law

```text
NO COMPLETION CLAIM WITHOUT FRESH PASSING TEST OUTPUT IN THE RESPONSE
```

Build passing does not mean tests passed.

A green build is not evidence.

Test output is evidence only if it was produced after the last code, configuration, test, script, or workflow change.

Historical passing output is stale if anything changed afterwards.

---

## Completion Gate

Completion is allowed only when all of the following are true:

```text
CompletionAllowed =
  Step3.UnitTests.PassedAfterLastChange
  AND Step5.SimulatedTests.PassedAfterLastChange
  AND Step6.LiveTests.PassedAfterLastChange
  AND Response.Contains(Step3.Output)
  AND Response.Contains(Step5.Output)
  AND Response.Contains(Step6.Output)
```

If `CompletionAllowed` is false, do not claim completion.

Instead state:

```text
Completion is blocked.

Missing fresh evidence:
- Step 3 Unit Tests: <missing / stale / failed / not run>
- Step 5 Simulated Tests: <missing / stale / failed / not run>
- Step 6 Live Tests: <missing / stale / failed / not run>

Next required action:
- <exact command to run next>
```

---

## Evidence Freshness Rule

A passing test result is valid completion evidence only if:
- It passed
- It was run after the last code, configuration, test, script, or workflow change
- The relevant output appears in the response
- No later failure invalidated it

A passing test result is stale if:
- Code changed after it ran
- Configuration changed after it ran
- Tests changed after it ran
- Scripts changed after it ran
- Workflow files changed after it ran
- Another later test failure required a new fix
- The output is not included in the response

Stale output may be mentioned as history, but it is not evidence of correctness.

---

## Verification Ledger

Maintain this ledger while working.

Update it after every test command.

Update it after every code, configuration, test, script, or workflow change.

Do not leave an old `Pass` marked as valid after a new change or failure.

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

If any row is No, Step 7 is blocked.

---

## Invalidation Rules

A gate is not a permanent pass.

Any new failure or new change can invalidate earlier evidence.

### Any change invalidates completion evidence

If any code, configuration, test, script, or workflow file changes:

- All previous completion evidence becomes stale
- Step 7 is blocked
- Phase B must restart from Step 3
- The final response must only use output produced after the last change

### Failure after Step 3

If any test command fails after Step 3 has passed:

- Step 3 remains historically passed
- Step 3 is no longer valid completion evidence
- Steps 4, 5, 6, and 7 are blocked
- Return to Step 1
- After the fix, rerun Step 3 before continuing

### Failure after Step 5

If any test command fails after Step 5 has passed:

- Step 5 remains historically passed
- Step 5 is no longer valid completion evidence
- Steps 6 and 7 are blocked
- Return to Step 1
- After the fix, rerun Step 5 before continuing

### Failure after Step 6

If any test command fails after Step 6 has passed:

- Step 6 remains historically passed
- Step 6 is no longer valid completion evidence
- Step 7 is blocked
- Return to Step 1
- After the fix, rerun Step 6 before continuing

---

## Step 1, Build the Failing Test List

Run the last relevant test suite and collect every failing test by name.

```powershell
pwsh ./build.ps1 SystemTest_Live
```

Record each failing test as a numbered list.

This list drives the rest of the workflow.

Do not proceed without a concrete list.

Invalid:

```text
Tests are failing.
```

Valid:

```text
Failing tests:
1. ExportJob_WritesExpectedTelemetry_WhenLiveSystemRuns
2. ImportJob_ReplaysPackage_WhenLiveTargetExists
```

If the suite passes, continue to Step 3. Do not claim completion yet.

---

## Step 2, For Each Failing Test

Repeat Steps 2.1 to 2.5 for every test in the failing list before advancing to Step 3.

Do not bundle fixes across multiple failing tests unless the same evidenced root cause explains all of them.

---

### Step 2.1, Validate It Still Fails

Run the single test in isolation.

```powershell
pwsh ./build.ps1 RunTest "<TestName>"
```

Confirm the failure is real and current, not a stale result.

If it now passes:
- Remove it from the current failing list
- Continue to the next failing test

If it fails:
- Continue to Step 2.2

---

### Step 2.2, Read the Evidence

Do not guess.

Read first.

Evidence read order is mandatory:

1. Test result artifacts and test runner output (`.trx`, stdout, stderr, assertion text)
2. OTel diagnostics and raw payload logs
3. Generated package files

OTel diagnostics are written by every spawned CLI or agent process:

```text
.output\workingtests\<TestMethodName>\.otel-diagnostics\
```

Files to read:
- `*-logs.log`, structured log output from each process
- `*-traces.json`, span and activity traces
- `*.metrics.json`, counter snapshots
- `inbox\`, raw bootstrap, telemetry, and progress payloads

First read:
- Test result artifacts (`TestResults\*.trx`)
- Test stdout
- Test stderr
- Assertion output

Then read:
- OTel diagnostics files
- Relevant generated package files

Trace the failure path:

1. What did the test assert?
2. What was the actual value, missing file, wrong count, wrong state, or unexpected exception?
3. Which process produced that value?
4. What does the log for that process show at the point of failure?
5. What trace or payload confirms the behaviour?
6. Where did the wrong value or missing behaviour originate?

This is root cause investigation.

Do not skip it.

---

### Step 2.3, State Root Cause

Before touching code, write the root cause using this format:

```text
Root cause: <specific behaviour> is happening because <specific cause>.
Evidence: <log line / trace / file / test output>.
```

If you cannot state the root cause with evidence, return to Step 2.2.

No root cause means no fix is allowed.

---

### Step 2.4, Apply the Minimal Fix

Apply the smallest possible single change that addresses the stated root cause.

Rules:
- One change at a time
- No "while I am here" edits
- No bundled refactoring
- No speculative changes
- No edits without evidence
- If root cause requires architectural discussion, stop and raise it before coding
- If this is the third failed fix attempt for this test, stop and raise an architectural concern before trying again

Any code, configuration, test, script, or workflow change invalidates previous completion evidence.

After a change, Phase B must later restart from Step 3.

---

### Step 2.5, Verify the Single Test

Run the single test again.

```powershell
pwsh ./build.ps1 RunTest "<TestName>"
```

If it passes:
- Remove it from the current failing list
- Continue to the next failing test

If it fails:
- Return to Step 2.2 with the new evidence
- Form a new evidenced hypothesis
- Do not stack fixes

When all failing tests from the current list pass, continue to Step 3.

---

## Step 3, Run All Unit Tests

Run all unit tests.

```powershell
pwsh ./build.ps1 Test
```

If all pass:
- Record the result in the verification ledger
- Capture the passing output
- Continue to Step 4

If any fail:
- Record the failure in the verification ledger
- Return to Step 1 with the new failing list

Required evidence before Step 4:
- Suite name
- Total tests run
- Pass count
- Fail count
- Skip count
- Exit code
- Relevant logs or test output
- Confirmation that the run happened after the last change

---

## Step 4, Run the Original Failing Tests Again

Re-run each test from the original failing list collected in Step 1.

```powershell
pwsh ./build.ps1 RunTest "<TestName>"
```

If all pass:
- Capture the passing output for every originally failing test
- Continue to Step 5

If any fail:
- Return to Step 1

Required evidence before Step 5:
- Test name
- Pass or fail result
- Exit code
- Confirmation that each run happened after the last change

---

## Step 5, Run All Simulated System Tests

Run all simulated system tests.

```powershell
pwsh ./build.ps1 SystemTest_Simulated
```

If all pass:
- Record the result in the verification ledger
- Capture the passing output
- Continue to Step 6

If any fail:
- Record the failure in the verification ledger
- Return to Step 1 with the new failing list

Required evidence before Step 6:
- Suite name
- Total tests run
- Pass count
- Fail count
- Skip count
- Exit code
- Relevant logs or test output
- Confirmation that the run happened after the last change

---

## Step 6, Run All Live System Tests

Run all live system tests.

```powershell
pwsh ./build.ps1 SystemTest_Live
```

If all pass:
- Record the result in the verification ledger
- Capture the passing output
- Continue to Step 7

If any fail:
- Record the failure in the verification ledger
- Return to Step 1 with the new failing list

### Timeout Increases for Live Tests

A live test timeout may be increased **only** if all of the following are true:

1. **The failure is a timeout**, not a logic error, assertion failure, or connectivity problem. Confirm from the test output or OTel log that the process was killed by deadline, not by an exception or wrong value.
2. **The operation is inherently slow against a live system**, for example a large work-item export, a long-running import, or a network-bound call with known latency characteristics.
3. **The new timeout is defensible with evidence**, for example: a log showing the operation took N seconds, a throughput calculation, or a documented SLA for the external system. State the evidence explicitly.
4. **The new value is testable**, meaning the test will still fail if the system regresses to genuinely broken behaviour rather than passing unconditionally.
5. **No simulated or unit test is affected**. Timeout changes apply only to live test configuration and must not widen timeouts in unit or simulated suites.

If any condition is not met, a timeout increase is not the fix. Investigate the real cause at Step 2.2.

When a timeout is increased:
- State the old value, the new value, and the evidence that justifies the change.
- Treat it as a code change: all previous completion evidence is stale and Phase B must restart from Step 3.

Required evidence before Step 7:
- Suite name
- Total tests run
- Pass count
- Fail count
- Skip count
- Exit code
- Relevant logs or test output
- Confirmation that the run happened after the last change

---

## Step 7, Claim Victory

Before claiming victory, evaluate the completion gate.

Completion is valid only if:
- Step 3 passed after the last change
- Step 5 passed after the last change
- Step 6 passed after the last change
- Step 3 output appears in the response
- Step 5 output appears in the response
- Step 6 output appears in the response
- No later failure or change invalidated the evidence

If any condition is false, do not claim victory.

Use the blocked-completion response instead.

Valid completion response must include:
- Verification ledger
- Unit Test output from Step 3
- Simulated Test output from Step 5
- Live Test output from Step 6
- Exit code for each required gate
- Statement that all evidence was produced after the last change

A completion claim without this output is invalid.

---

## Evidence Locations Quick Reference

| Source | Path |
|--------|------|
| Test result artifacts (first) | `TestResults\*.trx` |
| Test stdout/stderr (first) | Captured in test runner console output |
| OTel logs per test | `.output\workingtests\<TestName>\.otel-diagnostics\*-logs.log` |
| OTel traces per test | `.output\workingtests\<TestName>\.otel-diagnostics\*-traces.json` |
| Raw payloads | `.output\workingtests\<TestName>\.otel-diagnostics\inbox\` |
| Package output | `.output\workingtests\<TestName>\<org>\<project>\` |

---

## Red Flags, Return to Step 2.2 Immediately

Return to Step 2.2 if any of these occur:

- "I think the fix is X" without reading a log line
- Proposing a fix before stating root cause with evidence
- Applying a second fix on top of a first that did not work
- Using "build succeeded" as evidence of correctness
- Expressing satisfaction before showing passing test output
- Claiming Step 7 while Step 3, Step 5, or Step 6 evidence is missing
- Treating old passing output as current evidence after a later change
- Leaving the ledger marked as valid after a later failure
- Saying the task only asked for one test, therefore suite verification is unnecessary
- Treating a skipped or inconclusive test as a passing test

---

## Skipped Tests Are Not Passing Tests

A skipped, inconclusive, or ignored test result does not satisfy a verification gate.

Every test counted in the suite must either pass or be genuinely absent from the suite (i.e. not compiled, not registered, not applicable on this platform).

A test that skips at runtime is a test that ran and did not pass.

Skip counts in the evidence output must be zero, or explained and justified, before a gate is considered passed.

### Missing prerequisites must fail, not skip

If a test cannot execute because a required environment variable, credential, external service, or fixture is absent, the test must call `Assert.Fail` (or the equivalent hard failure) — not `Assert.Inconclusive`, `Assert.Ignore`, or any other skip mechanism.

A missing prerequisite is a configuration defect. It means the environment is not set up to run the test. The correct response is a clear test failure, not a quiet skip that hides the gap.

**`Assert.Inconclusive` is banned** unless explicitly operator-approved. The only permitted exception is a live test targeting infrastructure that is explicitly known to be absent in all CI environments (e.g. an on-premises TFS server). Such tests must meet all three conditions:

1. A comment in the test body names the missing infrastructure and explains why no CI environment has it.
2. The suite documentation records the test as intentionally absent from CI.
3. A human operator has approved the exception in writing (e.g. in a PR comment or decision record).

Any use of `Assert.Inconclusive` that does not meet all three conditions is a defect and must be replaced with `Assert.Fail`.
