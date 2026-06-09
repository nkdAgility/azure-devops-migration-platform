# Testing Standards

MSTest conventions, test naming, and organisation. See also: [coding-standards.md](../core/coding-standards.md), [module-rules.md](../domains/module-rules.md).

---

## Test Priority Hierarchy

> **Speed Budget is not a classifier.** A test's category is determined solely by its intent and makeup — what it exercises and what dependencies it uses. A test that exceeds its category's speed budget is non-compliant and must be fixed (e.g. inject a time abstraction, replace real delays with fakes) — not moved to a slower category to make the number fit.

Tests are grouped into two parent families that reflect the runtime requirement:

- **`[TestCategory("CodeTest")]`** — runs entirely in-process. No system must be active. Covers priorities 1–3.
- **`[TestCategory("SystemTest")]`** — requires the full system to be active. Covers priorities 4–6.

Every test carries **both** the specific category tag and its parent family tag.

| Priority | Parent | Specific Marker | Intent & Makeup | Speed Budget |
| --- | --- | --- | --- | --- |
| 1 | `CodeTest` | `[TestCategory("UnitTests")]` | Single class in isolation. All deps mocked. No I/O, no real infrastructure. | < 50 ms |
| 2 | `CodeTest` | `[TestCategory("DomainTests")]` | Business behaviour via the internal DSL. Real domain objects, no connectors or infrastructure. | < 500 ms |
| 3 | `CodeTest` | `[TestCategory("IntegrationTests")]` | Real infrastructure components (e.g. retry policies, HTTP clients, serialisers) wired together in-process. No external network or connector. | < 30 s |
| 4 | `SystemTest` | `[TestCategory("SystemTest_Smoke")]` | Critical-path subset of system tests run on every PR. | < 120 s |
| 5 | `SystemTest` | `[TestCategory("SystemTest_Simulated")]` | End-to-end with the `Simulated` connector. No network. | < 60 s |
| 6 | `SystemTest` | `[TestCategory("SystemTest_Live")]` | Requires live ADO/TFS. Environment-gated. | < 300 s |

### Distinguishing categories

| Criterion | UnitTests | DomainTests | IntegrationTests |
| --- | --- | --- | --- |
| Scope | Single class/method in isolation | Business behaviour across collaborating domain objects | Real infrastructure components wired together in-process |
| Dependencies | All mocked/stubbed | Real domain objects via DSL builders/runners/assertions | Real library/framework components (e.g. Polly, HttpClient) — no external network |
| DSL usage | No `DevOpsMigrationPlatform.Testing` usage | Uses the internal DSL library (builders, runners, assertions) | No DSL |
| Arrange style | Direct `new Foo()` + mock setup | Builder pattern (`A.WorkItem().WithField(...)`) | Direct construction of real infrastructure components |
| Assert style | Assert on return value / state of one object | Assert on observable business outcome | Assert on real component behaviour (retry count, response, serialised output) |
| I/O | None | None | None (in-process only; no filesystem, no network) |
| External connectivity | None | None | None |

**Classification rules:**
- If the test references `DevOpsMigrationPlatform.Testing` DSL infrastructure → `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]`
- If the test uses real library/framework components in-process with no external connectivity → `[TestCategory("CodeTest")]` + `[TestCategory("IntegrationTests")]`
- If the test is a single isolated class with all deps mocked → `[TestCategory("CodeTest")]` + `[TestCategory("UnitTests")]`
- If the test is a critical-path subset run on every PR → `[TestCategory("SystemTest")]` + `[TestCategory("SystemTest_Smoke")]`
- If the test exercises a full end-to-end flow with the Simulated connector → `[TestCategory("SystemTest")]` + `[TestCategory("SystemTest_Simulated")]`
- If the test requires live ADO/TFS → `[TestCategory("SystemTest")]` + `[TestCategory("SystemTest_Live")]`

**Principles:** Push tests downward — can it be a unit test? Live tests are a last resort. Simulated replaces live where possible. CI gates run `CodeTest` by default; `SystemTest` requires the full system active.

