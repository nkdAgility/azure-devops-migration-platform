# Tests — Directory Rules

## ⛔ Blocking rules

1. **Touch = Tag.** Every `[TestMethod]` and `[TestClass]` in any file you touch carries BOTH tags: parent family (`CodeTest` | `SystemTest`) AND specific category (`UnitTests` | `DomainTests` | `IntegrationTests` | `SystemTest_Smoke` | `SystemTest_Simulated` | `SystemTest_Live`). Fix the whole file, not just your method. Only those exact strings are valid.
2. **Never self-assign `SystemTest_Smoke`** — operator-designated only.
3. **Touch = Convert.** Changing behaviour of a legacy Reqnroll `.feature`/`*Steps.cs` obligates migrating that whole family to the internal DSL via `nkda-testdsl-autonomous {feature}` — do not edit legacy style in place.
4. **`Assert.Inconclusive` is banned.** Missing prerequisites call `Assert.Fail` naming the prerequisite. No `[Ignore]` in committed code.
5. **No vacuous assertions** — `Assert.IsTrue(true)`, `count >= 0`, sole `IsNotNull(result)`, or assert-free bodies are violations.
6. **Push tests down.** Prefer UnitTests → DomainTests → IntegrationTests → Simulated → Live; a Live test requires proof no lower layer can cover it.
7. Mocks for infrastructure interfaces in unit tests — never real `FileSystemArtefactStore`, never live Azure DevOps.
8. System-test debugging evidence lives at `.output/workingtests/<TestMethodName>/.otel-diagnostics/` — read it before proposing fixes; follow `docs/failing-tests-workflow.md` for any failing test.

## Authority

- Rules: `.agents/20-guardrails/workflow/testing-rules.md`, `.agents/20-guardrails/workflow/failing-tests-workflow.md`
- Explanation: `docs/testing-guide.md`, `docs/failing-tests-workflow.md`
