# Testing Rules

Enforced testing constraints. The contributor explanation — category selection
guidance, distinguishing-criteria tables, naming conventions, DSL layer
structure, and diagnostics walkthrough — lives in
[`docs/testing-guide.md`](../../../docs/testing-guide.md).
See also: [coding-standards.md](../core/coding-standards.md), [module-rules.md](../domains/module-rules.md).

---

## Canonical Categories and Dual Tagging

Every test carries **both** its parent family tag and its specific category tag.
Only these exact strings are valid: `CodeTest`, `UnitTests`, `DomainTests`,
`IntegrationTests`, `SystemTest`, `SystemTest_Smoke`, `SystemTest_Simulated`,
`SystemTest_Live`.

| Condition | Required attributes (both) | Speed budget |
| --- | --- | --- |
| Isolated unit test (no DSL, no I/O, all deps mocked) | `CodeTest` + `UnitTests` | < 50 ms |
| Uses `DevOpsMigrationPlatform.Testing` DSL | `CodeTest` + `DomainTests` | < 500 ms |
| Real infrastructure components in-process, no external connectivity | `CodeTest` + `IntegrationTests` | < 30 s |
| Critical-path PR subset | `SystemTest` + `SystemTest_Smoke` | < 120 s |
| End-to-end with `Simulated` connector | `SystemTest` + `SystemTest_Simulated` | < 60 s |
| Requires live ADO/TFS (environment-gated) | `SystemTest` + `SystemTest_Live` | < 300 s |

- **Speed Budget is not a classifier.** Category is determined solely by intent and makeup. A test exceeding its budget must be fixed, not moved to a slower category.
- ⛔ **`SystemTest_Smoke` is operator-designated only.** An agent MUST NOT assign it under any circumstances; note candidates in the output summary for the operator instead.
- **Push tests downward.** Live tests are a last resort; Simulated replaces live where possible. Instant reject: Integration/Simulated/Live tests for logic that could be mocked; a new Live test without proof that a lower level cannot cover it; Integration/Simulated/Live outnumbering Unit + Domain tests.

## Touch = Tag (HARD GATE)

Every time a test file is created, edited, moved, or touched in any way, every
`[TestMethod]` and `[TestClass]` in that file MUST carry correct `[TestCategory]`
attributes before the change is complete. All rules are blocking:

1. Missing tag on touch → add the correct tags in the same edit, for every method in the file, not just the one being modified.
2. Both tags required: parent family AND specific category. One of the two is non-compliant.
3. Wrong or non-canonical tag on touch → correct it in the same edit.
4. Delegation does not exempt: the calling agent verifies tags before closing the task.
5. No partial compliance: fix the whole file.
6. The `nkda-testdsl-*` skills must apply `CodeTest` + `DomainTests` to all converted tests; `nkda-testdsl-refactor` must verify and correct tags in any file it touches.

## Touch = Convert (HARD GATE)

Legacy Reqnroll is migration debt, not an editable test style. Any change to the
behaviour or scenarios of a legacy `.feature` file or its
`[Binding]`/`[Given]`/`[When]`/`[Then]` step definitions obligates migration of
that whole feature family to the internal DSL before the task is complete, via:

```text
nkda-testdsl-autonomous {feature}
```

Terminal state: the legacy `.feature` and `*Steps.cs` for that family are removed
and the converted `CodeTest` + `DomainTests` tests pass. Hand-rolled migration is
prohibited.

Carve-outs that do NOT trigger migration: (1) outright retirement of an obsolete
scenario/family with recorded rationale; (2) non-behavioural typo/comment edits;
(3) orphaned `.feature` files with no matching bindings — delete the orphan once
its intent is captured elsewhere.

A task that edits legacy `.feature`/`Steps` behaviour without running the DSL
migration is incomplete, regardless of whether tests pass.

## Framework Rules

- Unit runner: MSTest only.
- Code-first behavioural tests use the `tests/DevOpsMigrationPlatform.Testing` internal DSL.
- New feature behaviour must not be added as `.feature` files unless explicitly approved.
- No new `[Binding]`, `[Given]`, `[When]`, or `[Then]` classes in migrated areas.
- Unit test naming: `<ClassName>Tests` / `<MethodName>_<Condition>_<ExpectedResult>`.

## Mock Rules

- `Mock<T>` (Moq) or hand-written fakes for infrastructure interfaces.
- Never use real `FileSystemArtefactStore` or live Azure DevOps in unit tests.
- Real filesystem → `SystemTest_Simulated`.

## Required Coverage Per Module

| Behaviour | Required |
| --- | --- |
| `ValidateAsync` — valid artefact passes | Yes |
| `ValidateAsync` — missing field fails | Yes |
| `ExportAsync` — writes artefacts via `IArtefactStore` | Yes |
| `ExportAsync` — updates cursor via `IStateStore` | Yes |
| `ImportAsync` — reads one revision at a time (streaming) | Yes |
| `ImportAsync` — uses `IIdentityMappingService` | Yes (if applicable) |
| Cursor resume — re-run starts from cursor position | Yes |
| Cursor resume — first run with no cursor starts from beginning | Yes |

## Prohibited Patterns

- `Assert.IsTrue(true)`, vacuous assertions, or empty step bodies.
- `Thread.Sleep` in steps.
- Static mutable fields on `[Binding]` classes; steps calling each other directly; step order dependencies beyond Given/When/Then.
- Catching all exceptions without re-asserting.
- `Assert.Inconclusive` (or `Assert.Ignore`/skip equivalents) in any test without written operator approval. Missing prerequisites must `Assert.Fail` with a message naming the prerequisite. The only permitted exception is a live test targeting infrastructure absent from all CI environments, and only with all three documented conditions met (in-test comment, suite documentation, written operator approval) — see [`docs/failing-tests-workflow.md`](../../../docs/failing-tests-workflow.md).

## CLI Feature → System Test Requirement

Every CLI command MUST have a `SystemTest`-family test that:

1. Guards on env vars with `Assert.Fail` (never skip) when absent.
2. Exercises the feature against a real or simulated system.
3. Asserts observable output (files, zip, records).
4. Is co-located in the `.Tests` project under `Commands/`.

## System Test Evidence Location

Every CLI/agent process spawned by a system test writes OTel diagnostics to
`.output/workingtests/<TestMethodName>/.otel-diagnostics/` (repo-root relative).
Debugging analysis must use this evidence; the walkthrough lives in
[`docs/testing-guide.md`](../../../docs/testing-guide.md) and
[`docs/failing-tests-workflow.md`](../../../docs/failing-tests-workflow.md).
