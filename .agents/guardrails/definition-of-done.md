# Definition of Done

Every unit of work (feature, task, bugfix, refactor) must satisfy **all** of the following criteria before it can be declared complete. There are zero exceptions.

---

## 1. Build

- `dotnet clean && dotnet build --no-incremental` succeeds with **0 errors and 0 warnings treated as errors**.
- Both Debug and Release configurations must build clean.
- The `build.ps1 install` script must complete without error.

## 2. Tests — All Green, No Exceptions

| Rule | Detail |
|------|--------|
| **All tests run** | Every test in the solution is executed. No test may be excluded from the run. |
| **All tests pass** | Exit code 0. Zero failures, zero errors. |
| **No `Assert.Inconclusive`** | `Assert.Inconclusive()` is treated as a **build-breaking error**. Every test must assert a real outcome. The fix is always to **implement the assertion**. A test may only be removed if it is genuinely invalid (e.g. tests a deleted feature or contradicts the spec) — never to avoid implementation work. Only a human may decide to remove a test. |
| **No `@ignore` tag** | Gherkin `@ignore` tags are forbidden in committed code. They may be used **temporarily within a single editing session** to isolate a problem, but must be removed before the work is declared done. |
| **No `[Ignore]` attribute** | MSTest `[Ignore]` attributes are forbidden in committed code. Same temporary-use-only rule as `@ignore`. |
| **No `throw new NotImplementedException()`** | Stubs are permitted only within a single session. Every reachable code path must have a real implementation before done. |
| **No hanging tests** | Every test must complete within a reasonable time. Infinite loops, unbounded waits, and clock-racing conditions (e.g. comparing against a live `DateTime.UtcNow` in a tight loop) are bugs. |

### Temporary Isolation (Session-Only)

During active development, you **may** temporarily use `@ignore`, `[Ignore]`, or `Assert.Inconclusive` to isolate a subset of tests while debugging. This is a valid workflow technique. However:

- These markers must be removed before the session ends.
- Code containing these markers must never be committed.
- A build that contains any of these markers is **not done**.

## 3. Scenario Execution

- At least one scenario configuration (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) must be executed via a `.vscode/launch.json` debug profile.
- The run must complete without errors and produce expected observable output.

## 4. Code Quality

- No `throw new NotSupportedException("... not yet implemented")` in reachable code paths.
- No `.Result` or `.Wait()` on `Task`.
- No hard-coded secrets, credentials, or connection strings.
- No floating NuGet version ranges (`Version="*"`).
- All coding standards in [coding-standards.md](./coding-standards.md) are satisfied.

## 5. Documentation

- Every canonical doc named in any doc-task in `tasks.md` is updated.
- CLI changes have a corresponding `.vscode/launch.json` entry.
- Deployable Host changes are covered in `build.ps1`.

## 6. Compliance Review

After completing the work, re-read every relevant doc referenced by the guardrails. Check each change against the docs line by line. If any non-compliance is found, fix it and repeat. Only when the review loop finds zero violations is the task done.

---

## Summary Checklist

```
[ ] dotnet clean && dotnet build --no-incremental — 0 errors
[ ] dotnet test — all tests pass, 0 failures, 0 skipped-by-marker
[ ] No Assert.Inconclusive in any test
[ ] No @ignore or [Ignore] in committed code
[ ] No NotImplementedException in reachable code
[ ] No hanging tests
[ ] build.ps1 install — passes
[ ] Scenario config executed successfully
[ ] Docs updated where required
[ ] Compliance review loop completed with 0 findings
```
