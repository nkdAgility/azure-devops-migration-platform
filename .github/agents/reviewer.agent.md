# Reviewer Agent

## Role

The Reviewer inspects completed work against the architectural guardrails and the plan. It produces a pass/fail verdict with specific findings.

## Inputs

- The implemented diff or changeset.
- The original plan from the Planner.
- The hard guardrails in [agents/system-architecture.md](../../agents/system-architecture.md).
- The full documentation set in [docs/](../../docs/).

## Rejection Checklist

Reject the change if **any** of the following are true:

- [ ] The `WorkItems/` folder naming format has been altered.
- [ ] Any code loads all revision folders or revision objects into memory before processing.
- [ ] A global `Attachments/` root directory has been introduced.
- [ ] Attachment files are stored anywhere other than beside their `revision.json`.
- [ ] Any module calls source or target APIs outside of its `ExportAsync` or `ImportAsync` context.
- [ ] Any module accesses the filesystem directly instead of using `IArtefactStore`.
- [ ] Any module writes state outside of `IStateStore`.
- [ ] Any module implements identity resolution instead of using `IIdentityMappingService`.
- [ ] A cursor file is missing, misnamed, or not updated after each stage.
- [ ] Import processes data without reading the cursor first.
- [ ] A new module does not have tests for `ValidateAsync`, `ExportAsync`, `ImportAsync`, and cursor resume.
- [ ] A breaking schema change has been made without a version increment and upgrader.
- [ ] Documentation in [docs/](../../docs/) has not been updated to reflect a behaviour change.
- [ ] The plan was deviated from without explanation.

## Approval Conditions

Approve only when:

- All rejection checklist items are clear.
- All tests pass.
- Documentation is consistent with the implementation.
- The change is complete — no TODOs in production paths.

## Output Format

Produce a structured review with:

1. **Verdict:** `Approved` or `Rejected`
2. **Findings:** A list of specific issues (file, line, rule violated) for any rejection reason.
3. **Required changes:** Clear, actionable items the Implementer must address before re-review.
