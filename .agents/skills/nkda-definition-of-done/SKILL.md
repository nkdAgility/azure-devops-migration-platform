---
name: nkda-definition-of-done
description: Validates that a completed unit of work meets every criterion in the Definition of Done — build, tests, code quality, connector coverage, documentation, and compliance review. Fails if any redline is violated.
---

# Skill: Definition of Done Validation

Validate that a completed unit of work satisfies every criterion defined in `.agents/guardrails/definition-of-done.md`. This skill is deterministic and idempotent — running it twice on the same input produces the same output.

**Invocation modes:**

| Mode | How to invoke | Input | Output |
|---|---|---|---|
| **Document** | Run against a markdown file (spec, plan, tasks) | File path or currently open file | Injects/rewrites `## Definition of Done` section in the document |
| **Codebase** | Run against a project, folder, or the full solution | Project path, folder path, or solution root | Produces a Definition of Done Audit Report with pass/fail per criterion |
| **SpecKit hook** | Automatic via `after_implement` in `.specify/extensions.yml` | The feature directory being implemented | Same as Codebase mode against the solution |

When invoked manually, pass the target file or folder path. If no path is given, use the solution root. The skill auto-detects the mode:

1. If the target is a `.md` file → **Document mode**.
2. If the target is a `.cs` file, `.csproj`, `.slnx`, folder, or the user names a feature → **Codebase mode**.
3. If invoked by SpecKit hook → **Codebase mode** against the solution root.

---

## Role

When this skill is active, inspect the target for compliance with every Definition of Done criterion and **fail explicitly** if any redline is violated.

- **Document mode:** Inject or rewrite the `## Definition of Done` section to document which criteria are met and which are not. Useful for annotating a spec or plan with readiness status.
- **Codebase mode:** Execute checks against the actual solution — build, test, code scan, file inspection — and produce an audit report listing pass/fail status per criterion. In manual mode, does not fix violations — reports findings with specific details and required actions. In hook mode (invoked via `after_implement`), auto-fixes mechanical violations (see Step 0.5) and fails hard on violations that require human intervention.

---

## Preconditions

Before executing, read the following context files:

- `.agents/guardrails/definition-of-done.md` — The canonical criteria
- `.agents/guardrails/coding-standards.md` — Code quality rules and validation checklist
- `.agents/guardrails/testing-rules.md` — Test framework rules
- `.agents/guardrails/architecture-boundaries.md` — Architecture constraints

These files establish the redlines that must not be crossed.

---

## The Seven Gates

Every unit of work must pass all seven gates. A failure in any gate means the work is **not done**.

| # | Gate | What it checks |
|---|---|---|
| 1 | **Build** | Clean build succeeds with zero errors |
| 2 | **Tests** | All tests pass, no forbidden markers |
| 3 | **Scenario Execution** | At least one scenario config runs successfully |
| 4 | **Code Quality** | No stubs, no sync-over-async, no secrets, no floating versions |
| 5 | **Connector Coverage** | All three connectors implemented where applicable |
| 6 | **Documentation** | Canonical docs updated, launch.json entries present |
| 7 | **Compliance Review** | Changes match docs line-by-line, no undocumented behaviour |

---

## Execution Steps

Execute the following steps in order. Do not skip steps. Record every finding. All steps apply in both modes unless marked otherwise.

### Step 0 — Detect Mode

1. Examine the target path or context.
2. If the target is a `.md` file, or the user asks to annotate a document → enter **Document mode**.
3. If the target is a `.cs` file, `.csproj`, `.slnx`, folder, or the skill was invoked by a SpecKit hook → enter **Codebase mode**.
4. If invoked by a SpecKit `after_implement` hook → also set **Hook mode = true**.
5. Record the mode.

### Step 0.5 — Hook Mode Auto-Fix (Codebase + Hook mode only)

**Skip this step in manual mode. Only execute when Hook mode = true.**

Auto-fix mechanical violations that have unambiguous correct actions. Apply fixes silently, then record each fix in the audit report under a "Auto-Fixed" section.

| Violation | Auto-Fix Action |
|---|---|
| `Assert.Inconclusive()` found in test | Delete the entire test method (or the enclosing class if it becomes empty). These must not exist per guardrails. |
| `[Ignore]` attribute on a test class or method | Remove the attribute. Forbidden in committed code. |
| `@ignore` tag in a Gherkin `.feature` file | Remove the `@ignore` tag from the scenario. Forbidden in committed code. |

**Do NOT auto-fix these** — they require human implementation or judgment:

| Violation | Reason |
|---|---|
| `throw new NotImplementedException()` | Requires actual implementation, not deletion |
| Missing connector implementation | Requires writing real code |
| Build errors | Require developer diagnosis |
| Missing documentation | Require content authoring |
| Missing `launch.json` entries | Require developer decision |

After auto-fixing, run `dotnet build` and `dotnet test` to confirm the fixes did not break anything. If they do break, restore the auto-fix (the violation was load-bearing — flag it as FAIL requiring manual resolution).

Record all auto-fixes in the report under:

```markdown
### Auto-Fixed (Hook Mode)

| File | Line | Violation | Action Taken |
|---|---|---|---|
| ... | ... | `Assert.Inconclusive()` | Deleted test method `MethodName` |
```

### Step 1 — Gate 1: Build

**Codebase mode:**

1. Run `dotnet clean && dotnet build --no-incremental` against the solution.
2. Capture exit code and output.
3. **PASS** if exit code is 0 with zero errors.
4. **FAIL** if any errors or non-zero exit code. Record the error summary.

**Document mode:**

1. Check if the document mentions build status or contains a build checklist.
2. If a tasks.md, check whether the build task is marked `[X]`.
3. Record status as **Documented/Verified**, **Documented/Unverified**, or **Not mentioned**.

### Step 2 — Gate 2: Tests

**Codebase mode:**

1. Run `dotnet test` against the solution.
2. Capture exit code, pass count, fail count, skip count.
3. **PASS** if exit code is 0, zero failures, zero errors.
4. **FAIL** if any test fails or errors.
5. Scan the codebase for forbidden test markers:

   | Pattern | Search | Violation |
   |---|---|---|
   | `Assert.Inconclusive` | `grep -r "Assert.Inconclusive" tests/` | Build-breaking — must implement or delete |
   | `@ignore` | `grep -r "@ignore" features/` | Forbidden in committed code |
   | `[Ignore]` | `grep -r "\[Ignore\]" tests/` | Forbidden in committed code |
   | `throw new NotImplementedException()` | `grep -r "NotImplementedException" src/ tests/` | Stub — must implement |

6. **FAIL** if any forbidden marker is found. Record file + line for each.

**Document mode:**

1. Check if the document references test results or contains a test checklist.
2. Record status as **Documented/Verified**, **Documented/Unverified**, or **Not mentioned**.

### Step 3 — Gate 3: Scenario Execution

**Codebase mode:**

1. Check if a `.vscode/launch.json` exists and contains at least one scenario configuration profile.
2. If the current feature added or changed a CLI command, verify a matching `launch.json` entry exists.
3. Record whether at least one scenario was executed in the current session (check session logs if available).
4. **PASS** if scenario execution is confirmed.
5. **WARN** if scenario profiles exist but execution is unconfirmed (cannot be fully automated — note for human verification).

**Document mode:**

1. Check if the document mentions scenario execution or references `scenarios/` configs.
2. Record status.

### Step 4 — Gate 4: Code Quality

**Codebase mode:**

Scan the changed files (or full scope if standalone) for the following violations:

| # | Check | Pattern / Method | Severity |
|---|---|---|---|
| 4.1 | No `NotImplementedException` stubs | Search for `throw new NotImplementedException()` in `src/` and `tests/` | **CRITICAL** |
| 4.2 | No `NotSupportedException("not yet")` | Search for `NotSupportedException` with "not yet" messages | **CRITICAL** |
| 4.3 | No `return default` placeholders | Search for methods with only `return default;` or `return null;` | **HIGH** |
| 4.4 | No sync-over-async | Search for `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` on `Task`/`ValueTask` | **CRITICAL** |
| 4.5 | No discarded `CancellationToken` | Search for methods accepting `CancellationToken` that don't forward it | **HIGH** |
| 4.6 | No hard-coded secrets | Search for patterns like `"password"`, `"secret"`, `"connectionstring"` in string literals | **CRITICAL** |
| 4.7 | No floating NuGet versions | Check `Directory.Packages.props` and `.csproj` files for `Version="*"` or `Version="Latest"` | **HIGH** |
| 4.8 | No TODO-implement comments | Search for `TODO: implement`, `TODO: not yet`, `HACK:` without tracked issue | **MEDIUM** |
| 4.9 | Vulnerability scan | Run `dotnet list package --vulnerable` and check for HIGH/CRITICAL | **HIGH** |

For each violation found, record: file, line number, violation type, and the offending code snippet.

