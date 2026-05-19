# Quickstart: Plan Validation for Resumable Batching

## Goal
Validate planning artifacts for resumable batching before implementation begins.

## Prerequisites
- Feature spec exists at `specs/020-resumable-batching-cursor/spec.md`.
- Planning artifacts in this folder are present.
- Guardrails in `agents.md` and `/.agents/20-guardrails/*` were read in current session.

## Steps
1. Confirm the contract assumptions:
   - Resume mode is caller-controlled.
   - Query fingerprint includes query text + parameters only.
   - Caller owns dedup and checkpoint persistence cadence.
2. Review `research.md` decisions and ensure each maps to one or more FR/SC items in the spec.
3. Review `data-model.md` entities and validate transition correctness for `ResumeDecision` states.
4. Review `contracts/resumable-batching-contract.md` and confirm:
   - Deterministic ordering (`ChangedDate`, `WorkItemId`) is fixed.
   - Query mismatch behavior returns explicit decision, not silent reset.
   - Completion checkpoint is mandatory.
5. Verify architecture alignment against references listed in `spec.md` and ensure no new undocumented architecture decisions were introduced.

## Expected Outcome
- Plan artifacts are complete and aligned with guardrails.
- No implementation code changes are required at this stage.
- Remaining doc gaps are tracked in `discrepancies.md` for resolution during implementation.

## Handoff to Task Generation
- Run `/speckit.tasks` for this feature after planning approval.
- Keep one ATDD session per scenario during implementation.