**Anti-patterns (instant reject):** Integration/Simulated/Live test for logic that could be mocked. New Live test without proving lower level can't cover it. Integration/Simulated/Live outnumbering Unit + Domain tests.

---

## Mandatory Category Tagging (Touch = Tag)

> **HARD GATE — no exceptions, no deferrals, no delegation excuses.**
> A task is **incomplete** if any touched test file contains a test method or test class missing a `[TestCategory]` attribute. The agent MUST apply the correct category before the edit is considered done. "The class already lacked a tag" is not an excuse — it makes the fix more urgent, not optional.

Every time a test file is **created, edited, moved, or touched in any way**, every `[TestMethod]` and `[TestClass]` in that file MUST carry the correct `[TestCategory]` before the change is committed or reported as complete.

| Condition | Required attributes (both must be present) |
| --- | --- |
| Test is isolated unit test (no DSL, no I/O, all deps mocked) | `[TestCategory("CodeTest")]` + `[TestCategory("UnitTests")]` |
| Test uses `DevOpsMigrationPlatform.Testing` DSL | `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` |
| Test uses real infrastructure components in-process, no external connectivity | `[TestCategory("CodeTest")]` + `[TestCategory("IntegrationTests")]` |
| Test is a critical-path smoke subset | `[TestCategory("SystemTest")]` + `[TestCategory("SystemTest_Smoke")]` |
| Test uses `Simulated` connector end-to-end | `[TestCategory("SystemTest")]` + `[TestCategory("SystemTest_Simulated")]` |
| Test targets live ADO/TFS | `[TestCategory("SystemTest")]` + `[TestCategory("SystemTest_Live")]` |

**Enforcement rules — all are blocking, none are optional:**

1. **Missing tag on touch:** If any `[TestMethod]` or `[TestClass]` in a touched file lacks `[TestCategory]`, add the correct tags in the same edit. This applies to every method in the file, not just the method being modified.
2. **Both tags required:** Every test must carry its parent family tag (`CodeTest` or `SystemTest`) AND its specific category tag. A test with only one of the two is non-compliant.
3. **Wrong tag on touch:** If a tag is incorrect (wrong category, old name), correct it in the same edit.
4. **Delegation does not exempt:** If a sub-agent or delegated run added or modified a test, the calling agent is responsible for verifying tags before closing the task. "The delegated run didn't add it" is not a valid completion state.
5. **No partial compliance:** Applying the tag to the new method while leaving existing uncategorised methods in the same file is non-compliant. Fix the whole file.
6. **Category names are canonical:** Only the exact strings `CodeTest`, `UnitTests`, `DomainTests`, `IntegrationTests`, `SystemTest`, `SystemTest_Smoke`, `SystemTest_Simulated`, `SystemTest_Live` are valid. Any other value is non-compliant and must be corrected on contact.
7. The `nkda-testdsl-*` skills must apply `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` to all converted tests.
8. The `nkda-testdsl-refactor` skill must verify and correct all category tags in any file it touches.

**Checklist before marking any test-touching task complete:**

- [ ] Every `[TestMethod]` in every touched file has a `[TestCategory]` with a canonical value.
- [ ] Every `[TestClass]` in every touched file has a `[TestCategory]` with a canonical value (class-level tag is required when all methods share the same category).
- [ ] No old or non-canonical category values remain in touched files.

---

## Mandatory DSL Migration (Touch = Convert)

> **HARD GATE — no exceptions, no deferrals, no delegation excuses.**
> Legacy Reqnroll is a **migration debt**, not an editable test style. The moment you need to
> *change the behaviour* of a legacy `.feature` file or its `*Steps.cs`, that feature family
> MUST be migrated to the internal DSL — you do not edit the old style in place.

