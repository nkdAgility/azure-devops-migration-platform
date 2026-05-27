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

Autonomous execution runs the seven phases in order for exactly one feature family:

1. assessment
2. dsl design
3. extraction
4. conversion
5. refactor
6. verification
7. next-feature recommendation

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

## Phase Gates

### Assessment Gate

Before design, identify:

- behaviour inventory
- step implementation map
- context state map
- assertion quality map
- migration risks

If feature files and step files cannot be matched, stop and report.

### Design Gate

Before conversion, define:

- target test examples
- DSL public surface
- builder/runner/assertion split
- deletion plan for legacy artefacts

### Conversion Gate

Before deleting Reqnroll artefacts, equivalent code-first MSTest behaviour coverage must exist.

### Verification Gate

A family is complete only when:

- parity map is complete
- Reqnroll artefacts are removed or explicitly retained for unmigrated scope
- relevant test command is recorded in verification output

### Stop Gate

Autonomous execution stops after one family or sooner if:

- behaviour parity cannot be established
- conversion requires unplanned production behaviour changes
- failures cannot be resolved within family scope
