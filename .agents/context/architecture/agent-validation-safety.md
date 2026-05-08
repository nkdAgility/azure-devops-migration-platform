# agent_validation_safety — Validation and Safety System

- Tag: `agent_validation_safety`
- Responsibility: Validate package invariants before/after execution and enforce fail-fast behavior for invalid execution inputs.

## Core Classes

- `PackageValidator`
- `ValidationResult`
- `ValidationError`
- `PackageConfigNotFoundException`

## Validating Tests

- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/PackageValidatorTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobAgentWorkerDispatchTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs`

## Sequence Diagram

```mermaid
sequenceDiagram
  participant JW as JobAgentWorker
  participant PV as PackageValidator
  participant AS as IArtefactStore
  participant PS as IProgressSink
  participant CP as ControlPlane

  JW->>PV: ValidateAsync()
  PV->>AS: Read manifest/revision/attachment metadata
  PV-->>JW: ValidationResult
  alt Validation failed
    JW->>PS: Emit failure ProgressEvent
    JW->>CP: POST /agents/lease/{leaseId}/fail
  else Validation passed
    JW-->>JW: Continue phase execution
  end
```
