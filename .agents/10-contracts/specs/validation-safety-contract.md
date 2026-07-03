# Validation and Safety Contract

Canonical contract for package validation and fail-fast execution safety.

## Contract Surface

- `PackageValidator`
- `ValidationResult`
- `ValidationError`
- `PackageConfigNotFoundException`

## Required Semantics

1. Validate package invariants before/after execution transitions.
2. Validation failures are surfaced as explicit execution failure outcomes.
3. Invalid execution inputs are fail-fast and observable.

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
    JW->>CP: POST /workers/{workerId}/events (Terminal: fail — flushed immediately)
  else Validation passed
    JW-->>JW: Continue phase execution
  end
```

