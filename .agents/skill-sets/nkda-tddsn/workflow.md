# NKDA TDD Safety Net Workflow

## Manual Workflow

1. `nkda-tddsn-assess`
   - uses `nkda-tddsn-assessment`
   - produces `.output/nkda-tddsn/<subsystem>/01-assessment.md`
   - must not modify tests, production code, or architecture docs

2. `nkda-tddsn-design`
   - uses `nkda-tddsn-target-suite-design`
   - uses `nkda-tddsn-architecture-refresh`
   - uses `nkda-tddsn-rebuild-planning`
   - consumes `01-assessment.md`
   - produces `02-target-test-suite.md`, `03-architecture-update.md`, and `04-rebuild-plan.md`
   - must not modify production code

3. `nkda-tddsn-rebuild`
   - uses `nkda-tddsn-test-implementation`
   - uses `nkda-tddsn-code-adjustment`
   - consumes `02-target-test-suite.md` and `04-rebuild-plan.md`
   - updates tests
   - updates production code only where required by the target behavioural tests
   - produces `05-implementation-summary.md`

4. `nkda-tddsn-verify`
   - uses `nkda-tddsn-verification-review`
   - uses `nkda-tddsn-assessment`
   - consumes all prior artefacts
   - runs relevant tests
   - verifies tests, code, architecture docs, and remaining drift risk
   - produces `06-verification.md`

Implementation cannot begin until the target test suite exists.

## Autonomous Workflow

`nkda-tddsn-autonomous` performs all manual phases in order:

1. read the manifest and workflow
2. inspect `.agents/skills/nkd-tdd-assessment`
3. read all available guardrails and subsystem documentation
4. discover production and test files
5. build the subsystem behaviour model
6. assess current tests
7. identify drift risks
8. design the proposed target test suite
9. update or propose subsystem architecture documentation changes
10. create a rebuild plan
11. rebuild tests
12. make minimal production code changes required by the tests
13. run relevant tests using PowerShell
14. verify the final result against the target suite
15. produce all six workflow artefacts separately

The autonomous workflow must not skip assessment, behaviour model construction, target suite design, rebuild planning, or verification.

## Phase Gates

### Behaviour Model Gate

Before designing tests, produce:

- subsystem purpose
- primary behaviours
- state transitions
- external contracts
- failure and rejection behaviours
- boundary conditions
- drift risks

If this cannot be produced with confidence, stop and report uncertainty.

### Target Suite Gate

Before implementation, produce:

- proposed test classes
- proposed test method names
- test type for each test
- protected behaviour for each test
- expected assertions
- keep, rewrite, delete, merge, split, or add decision for each relevant test

Implementation cannot begin until the target test suite exists.

### Minimal Change Gate

Before changing production code, state:

- which failing or missing target test requires the change
- what behaviour is being corrected or enabled
- why the change is minimal
- whether the architecture documentation needs to change

### Verification Gate

Before claiming completion, provide:

- test command used
- test result
- changed files summary
- target suite coverage status
- remaining drift risks
- guardrail violations, if any
