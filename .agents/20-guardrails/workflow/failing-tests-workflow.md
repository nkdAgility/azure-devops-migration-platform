# Failing Tests Workflow

Mandatory procedure when any test is failing. Follow every step in order. Do not skip steps. Do not claim completion without evidence.

---

## Iron Law

```
NO COMPLETION CLAIM WITHOUT PASSING TEST OUTPUT IN THE RESPONSE
```

Build passing ≠ tests passing. A green build is not evidence. Test output is evidence.

---

## Step 1 — Build the Failing Test List

Run the last relevant test suite and collect every failing test by name.

```powershell
pwsh ./build.ps1 SystemTest_Live
```

Record each failing test as a numbered list. This list drives the rest of the workflow.

**Do not proceed without a concrete list.** "Tests are failing" is not a list.

---

## Step 2 — For Each Failing Test (inner loop)

Repeat steps 2.1–2.4 for every test in the list before advancing to Step 3.

### 2.1 — Validate It Still Fails

Run the single test in isolation. Confirm the failure is real and current, not a stale result.

```powershell
pwsh ./build.ps1 RunTest "<TestName>"
```

If it now passes → remove from list, continue to next test.

### 2.2 — Read the Evidence

**Do not guess. Read first.**

**OTel diagnostics** (written by every spawned CLI/agent process):
```
.output\workingtests\<TestMethodName>\.otel-diagnostics\
```

Files to read:
- `*-logs.log` — structured log output from each process
- `*-traces.json` — span/activity traces
- `*.metrics.json` — counter snapshots
- `inbox\` — raw bootstrap, telemetry, and progress payloads

**Test stdout/stderr** — printed by the test method itself (captured in test runner output).

**Trace the failure path:**
1. What did the test assert?
2. What was the actual value / missing file / wrong count?
3. Which process produced that value? (CLI? Agent? Control plane?)
4. What does the log for that process show at the point of failure?
5. Trace backward up the call stack until you find where the wrong value originates.

This is root cause investigation. Do not skip it.

### 2.3 — State Root Cause and Fix

Before touching any code, write:

> "Root cause: X is happening because Y. Evidence: [log line / file / trace]."

If you cannot state the root cause with evidence, return to 2.2 and gather more.

Apply the **smallest possible single change** that addresses the stated root cause.

Rules:
- One change at a time. No "while I'm here" edits.
- No bundled refactoring.
- If root cause requires architectural discussion → STOP and raise it before coding.
- If this is the 3rd failed fix attempt for this test → STOP. The architecture may be wrong. Discuss before continuing.

### 2.4 — Verify the Fix

Run the single test again:

```powershell
pwsh ./build.ps1 RunTest "<TestName>"
```

- Passes → remove from list, continue to next test.
- Fails → return to 2.2 with the new evidence. Form a new hypothesis. Do not stack fixes.

---

## Step 3 — Run All Unit Tests

```powershell
pwsh ./build.ps1 Test
```

- All pass → continue to Step 4.
- Any fail → return to Step 1 with the new failing list.

---

## Step 4 — Run the Original Failing Tests Again

Re-run each test from the original failing list collected in Step 1:

```powershell
pwsh ./build.ps1 RunTest "<TestName>"
```

- All pass → continue to Step 5.
- Any fail → return to Step 1.

---

## Step 5 — Run All Simulated System Tests

```powershell
pwsh ./build.ps1 SystemTest_Simulated
```

- All pass → continue to Step 6.
- Any fail → return to Step 1.

---

## Step 6 — Run All Live System Tests

```powershell
pwsh ./build.ps1 SystemTest_Live
```

- All pass → continue to Step 7.
- Any fail → return to Step 1.

---

## Step 7 — Claim Victory

State completion with the full test output as evidence. Include:
- Suite name
- Total tests run
- Pass / fail / skip counts
- Exit code

A completion claim without this output is invalid.

---

## Evidence Locations Quick Reference

| Source | Path |
|--------|------|
| OTel logs (per test) | `.output\workingtests\<TestName>\.otel-diagnostics\*-logs.log` |
| OTel traces (per test) | `.output\workingtests\<TestName>\.otel-diagnostics\*-traces.json` |
| Raw payloads | `.output\workingtests\<TestName>\.otel-diagnostics\inbox\` |
| Package output | `.output\workingtests\<TestName>\<org>\<project>\` |
| Test stdout | Captured in test runner console output |

---

## Red Flags — Return to Step 2.2 Immediately

- "I think the fix is X" without reading a log line
- Proposing a fix before stating root cause with evidence
- Applying a second fix on top of a first that didn't work
- "Build succeeded" used as evidence of correctness
- Expressing satisfaction before showing passing test output