**Trigger.** Any change to the *behaviour or scenarios* of a legacy Reqnroll `.feature` file, or
to its `[Binding]`/`[Given]`/`[When]`/`[Then]` step definitions, obligates migration of that
**whole feature family** to the code-first internal DSL before the task is complete. This sits
alongside Touch = Tag: a touched legacy test must be both migrated **and** correctly categorised.

**How.** Migration is performed *only* via the DSL orchestration skill — do not hand-roll it:

```text
nkda-testdsl-autonomous {feature}
```

`{feature}` is the touched `.feature` path, step-file path, family folder, or named family. The
skill runs the full loop (assess → DSL design → extraction → conversion → refactor → verification)
and produces code-first `[TestCategory("DomainTests")]` tests under
`tests/<Project>.Tests/<Area>/<Behaviour>Tests.cs` using the
`tests/DevOpsMigrationPlatform.Testing` DSL library.

**Terminal state.** After migration the legacy `.feature` and `*Steps.cs` for that family are
**removed** (their behaviour now lives in the DSL tests). A family is "migrated" only when no
`.feature`/`[Binding]` artefacts remain for it and the converted tests pass.

**Narrow carve-outs (do NOT require migration):**

1. **Retirement** — *deleting* an obsolete or architecturally-impossible scenario/family outright
   (no replacement behaviour) is a removal, not a change-in-place; record the rationale.
2. **Non-behavioural edits** — fixing a typo/comment with no scenario or step change.
3. **Orphaned feature files** — a `.feature` with **no matching `[Binding]`/`Steps.cs`** that
   generates **no executable tests** is stale *documentation*, not a legacy test. It cannot be
   "migrated" (there is no behaviour to convert). Editing it does not trigger this gate; the
   correct end state is to **delete** the orphan once its intent is captured elsewhere (docs or
   DSL tests), not to keep dead Gherkin.

If you find yourself editing legacy scenario steps to assert new behaviour, **stop and run
`nkda-testdsl-autonomous`** instead.

**Enforcement:** blocking. A task that edits legacy `.feature`/`Steps` behaviour without running
the DSL migration is incomplete, regardless of whether tests pass.

---

## Framework

- Unit runner: MSTest only.
- Code-first behavioural tests: use `tests/DevOpsMigrationPlatform.Testing` internal DSL.
- Legacy BDD: Reqnroll is a migration debt. It remains **only** for families not yet migrated, and
  **any behavioural touch triggers migration** (see *Touch = Convert* above) — it is not an
  editable style.
- New feature behaviour must not be added as `.feature` files unless explicitly approved.
- Do not add new `[Binding]`, `[Given]`, `[When]`, or `[Then]` classes for migrated areas.

### Layer structure

```text
tests/DevOpsMigrationPlatform.Testing/<Domain>/...             ← reusable typed DSL
tests/<Project>.Tests/<Area>/<Behaviour>Tests.cs               ← code-first MSTest behavioural tests
features/<operation>/...                                       ← legacy Reqnroll feature files pending migration only
tests/<Project>.Tests/<Area>/<Feature>Steps.cs                 ← legacy Reqnroll step definitions pending migration only
```

---

## Naming

- Unit test class: `<ClassName>Tests`
- Unit test method: `<MethodName>_<Condition>_<ExpectedResult>` (PascalCase)
- Legacy-only Reqnroll step definition class: `<FeatureName>Steps` (maps to `Feature:` name, PascalCase, `Steps` suffix)
- Legacy-only Reqnroll step methods: PascalCase action name. `[Given]`/`[When]`/`[Then]` attribute string must exactly match `.feature` step text.

---

## Step Definitions

Legacy-only (`Reqnroll` / `[Binding]`) guidance:

- Constructor-injected shared context object for communication between steps.
- `(.*)` for string captures, `(\d+)` for integers.
- Steps MUST NOT call each other directly — communicate via context only.
- No new `[Binding]` step definitions in migrated areas.

---

## Mock Rules

- `Mock<T>` (Moq) or hand-written fakes for infrastructure interfaces.
- Never use real `FileSystemArtefactStore` in unit tests.
- Never use live Azure DevOps in unit tests.
- Real filesystem → `[TestCategory("SystemTest_Simulated")]`.