**FAIL** if any CRITICAL violation is found. **WARN** for HIGH/MEDIUM.

**Document mode:**

1. Check if the document addresses code quality criteria.
2. Record status.

### Step 5 — Gate 5: Connector Coverage

**Codebase mode:**

1. Identify all features that interact with source or target systems (classes implementing `IModule`, `IWorkItemRevisionSource`, etc.).
2. For each feature, check that implementations exist for:
   - **Simulated** — always required
   - **AzureDevOpsServices** — always required
   - **TeamFoundationServer** — required unless API does not support (with documented exemption)
3. Verify no implementation contains `NotImplementedException` or equivalent stub.
4. **PASS** if all connectors are covered.
5. **FAIL** if any connector is missing or stubbed.
6. If the feature does not interact with source/target systems → **PASS (N/A)**.

**Document mode:**

1. Check if the document contains a `## Connector Coverage` section (as written by the connector-coverage-check skill).
2. If present, verify verdict is PASS.
3. Record status.

### Step 6 — Gate 6: Documentation

**Codebase mode:**

1. If a `tasks.md` exists in the feature directory, identify all doc-tasks (tasks referencing `docs/` or `.agents/context/` files).
2. For each doc-task marked `[X]`, verify the referenced file was actually modified (check git diff or file timestamps).
3. Check if the feature adds/changes a CLI command → verify `.vscode/launch.json` has a matching entry.
4. Check if the feature adds/changes a deployable Host → verify `build.ps1` covers it.
5. If a CLI-exposed feature was added → verify a `[TestCategory("SystemTest")]` test exists.
6. **PASS** if all documentation requirements are met.
7. **FAIL** if any doc-task is incomplete or launch.json/build.ps1 entries are missing.

**Document mode:**

1. Check if the document references documentation status.
2. Record status.

### Step 7 — Gate 7: Compliance Review

**Codebase mode:**

1. Identify the `docs/` and `.agents/context/` files relevant to the changes.
2. For each relevant doc file, compare the implementation against what the doc specifies:
   - Does the implementation match the documented behaviour?
   - Does it add any undocumented parameters, options, commands, or behaviour?
   - Does it omit anything the documentation requires?
3. Check `specs/<feature>/discrepancies.md` — all entries must be `Resolved` or `N/A`.
4. **PASS** if compliance review finds zero violations.
5. **FAIL** if any non-compliance is found. Record the specific doc, section, and discrepancy.

**Document mode:**

1. Check if the document addresses compliance review.
2. Record status.

---

## Report Format

### Codebase Output Structure

```markdown
## Definition of Done — Audit Report

**Scope:** `<solution, project, or feature path>`
**Date:** `<date>`
**Feature:** `<feature name or branch>`

### Gate Summary

| # | Gate | Status | Detail |
|---|---|---|---|
| 1 | Build | ✅ PASS / ❌ FAIL | <summary> |
| 2 | Tests | ✅ PASS / ❌ FAIL | <pass count> passed, <fail count> failed |
| 3 | Scenario Execution | ✅ PASS / ⚠️ UNCONFIRMED | <detail> |
| 4 | Code Quality | ✅ PASS / ❌ FAIL | <N> violations found |
| 5 | Connector Coverage | ✅ PASS / ❌ FAIL / ✅ N/A | <detail> |
| 6 | Documentation | ✅ PASS / ❌ FAIL | <detail> |
| 7 | Compliance Review | ✅ PASS / ❌ FAIL | <detail> |

### Overall Verdict

**DONE** — All seven gates passed. The work is complete.
— or —
**NOT DONE** — <N> gate(s) failed. See details below.

### Gate 1: Build Details
<build output summary or "Clean build — 0 errors">

### Gate 2: Test Details
<test summary table>

| Metric | Value |
|---|---|
| Total tests | <N> |
| Passed | <N> |
| Failed | <N> |
| Skipped | <N> |

**Forbidden markers found:**

| File | Line | Marker | Required Action |
|---|---|---|---|
| ... | ... | ... | ... |

(Empty table if none found)

### Gate 3: Scenario Execution Details
<scenario execution status>

### Gate 4: Code Quality Details

**Violations found:**

| # | File | Line | Check | Severity | Code Snippet |
|---|---|---|---|---|---|
| 1 | ... | ... | ... | CRITICAL/HIGH/MEDIUM | `<snippet>` |

(Empty table if none found)

**Vulnerability scan:**
<output of `dotnet list package --vulnerable` or "No vulnerabilities found">

### Gate 5: Connector Coverage Details
<connector coverage summary — reference connector-coverage-check skill output if available>

### Gate 6: Documentation Details

| Doc Task | File | Status |
|---|---|---|
| ... | `docs/xxx.md` | ✅ Updated / ❌ Not updated |

| Check | Status |
|---|---|
| CLI command → launch.json | ✅ Present / ❌ Missing / N/A |
| Host → build.ps1 | ✅ Covered / ❌ Missing / N/A |
| CLI feature → SystemTest | ✅ Present / ❌ Missing / N/A |

### Gate 7: Compliance Review Details

| Doc File | Section | Status | Discrepancy |
|---|---|---|---|
| ... | ... | ✅ Compliant / ❌ Non-compliant | <detail if non-compliant> |

**Discrepancies file:** `<path>` — All entries Resolved/N/A: ✅ / ❌

### Required Actions

Prioritised list of actions needed to reach DONE:

1. **[CRITICAL]** <action> (Gate <N>)
2. **[HIGH]** <action> (Gate <N>)
3. **[MEDIUM]** <action> (Gate <N>)
```

