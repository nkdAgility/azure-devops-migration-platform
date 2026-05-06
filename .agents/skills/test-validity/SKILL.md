---
name: test-validity
description: Scores every test in the target scope for intrinsic value across five dimensions. Tests classified as WASTE are deleted immediately. Runs before test-promotion so that only valuable tests enter the promotion pipeline.
---

# Skill: Test Validity

Evaluate every test for the value it provides against credible regressions. Delete waste. Flag low-value tests for rewrite. Leave useful and high-value tests untouched.

**Goal:** Ensure every test in the suite earns its place. A test that does not protect against a credible, harmful regression is waste and must be removed.

---

## Invocation Modes

| Mode | How to invoke | Input | Output |
|---|---|---|---|
| **Targeted** | After `speckit.implement` completes (runs before `test-promotion`) | The test projects touched by the implementation | Validity report + deletions |
| **Full sweep** | Standalone, run against the entire test suite | Solution root or `tests/` folder | Full validity report + deletions |

When no path is given, default to the solution root and scan all `tests/` projects.

---

## Preconditions

Before executing, read:

- `.agents/guardrails/testing-rules.md` — Test conventions and anti-patterns
- `.agents/guardrails/coding-standards.md` — Testability rules (category 6)

---

## Scoring Model (0–5 per dimension)

Each test is scored on five dimensions. Score strictly — default to low scores unless clear value is evident.

### 1. Behaviour Value (0–5)

What meaningful behaviour does this test protect?

| Score | Meaning |
|-------|---------|
| 0 | No real behaviour (constant, trivial property, duplication) |
| 3 | Some behaviour but low importance or unclear impact |
| 5 | Clear rule, invariant, or meaningful outcome |

### 2. Credible Failure Mode (0–5)

Could this realistically fail due to a mistake?

| Score | Meaning |
|-------|---------|
| 0 | Only fails if someone intentionally changes the same value |
| 3 | Possible but unlikely or contrived |
| 5 | Common or realistic defect could trigger failure |

### 3. Impact of Failure (0–5)

What happens if this behaviour breaks?

| Score | Meaning |
|-------|---------|
| 0 | No meaningful impact |
| 3 | Minor or localised issue |
| 5 | Significant system, business, or data impact |

### 4. Refactor Resilience (0–5)

Would this test survive a refactor that preserves behaviour?

| Score | Meaning |
|-------|---------|
| 0 | Tightly coupled to implementation, likely to break |
| 3 | Partially coupled |
| 5 | Fully behaviour-focused, stable under refactoring |

### 5. Redundancy (0–5)

Is this already covered elsewhere?

| Score | Meaning |
|-------|---------|
| 0 | Duplicate or overlapping coverage |
| 3 | Partially redundant |
| 5 | Unique coverage |

---

## Classification

| Total (0–25) | Classification | Action |
|---------------|----------------|--------|
| 0–8 | **WASTE** | Delete immediately |
| 9–15 | **LOW VALUE** | Flag for rewrite or merge |
| 16–20 | **USEFUL** | Keep, possibly improve |
| 21–25 | **HIGH VALUE** | Protect strongly |

---

## Workflow

### Phase 1 — Discovery

1. **Enumerate all test files** in the target scope (`tests/**/*Tests.cs`, `tests/**/*Steps.cs`).
2. **Parse each test method** — extract: class name, method name, attributes, assertion targets, mocked dependencies.
3. **Read the production code** each test exercises — understand what behaviour is under test.

### Phase 2 — Scoring

For each test method, evaluate all five dimensions and compute the total score.

**Scoring rules (strict):**

- Do **not** give credit for testing language or runtime guarantees (e.g., asserting that a constructor sets a property that the compiler enforces).
- **Penalise:**
  - Tests of constants or literal values
  - Trivial getter/setter tests
  - `mock.Verify()` without asserting an observable outcome
  - Tests that mirror implementation line-by-line
  - Tests that assert only default/empty state
- **Reward:**
  - Tests of rules, invariants, transformations, edge cases
  - Tests that exercise conditional logic with multiple branches
  - Tests of error handling paths
  - Tests of state machines or workflow transitions

### Phase 3 — Report

Produce a structured report grouped by classification:

```markdown
## Test Validity Report

**Scope**: [target path or "Full suite"]
**Date**: [timestamp]
**Tests evaluated**: [count]

### Summary

| Classification | Count | Action |
|----------------|-------|--------|
| WASTE (0–8) | N | Delete |
| LOW VALUE (9–15) | N | Rewrite/merge |
| USEFUL (16–20) | N | Keep |
| HIGH VALUE (21–25) | N | Protect |

### WASTE — To Delete

| Test | Score | Behaviour Value | Credible Failure | Impact | Refactor Resilience | Redundancy | Reasoning |
|------|-------|-----------------|------------------|--------|---------------------|------------|-----------|
| Class.Method | 5 | 1 | 0 | 1 | 2 | 1 | Tests a constant; only fails if literal is changed |

### LOW VALUE — To Rewrite

| Test | Score | Reasoning | Suggested Improvement |
|------|-------|-----------|-----------------------|
| Class.Method | 12 | Mock-heavy with no outcome assertion | Assert observable state change instead of mock.Verify |

### USEFUL — Keep

| Test | Score | Reasoning |
|------|-------|-----------|
| Class.Method | 18 | Validates transformation rule with edge case coverage |

### HIGH VALUE — Protect

| Test | Score | Reasoning |
|------|-------|-----------|
| Class.Method | 23 | Guards critical invariant; unique coverage of error path |
```