---

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

---

## Prohibited Patterns

- `Assert.IsTrue(true)` or empty step bodies.
- `Thread.Sleep` in steps.
- Static mutable fields on `[Binding]` classes.
- Steps calling each other directly.
- Catching all exceptions without re-asserting.
- Steps depending on execution order beyond Given/When/Then.
- `Assert.Inconclusive` in any test unless explicitly operator-approved (see below).

---

## Assert.Inconclusive Is Banned

`Assert.Inconclusive` (and any equivalent — `Assert.Ignore`, `Assert.Skip`, or any mechanism that produces a skipped/inconclusive result) **must not appear in any test** without explicit operator approval.

A skipped test is not a passing test. It provides no verification signal and silently hides defects.

### Missing prerequisites must use Assert.Fail

If a test cannot execute because a required environment variable, credential, external service, or fixture is absent, the test must call `Assert.Fail` with a clear message naming the missing prerequisite.

Do not use `Assert.Inconclusive` to hide a missing configuration. The absence of a required prerequisite is a defect in the environment. `Assert.Fail` makes that defect visible.

### Operator-approved exceptions

The **only** permitted use of `Assert.Inconclusive` is for a live test that targets infrastructure that is explicitly known to be absent in all CI environments (for example, an on-premises TFS server with no hosted equivalent).

To use `Assert.Inconclusive` for this purpose, all three conditions must be met:

1. A comment in the test body names the missing infrastructure and states why no CI environment has it.
2. The suite documentation records the test as intentionally absent from CI.
3. A human operator has approved the exception in writing (e.g. in a PR comment or decision record).

If any condition is not met, the `Assert.Inconclusive` call is a defect and must be replaced with `Assert.Fail`.

---

## Diagnosing System Test Failures

When a `SystemTest_Simulated`, `SystemTest`, or `SystemTest_Live` test fails, every CLI and agent process spawned by that test writes full OTel file diagnostics to:

```text
.output/workingtests/{TestName}/.otel-diagnostics/
```

where `{TestName}` is the **exact MSTest method name** (e.g. `QueueCommand_WithExportMode_ExitsZero_AndWritesRevisionFiles`), and the path is relative to the **repository root**.

**Absolute path pattern:**

```text
<repo-root>\.output\workingtests\<TestMethodName>\.otel-diagnostics\
```

`CliRunner.TestWorkingFolder` is `.output\workingtests`. Each spawned process writes logs, traces, and metrics for that test run into the `.otel-diagnostics` folder above.

When reproducing the same simulated or live run with `--diagnostics`, also inspect `.output/workingtests/{TestMethodName}/.otel-diagnostics/inbox/` for raw `bootstrap`, `telemetry`, `progress-{module}-{stage}`, and diagnostics payloads.

The contributor-facing debugging workflow lives in [docs/testing-guide.md](../../../docs/testing-guide.md). This guardrail only establishes the required location of the evidence and the expectation that debugging analysis uses it.

---

## CLI Feature → System Test Requirement

Every CLI command MUST have `[TestCategory("SystemTest")]` test that:

1. Guards on env vars (calls `Assert.Fail` with a clear message if absent — see Assert.Inconclusive Is Banned above).
2. Exercises the feature against real/simulated system.
3. Asserts observable output (files, zip, records).
4. Co-located in `.Tests` project under `Commands/`.

| CLI command | Test class | Assertion |
| --- | --- | --- |
| `queue` (`Mode: Export`, ADO source) | `AzureDevOpsExportCommandTests` | `WorkItems/` directory + zip |
| `queue` (`Mode: Inventory`) | `InventoryCommandTests` | Records against live ADO |
| `migrate` (Simulated) | `SimulatedMigrationCommandTests` | `WorkItems/`, `Checkpoints/`, `Logs/progress.jsonl` |
| `queue` (`Mode: Export`, TFS source) | (environment-gated: requires live TFS) | — |