### Document Output Structure

The `## Definition of Done` section MUST follow this structure:

```markdown
## Definition of Done

### Status: DONE / NOT DONE

| # | Gate | Status | Notes |
|---|---|---|---|
| 1 | Build | ✅ / ❌ / ⬜ | <notes> |
| 2 | Tests | ✅ / ❌ / ⬜ | <notes> |
| 3 | Scenario Execution | ✅ / ❌ / ⬜ | <notes> |
| 4 | Code Quality | ✅ / ❌ / ⬜ | <notes> |
| 5 | Connector Coverage | ✅ / ❌ / ⬜ / N/A | <notes> |
| 6 | Documentation | ✅ / ❌ / ⬜ | <notes> |
| 7 | Compliance Review | ✅ / ❌ / ⬜ | <notes> |

Legend: ✅ = Pass, ❌ = Fail, ⬜ = Not yet verified

### Gaps (if any)

| Gate | Issue | Required Action |
|---|---|---|
| ... | ... | ... |
```

---

## Enforcement Summary

| Condition | Action |
|---|---|
| No `## Definition of Done` section (Document mode) | Create it |
| Section exists but incomplete | Amend with current status |
| Section exists and all gates pass | No modification (idempotent) |
| Build fails | **FAIL** — Gate 1 |
| Any test fails or forbidden marker found | **FAIL** — Gate 2 |
| No scenario execution confirmed | **WARN** — Gate 3 (human must verify) |
| `NotImplementedException` in reachable code | **FAIL** — Gate 4 (CRITICAL) |
| `.Result` or `.Wait()` on Task | **FAIL** — Gate 4 (CRITICAL) |
| Hard-coded secret found | **FAIL** — Gate 4 (CRITICAL) |
| Floating NuGet version | **FAIL** — Gate 4 (HIGH) |
| Known HIGH/CRITICAL vulnerability unaddressed | **FAIL** — Gate 4 (HIGH) |
| Connector missing or stubbed | **FAIL** — Gate 5 |
| Doc-task marked done but file not updated | **FAIL** — Gate 6 |
| CLI command without launch.json entry | **FAIL** — Gate 6 |
| Implementation adds undocumented behaviour | **FAIL** — Gate 7 |
| Discrepancies not all Resolved/N/A | **FAIL** — Gate 7 |

---

## Completion Criteria

### Both modes

- [ ] All seven gates have been evaluated.
- [ ] Every gate has an explicit PASS, FAIL, WARN, or N/A status.
- [ ] Every FAIL has a specific detail and required action.
- [ ] Overall verdict is explicitly stated (DONE or NOT DONE).

### Document mode only

- [ ] Definition of Done section follows the mandatory structure.
- [ ] Existing correct content is preserved (idempotent).
- [ ] No TODO placeholders remain in the section.

### Codebase mode only

- [ ] Build was executed and result captured.
- [ ] Tests were executed and result captured.
- [ ] Code quality scan was performed on all changed files (or full scope).
- [ ] Connector coverage was assessed (or marked N/A with justification).
- [ ] Documentation completeness was verified against tasks.md.
- [ ] Compliance review compared implementation to relevant docs.
- [ ] Required Actions list is prioritised (Critical > High > Medium).
- [ ] No source files were modified without explicit user instruction (manual mode) or outside the auto-fix list (hook mode).

The skill is not complete until all criteria are checked. Any unchecked criterion is a failure.