### Phase 4 — Deletion

**WASTE tests are deleted without confirmation.** This is the explicit contract of this skill.

1. For each test classified as WASTE:
   - Delete the test method.
   - If the containing class is now empty, delete the class file.
   - If a `.feature` file's only scenarios map to deleted tests, delete the `.feature` file.
2. For LOW VALUE tests:
   - Add a `// TODO: [test-validity] Score {N}/25 — {reasoning}. Rewrite to test: {suggested behaviour}` comment above the test method.
   - Do **not** delete — leave for manual rewrite.

### Phase 5 — Validate

After deletions:

1. Run `dotnet build` — must pass.
2. Run `dotnet test` — all remaining tests must pass.
3. If build or tests fail after deletion, the deleted test was load-bearing on infrastructure (e.g., shared setup). In that case:
   - Restore the test.
   - Score it as USEFUL (infrastructure dependency) with a note.
   - Re-run build and test to confirm green.

---

## Per-Test Output Format

For each evaluated test, the detailed log must include:

```
- Test Name: [ClassName.MethodName]
- Scores:
  - Behaviour Value: [0–5]
  - Credible Failure Mode: [0–5]
  - Impact of Failure: [0–5]
  - Refactor Resilience: [0–5]
  - Redundancy: [0–5]
- Total Score: [0–25]
- Classification: [WASTE | LOW VALUE | USEFUL | HIGH VALUE]
- Behaviour Under Test: [what production behaviour this guards]
- Credible Failure Scenario: [how a real defect could trigger failure]
- Impact if Broken: [consequence of the behaviour breaking]
- Reasoning: [why this score]
```

---

## Rules

1. **WASTE tests are deleted immediately.** No confirmation step. The scoring model is the gate.
2. **LOW VALUE tests get a TODO comment, not deletion.** They need human attention to rewrite.
3. **Never delete a test that is the only coverage for a behaviour scoring ≥ 3 on Impact of Failure.** If a test scores WASTE overall but is the sole guard on something impactful, bump it to LOW VALUE and flag for rewrite.
4. **Build must stay green.** If a deletion breaks the build, restore and reclassify.
5. **Run this skill BEFORE test-promotion.** Only valuable tests should enter the promotion pipeline.
6. **Score conservatively for integration/system tests.** Tests at higher levels (Simulated, Live) often have hidden integration value — consider whether the test catches wiring or serialisation bugs that unit tests cannot.

---

## Anti-patterns That Score WASTE

- Asserting that a constructor sets a property to a known constant
- Asserting `Assert.IsNotNull(new Foo())` — tests the runtime, not the code
- Asserting that a method returns exactly the mock's `.Returns()` value with no transformation
- Testing auto-generated code (e.g., record equality)
- `[TestMethod]` with no assertions
- Tests that duplicate another test's assertions with only cosmetic differences
- `mock.Verify(x => x.SomeMethod(...))` with no assertion on the outcome of calling that method

### Module & Connector-Specific WASTE Patterns (Instant 0 on Behaviour Value)

The following patterns are unconditional WASTE in any test for a module (`ExportAsync`, `ImportAsync`) or a connector (`ITeamSource`, `IIdentitySource`, `ITeamTarget`, etc.):

| Pattern | Why it is WASTE |
|---------|-----------------|
| `Assert.IsTrue(count >= 0)` | Always true; asserts nothing about whether the module produced any output |
| `Assert.IsTrue(true)` | Tautology; the test body is meaningless |
| `Assert.IsNotNull(result)` as **sole** assertion | Proves the method returned without throwing — not that it did anything useful |
| No `Assert.*` call at all | The test only verifies "does not crash", not correctness |
| Export test with no check that `IArtefactStore` contains the expected path | The module may have written nothing; the test cannot detect it |
| Export test with no check that the written file is non-empty (length > 0 / line count > 0) | The module may have written an empty file; the test cannot detect it |
| Import test with no check on `Simulated*Target` state (e.g., `.Teams.Count`, `.NodesCreated`) | The module may have called nothing on the target; the test cannot detect it |
| Import test that asserts `targetState.Count >= 0` | Always true; equivalent to no assertion |

**Scoring rule:** Any test containing solely one or more of the patterns above — with no additional assertion that checks artefact content or target state — scores **0 on Behaviour Value** and **0 on Credible Failure Mode**, giving a maximum possible total of 15 (all other dimensions at 5). This will almost always produce a WASTE classification. If it does not, override to WASTE explicitly and note the reason.

---

## Integration with SpecKit

When run as a post-`speckit.implement` hook (before `test-promotion`):

1. Scope analysis to the test projects touched by the implementation.
2. Score all tests in those projects — both new and existing.
3. Delete WASTE, annotate LOW VALUE.
4. Pass clean test suite to `test-promotion` for category optimisation.
5. Include the validity report in the implementation session log.
