---
name: nkda-test-promotion
description: Analyses the test suite to identify tests that can be promoted to a faster category (Live→Simulated→Feature→Unit) and retires slower tests that are made redundant by faster equivalents. Runs standalone or as a post-implementation hook.
---

# Skill: Test Promotion

Analyse the test suite to push tests toward faster categories and retire slower tests that duplicate coverage already provided by faster ones.

**Goal:** Fast validation. The inner dev loop must complete in seconds, not minutes.

---

## Invocation Modes

| Mode | How to invoke | Input | Output |
|---|---|---|---|
| **Targeted** | After `speckit.implement` completes | The feature directory or test project touched by the implementation | Promotion report + code changes |
| **Full sweep** | Standalone, run against the entire test suite | Solution root or `tests/` folder | Full promotion report + code changes |

When no path is given, default to the solution root and scan all `tests/` projects.

---

## Priority Hierarchy (fastest → slowest)

| Priority | Category | Markers | Speed |
|----------|----------|---------|-------|
| 1 | Unit Tests | `[TestClass]`/`[TestMethod]`, no `TestCategory` | < 50 ms |
| 2 | Feature Tests | Reqnroll `[Binding]` + `.feature` | < 500 ms |
| 3 | Simulated System Tests | `[TestCategory("SystemTest_Simulated")]` | < 10 s |
| 4 | Live System Tests | `[TestCategory("SystemTest")]` or `[TestCategory("SystemTest_Live")]` | < 60 s |

**Direction of promotion is always upward (4 → 3 → 2 → 1).**

---

## Preconditions

Before executing, read:

- `.agents/guardrails/testing-rules.md` — Test priority hierarchy and anti-patterns
- `.agents/guardrails/coding-standards.md` — Testability rules (category 6)

---

## Workflow

### Phase 1 — Discovery

1. **Enumerate all test files** in the target scope (`tests/**/*Tests.cs`, `tests/**/*Steps.cs`).
2. **Classify each test** by its current category (Unit / Feature / Simulated / Live) using markers:
   - `[TestCategory("SystemTest")]` or `[TestCategory("SystemTest_Live")]` → Live
   - `[TestCategory("SystemTest_Simulated")]` → Simulated
   - `[Binding]` class with associated `.feature` → Feature
   - Plain `[TestMethod]` with no system category → Unit
3. **Build a coverage map**: for each test, identify what behaviour/logic it validates (method name, scenario name, step text).

### Phase 2 — Promotion Analysis

For each test at level N (where N > 1), ask:

#### Can a Live test (level 4) become Simulated (level 3)?

- **YES if:** The test exercises connector-agnostic pipeline logic and the `Simulated` connector can reproduce the scenario.
- **NO if:** The test specifically validates connector-specific API behaviour (rate limiting, pagination quirks, auth token refresh) that only manifests against the real service.

#### Can a Simulated test (level 3) become a Feature test (level 2)?

- **YES if:** The test validates a single module's behaviour and can be expressed as a Reqnroll scenario with mocked dependencies.
- **NO if:** The test requires the full DI container, multiple modules interacting, or real filesystem I/O to be meaningful.

#### Can a Feature test (level 2) become a Unit test (level 1)?

- **YES if:** The scenario under test is a single method with deterministic input/output and no infrastructure dependency.
- **NO if:** The value of the test lies in validating the interaction contract between multiple components (the BDD scenario itself is the specification).

### Phase 3 — Redundancy Detection

For each test at level N, check if a **faster test at level < N already covers the same assertion**:

1. Compare assertion targets (method under test, state transitions, output artefacts).
2. If a unit test already asserts the same logic that a Feature/Simulated/Live test also asserts, the slower test is a **retirement candidate**.
3. A slower test is NOT redundant if it validates integration behaviour (wiring, DI, serialisation round-trip) that the faster test cannot cover.

### Phase 4 — Report

Produce a structured report:

```markdown
## Test Promotion Report

### Promotions (move to faster category)

| Test | Current Level | Proposed Level | Reason |
|------|---------------|----------------|--------|
| ... | Simulated | Unit | Logic is pure transform with no I/O dependency |

### Retirements (remove — covered by faster tests)

| Test to Retire | Level | Covering Test(s) | Reason |
|----------------|-------|-------------------|--------|
| ... | Live | FooTests.Bar_WhenX_ReturnsY | Same assertion, no integration value |

### No Action (correctly placed)

| Test | Level | Reason |
|------|-------|--------|
| ... | Unit | Already at fastest viable level |
```

### Phase 5 — Implementation

**Invocation mode determines whether confirmation is required:**

- **Hook mode** (invoked via `after_implement` in `.specify/extensions.yml`): Apply all promotions and retirements immediately — no confirmation step. The user has implicitly approved all promotion actions by configuring this hook.
- **Manual mode** (invoked directly by the user): Present the Phase 4 report and wait for explicit confirmation before making any changes.

After confirmation (manual mode) or immediately (hook mode):

1. **Promote tests:**
   - Extract the testable logic into a unit-testable shape if needed (extract method, inject dependency).
   - Write the new faster test following naming conventions in `testing-rules.md`.
   - Remove or downgrade the old test marker (e.g., remove `[TestCategory("SystemTest_Simulated")]`).

2. **Retire tests:**
   - Delete the slower test method (or entire class if now empty).
   - Verify the covering test still passes.

3. **Validate:**
   - Run `dotnet build` — must pass.
   - Run `dotnet test` — all remaining tests must pass.
   - Confirm no coverage regression by checking the covering tests exercise the same code paths.

---

## Rules

1. **Never delete a test without a faster replacement that covers the same behaviour.** Retirement requires proof of coverage.
2. **Never promote a test if the promotion loses integration signal.** A Live test that catches real API pagination bugs has value that a unit test cannot replicate.
3. **Prefer extracting logic over rewriting tests.** If a Simulated test exercises complex logic buried inside a large method, extract that logic into a testable class and unit-test it — then simplify the Simulated test to only verify wiring.
4. **Report before acting (manual mode only).** In hook mode, apply changes directly after Phase 4 analysis. In manual mode, always present the promotion report and get user confirmation before making changes.
5. **One promotion at a time.** Each promotion is a self-contained change: new test + old test removal + build + test pass.

---

## Anti-patterns to Flag

- **Pyramid inversion:** More Simulated/Live tests than Unit tests for a module → flag for bulk promotion.
- **Duplicate assertions:** Same `Assert` call appearing in both a Unit test and a Feature test → flag the Feature test for retirement.
- **Mock-heavy Feature tests:** A Feature test that mocks everything and tests no interaction → should be a Unit test.
- **Unit tests with real I/O:** A "Unit" test that touches the filesystem or network → should be reclassified or fixed.

---

## Integration with SpecKit

When run as a post-`speckit.implement` hook:

1. Scope analysis to the test projects touched by the implementation.
2. Focus on newly added tests — were they placed at the fastest viable level?
3. Also scan existing tests in the same area — can any be promoted now that new unit tests exist?
4. Include findings in the implementation session log.
