# NKDA Test DSL Workflow

## Manual Workflow

1. `nkda-testdsl-feature-assessment`
   - produces `.output/nkda-testdsl/<feature-family>/01-feature-assessment.md`
   - does not modify code

2. `nkda-testdsl-dsl-design`
   - consumes `01-feature-assessment.md`
   - produces `02-dsl-design.md`
   - does not modify code

3. `nkda-testdsl-extraction` then `nkda-testdsl-feature-conversion`
   - consumes `02-dsl-design.md`
   - bootstraps `tests/DevOpsMigrationPlatform.Testing` if missing (not a blocker)
   - produces `03-extraction-summary.md` and `04-conversion-summary.md`

4. `nkda-testdsl-refactor`
   - consumes conversion outputs
   - produces `05-refactor-summary.md`

5. `nkda-testdsl-verification`
   - consumes all prior outputs
   - produces `06-verification.md`

6. `nkda-testdsl-next-feature-selection`
   - consumes all prior outputs
   - produces `07-next-feature-recommendation.md`

## Autonomous Workflow

Autonomous execution runs the seven phases in order for each selected feature family:

1. assessment
2. dsl design
3. extraction (includes typed DSL foundation bootstrap when missing)
4. conversion
5. refactor
6. verification
7. next-feature recommendation (after the final selected family)

## Entry Point

Use `nkda-testdsl-autonomous` as the only required operator entrypoint.

Invoke it as:

`nkda-testdsl-autonomous {feature}`

`{feature}` may be one of:

- feature file path
- step file path
- feature family folder
- named feature family

If `{feature}` is omitted, `nkda-testdsl-autonomous` must run `nkda-testdsl-next-feature-selection` first and then continue the full loop.

If `{feature}` is a folder, `nkda-testdsl-autonomous` must:

1. Enumerate all `.feature` files under that folder in deterministic path order. This produces the worklist.
2. For each file in the worklist, one at a time:
   a. Resolve to feature family.
   b. Check already-adapted state — if adapted, record and skip.
   c. Classify wiring state.
   d. Run the full per-file pipeline: assessment → design → extraction → conversion (with per-scenario test execution and immediate scenario retirement on pass) → refactor → verification.
   e. On verification `PASS`: delete all retired scenarios, delete the `.feature` file, remove generated `.feature.cs` and legacy `*Steps.cs` scoped to wiring state.
   f. On verification `BLOCKED`/`FAIL`: retain the `.feature` file with unconverted scenarios only, record reason.
   g. Record terminal status for this file.
   h. Move to the next file in the worklist.
3. After all files are processed, run `nkda-testdsl-next-feature-selection`.
4. Output final totals and per-`.feature` status.

## Phase Gates

### Assessment Gate

Before design, identify:

- behaviour inventory
- step implementation map
- context state map
- assertion quality map
- missing-step intent backlog (scenarios requiring intent-derived tests)
- migration risks

If feature files and step files cannot be matched, stop and report.

### Design Gate

Before conversion, define:

- target test examples
- DSL public surface
- builder/runner/assertion split
- business-capability grouping model for converted tests and DSL entry points
- deletion plan for legacy artefacts

### Conversion Gate

Before deleting Reqnroll artefacts, equivalent code-first MSTest behaviour coverage must exist.
Before building any test, the existing test corpus must be searched for equivalent coverage; `pre-existing` scenarios map to the existing test, `partial-existing` scenarios extend it, and only `to-build` scenarios get a new test. No duplicate coverage may be created.
Missing-step scenarios with no pre-existing coverage must be converted into intent-derived tests or explicitly blocked with reason.
Scenario-level `.feature` cleanup is allowed only when the mapped code-first test is passing; otherwise the scenario remains in the `.feature` file.

### Verification Gate

A family is complete only when:

- for `wired` families, the parity map is complete; for `miswired`/`unwired` families, intent coverage is complete and every assertion is confirmed against observed production behaviour (no parity baseline existed)
- no duplicate coverage was created: `pre-existing` scenarios map to existing tests and no newly built test re-asserts already-covered behaviour
- Reqnroll artefacts are removed or explicitly retained for unmigrated scope, scoped to wiring state (`unwired` has no bindings or generated test to remove)
- converted/affected tests are green
- intent-derived tests meet test-validity threshold (`USEFUL` or `HIGH VALUE`, >= 16/25)
- full repository test suite is rerun after converted tests are green
- test commands, outcomes, and validity scores are recorded in verification output
- full `.feature` file deletion happens only after all family scenarios are retired and verification returns `PASS`

### Stop Gate

Autonomous execution stops after all selected families are complete, or sooner if:

- scope cannot be resolved for the entire run
- required shared inputs are missing for the entire run

A `miswired` or `unwired` wiring state is not by itself a failure; those families are built from intent. Per-family failures (for example parity gaps for `wired` families, assertions that cannot be confirmed against observed production behaviour or unresolved intent-vs-behaviour conflicts for `miswired`/`unwired` families, or unresolved family-scope failures) are recorded as `blocked`/`failed`, and execution continues with remaining families so the run converts everything it can.
