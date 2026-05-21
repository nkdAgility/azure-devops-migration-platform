---
name: nkda-debugtests
description: Mandatory procedure for diagnosing and fixing failing tests. Runs a structured inner loop per failing test (validate → read evidence → state root cause → fix → verify), then progressively widens to unit, simulated, and live suites. Does not stop until all suites are green.
---

# NKDA Debug Tests

## When to Use

Use this skill whenever:
- Any test is failing
- A fix was applied but tests have not been verified
- A test suite run returned non-zero exit code
- You are about to claim "tests pass" without fresh output

## The Full Procedure

The complete step-by-step workflow is in:

```
.agents/20-guardrails/workflow/failing-tests-workflow.md
```

**Read it. Follow every step. Do not improvise.**

---

## Summary (not a substitute — read the full workflow)

```
1. Run suite → collect failing test list
2. For each failing test:
   2.1  Validate it still fails (run in isolation)
   2.2  Read .otel-diagnostics logs and test output — DO NOT GUESS
   2.3  State root cause with evidence, then apply minimal fix
   2.4  Run that one test — passes → next; fails → back to 2.2
3. Run all Unit Tests          → any fail → back to 1
4. Run original failing tests  → any fail → back to 1
5. Run all Simulated tests     → any fail → back to 1
6. Run all Live tests          → any fail → back to 1
7. Claim victory with output as evidence
```

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

- **Build passing ≠ fix working.** Always run the test.
- **No root cause stated = no fix allowed.** Read evidence first.
- **3+ failed fixes = stop.** Raise architectural concern before trying again.
- **No completion claim without test output in the response.**

---

## Related

- Full workflow: `.agents/20-guardrails/workflow/failing-tests-workflow.md`
- Verification gate: `.agents/skills/verification-before-completion/SKILL.md`
- Deep debugging: `.agents/skills/systematic-debugging/SKILL.md`
- Mandatory test-fix loop: `.agents/20-guardrails/workflow/testing-rules.md` (Mandatory Test-Fix Loop section)
