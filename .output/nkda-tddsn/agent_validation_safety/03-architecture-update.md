# Architecture Update: agent_validation_safety

## Current Architecture Narrative

`agent_validation_safety` is centered on a small validation port and implementation:

- `IPackageValidator` exposes `ValidateAsync(CancellationToken)`.
- `PackageValidator` reads package artefacts through `IArtefactStore`.
- `ValidationResult` and `ValidationError` carry the validation outcome and package-relative failure details.
- The validator is read-only and does not call target APIs.

The subsystem documentation also describes intended fail-fast orchestration through the migration worker: invalid validation results should emit failure progress and signal the control plane lease as failed before import begins.

## Architecture Alignment From This Rebuild

The rebuilt unit tests deepen the hexagonal boundary around `PackageValidator` by replacing direct filesystem use with an in-memory `IArtefactStore` fake. This keeps package validation tests at the unit layer and verifies the validator through its package port instead of an infrastructure store implementation.

No production architecture change was required for the PackageValidator behaviours covered by the target suite.

## Documentation Change Decision

No canonical architecture document was changed in this pass because the existing `.agents/30-context/architecture/agent-validation-safety.md` already states the intended responsibility and sequence. The report records a drift risk instead: worker-level fail-fast orchestration is documented but not verified by active compiled tests and was not changed without a confirmed production seam.

## Proposed Follow-Up Architecture Clarification

Clarify whether pre-import and post-import validation are owned by:

1. `JobAgentWorker` directly,
2. `ModulePipelineWorkerBase`,
3. `JobPlanExecutor`, or
4. a dedicated validation orchestration service.

Once the owner is confirmed, add an explicit dependency on `IPackageValidator` in that layer and protect it with an active test that proves failed validation prevents import execution and signals terminal failure to the control plane.

## Guardrail Alignment

- Clean Architecture: validator tests now depend on `IArtefactStore`, not `FileSystemArtefactStore`.
- Hexagonal Architecture: package storage remains a port boundary.
- Testing Rules: validator logic is protected by unit tests rather than real I/O.
- Package Rules: validator remains read-only and package-relative.
- Observability: no new telemetry was added; the rebuild only changes tests.
