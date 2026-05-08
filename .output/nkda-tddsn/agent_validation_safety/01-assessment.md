# Assessment Report: agent_validation_safety

## Scope

Subsystem:
  agent_validation_safety

Analysed sources:
  - `.agents/context/architecture/agent-validation-safety.md`
  - `features/platform/validation/package-validation.feature`
  - `src/DevOpsMigrationPlatform.Infrastructure.Agent/Validation/PackageValidator.cs`
  - `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/IPackageValidator.cs`
  - `src/DevOpsMigrationPlatform.Abstractions/Validation/ValidationResult.cs`
  - `src/DevOpsMigrationPlatform.Abstractions/Validation/ValidationError.cs`
  - `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs`

Analysed tests:
  - `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/PackageValidatorTests.cs`
  - `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/PackageValidationContext.cs`
  - `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/PackageValidationSteps.cs`
  - `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobAgentWorkerDispatchTests.cs` (excluded from current test project compile)
  - `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs`

Partial analysis warnings:
  - `dotnet` is unavailable in this container, so current test execution evidence cannot be produced here.
  - `JobAgentWorkerDispatchTests.cs` is explicitly removed from `DevOpsMigrationPlatform.Infrastructure.Agent.Tests.csproj`; worker dispatch assertions in that file are not active in this project.
  - The architecture document names fail-fast worker behaviour, but the current discoverable production implementation does not inject or invoke `IPackageValidator` from `JobAgentWorker`; this report treats that as an architectural drift risk rather than inventing undocumented execution behaviour.

## Behaviour Model

Purpose:
  Validate package invariants before/after execution and expose structured validation failures without mutating package content. Invalid package inputs must be rejected early so later migration phases do not process corrupted or incomplete data.

Primary behaviours:
  B1. A package with a supported manifest schema version and valid WorkItems `revision.json` files returns `ValidationResult.Ok()`.
  B2. Missing, malformed, or unsupported `manifest.json` content returns `ValidationResult.Fail()` with an error path of `manifest.json`.
  B3. Each enumerated WorkItems `revision.json` file must be readable JSON and contain every required revision field.
  B4. Invalid revisions return path-specific `ValidationError` entries that identify the offending file and missing/invalid content.
  B5. Non-`revision.json` WorkItems artefacts are outside the current `PackageValidator` contract and are ignored.
  B6. Validation is read-only: no writes, binary writes, stream writes, appends, target calls, or package side effects.
  B7. Pre-import fail-fast orchestration is intended by feature/docs, but is not currently active in the compiled worker surface inspected here.

State transitions:
  S1. Valid package -> `Passed = true`, `Errors = []`.
  S2. Any manifest or revision error -> `Passed = false`, `Errors = validation errors`.
  S3. Failed pre-import validation -> intended orchestration status `ValidationFailed`; current active worker code requires follow-up verification/implementation.

External contracts:
  C1. `IPackageValidator.ValidateAsync(CancellationToken)` returns `ValidationResult`.
  C2. `IArtefactStore.ReadAsync` is used for package reads.
  C3. `IArtefactStore.EnumerateAsync("WorkItems/", cancellationToken)` is used to stream candidate revision paths.
  C4. `ValidationError.Path` is package-relative.
  C5. `ValidationError.Message` identifies missing fields, invalid JSON, unsupported schemas, or missing files.

Failure and rejection behaviours:
  F1. Missing manifest is rejected as `manifest.json not found.`.
  F2. Missing manifest `schemaVersion` is rejected.
  F3. Unsupported schema version is rejected.
  F4. Invalid JSON in manifest or revision is rejected.
  F5. Enumerated revision path that cannot be read is rejected as `File not found.`.
  F6. Missing required revision fields are rejected.

Boundary conditions:
  E1. Empty WorkItems folder with valid manifest currently passes because no revision files are enumerated.
  E2. Only paths ending with `revision.json` are inspected.
  E3. Path casing for `revision.json` is case-insensitive.
  E4. Validation reports one missing field per invalid revision because `ValidateRevisionAsync` returns on first missing required field.

Drift risks:
  D1. Tests using real filesystem can drift into integration behaviour and violate unit-test guardrails.
  D2. Missing schemaVersion, invalid manifest JSON, unreadable enumerated paths, multiple invalid revisions, and ignored non-revision artefacts were not explicitly covered by plain unit tests.
  D3. Feature-level steps contain simulated orchestration for pre-import fail-fast, not compiled production worker verification.
  D4. Architecture docs mention fail-fast worker behaviour that is not evident in current active `JobAgentWorker` dependencies.
  D5. Package guardrails mention richer pre-flight/post-flight validation than the current `PackageValidator` implementation provides; this is out of scope for the minimal safety-net rebuild but should remain visible.

## Current Test Inventory

| Test | Type | Behaviour Protected | Score | Classification | Action |
|------|------|---------------------|-------|----------------|--------|
| `PackageValidatorTests.ValidateAsync_WellFormedPackage_ReturnsPassed` | Unit with real filesystem | Valid package passes | 28/36 | Partial | Rewrite to in-memory fake |
| `PackageValidatorTests.ValidateAsync_MissingManifest_ReturnsFailed` | Unit with real filesystem | Missing manifest rejects | 27/36 | Partial | Rewrite |
| `PackageValidatorTests.ValidateAsync_UnsupportedSchemaVersion_ReturnsFailed` | Unit with real filesystem | Unsupported schema rejects | 27/36 | Partial | Rewrite |
| `PackageValidatorTests.ValidateAsync_RevisionMissingWorkItemId_ReturnsFailed` | Unit with real filesystem | Required revision field rejects | 27/36 | Partial | Rewrite |
| `PackageValidatorTests.ValidateAsync_InvalidJson_ReturnsFailed` | Unit with real filesystem | Invalid revision JSON rejects | 27/36 | Partial | Rewrite/rename for precision |
| `PackageValidatorTests.ValidateAsync_IsReadOnly_NoFilesCreated` | Unit with real filesystem | No file count side effects | 24/36 | Weak | Rewrite to assert no `IArtefactStore` writes/appends |
| `PackageValidationSteps` scenarios | Feature step definitions | Gherkin behaviour examples | 20/36 | Weak | Keep as feature mapping but do not rely on simulated orchestration as unit proof |
| `JobAgentWorkerDispatchTests` validation references | Excluded unit tests | Intended worker dispatch/fail-fast | 8/36 | Failing/inactive | Follow-up; not rebuilt in this pass |

## Detailed Scoring

### PackageValidatorTests.ValidateAsync_WellFormedPackage_ReturnsPassed

Type:
  Unit test with real filesystem dependency

Protects:
  Valid manifest and valid revision produce a passed result.

Scores:
  Behaviour Focus: 3 | Directly checks validator contract.
  Small and Focused: 3 | Single behaviour.
  Readable as Example: 3 | Inputs are visible.
  Fails for Right Reason: 2 | Could fail due filesystem setup/cleanup rather than validator logic.
  Deterministic: 2 | Temp filesystem dependency is usually deterministic but unnecessary.
  Fast for Type: 2 | Real I/O is slower than needed.
  Independent: 3 | Uses unique temp directory.
  Clear Name: 3 | Name follows convention.
  Meaningful Example: 3 | Valid package sample is concrete.
  Minimises Mocking: 2 | Uses real infrastructure rather than fake store.
  Drives Design Pressure: 1 | Does not pressure `IArtefactStore` read-only boundary.
  Asserts Outcomes, State, or Contracts: 1 | Only result; no store interaction contract.

Total:
  28/36

Classification:
  Partial

Recommended action:
  rewrite

### PackageValidatorTests.ValidateAsync_IsReadOnly_NoFilesCreated

Type:
  Unit test with real filesystem dependency

Protects:
  Validator does not create additional files.

Scores:
  Behaviour Focus: 2 | Targets read-only property but only file count.
  Small and Focused: 2 | Includes setup and filesystem counting.
  Readable as Example: 2 | Intent is clear but implementation-centric.
  Fails for Right Reason: 1 | A rewrite of existing file content could pass.
  Deterministic: 2 | Real filesystem dependency.
  Fast for Type: 1 | I/O-heavy for unit contract.
  Independent: 3 | Temp folder isolation.
  Clear Name: 2 | Says no files created, not no writes.
  Meaningful Example: 2 | Valid package sample.
  Minimises Mocking: 1 | Real store hides interaction contract.
  Drives Design Pressure: 2 | Some read-only pressure.
  Asserts Outcomes, State, or Contracts: 1 | Only file count is asserted, so existing-file writes could go undetected.

Total:
  21/36

Classification:
  Weak

Recommended action:
  rewrite

## Drift Risk Map

### D1: Unit tests drift into filesystem integration tests

Behaviour:
  Package validation should be testable through `IArtefactStore` without direct filesystem dependency.

Current protection:
  weak

Why drift can occur:
  File count assertions can miss writes to existing files and make tests slower/flakier than necessary.

Proposed protection:
  - `ValidateAsync_IsReadOnly_NoPackageWritesPerformed`

Priority:
  high

### D2: Manifest boundary errors not fully protected

Behaviour:
  Manifest must exist, parse, and contain supported `schemaVersion`.

Current protection:
  partial

Why drift can occur:
  Missing `schemaVersion` and malformed manifest JSON were not directly protected by plain unit tests.

Proposed protection:
  - `ValidateAsync_MissingSchemaVersion_ReturnsFailed`
  - `ValidateAsync_InvalidManifestJson_ReturnsManifestError`

Priority:
  high

### D3: Revision enumeration/read boundaries not fully protected

Behaviour:
  Every enumerated `revision.json` path must be checked and invalid revisions reported.

Current protection:
  partial

Why drift can occur:
  Tests did not cover an enumerated path whose read returns null, multiple invalid revisions, or non-revision artefact filtering.

Proposed protection:
  - `ValidateAsync_RevisionListedButUnreadable_ReturnsFileNotFoundErrorForListedPath`
  - `ValidateAsync_MultipleInvalidRevisionFiles_ReturnsErrorForEachInvalidRevision`
  - `ValidateAsync_NonRevisionWorkItemsArtefact_IsIgnored`

Priority:
  high

### D4: Intended fail-fast worker orchestration is not actively protected

Behaviour:
  Import should not begin when pre-import validation fails.

Current protection:
  weak/inactive

Why drift can occur:
  Feature steps simulate the validator result, and `JobAgentWorkerDispatchTests.cs` is excluded from the infrastructure agent test project.

Proposed protection:
  - Follow-up worker-level target test after confirming intended production seam for `IPackageValidator`.

Priority:
  critical follow-up

## Gap Map

| Behaviour / Risk | Existing Protection | Missing Tests | Priority |
|------------------|--------------------|---------------|----------|
| Valid package passes | partial | rewritten in-memory test | high |
| Missing manifest rejects | partial | rewritten in-memory test | high |
| Unsupported schema rejects | partial | rewritten in-memory test | high |
| Missing schemaVersion rejects | none | `ValidateAsync_MissingSchemaVersion_ReturnsFailed` | high |
| Invalid manifest JSON rejects | none | `ValidateAsync_InvalidManifestJson_ReturnsManifestError` | high |
| Missing revision field rejects | partial | rewritten in-memory test | high |
| Invalid revision JSON rejects | partial | renamed/revised in-memory test | high |
| Enumerated unreadable revision rejects | none | `ValidateAsync_RevisionListedButUnreadable_ReturnsFileNotFoundErrorForListedPath` | high |
| Multiple invalid revisions all reported | none | `ValidateAsync_MultipleInvalidRevisionFiles_ReturnsErrorForEachInvalidRevision` | high |
| Non-revision artefacts ignored | none | `ValidateAsync_NonRevisionWorkItemsArtefact_IsIgnored` | medium |
| Read-only behaviour | weak | `ValidateAsync_IsReadOnly_NoPackageWritesPerformed` | high |
| Worker fail-fast orchestration | weak/inactive | follow-up worker test | critical follow-up |

## Design Feedback

Issue: The current `PackageValidator` is cohesive and testable through `IArtefactStore`, but the broader fail-fast orchestration described by docs is not connected to the active worker dependency graph inspected here.

Evidence: `PackageValidator` implements `IPackageValidator`; search of active worker code did not reveal injection/use of `IPackageValidator` in `JobAgentWorker`.

Impact on tests: Unit tests can stabilize validator invariants now. Worker fail-fast should not be faked in validator unit tests; it needs a separate orchestration seam.

Recommended seam: Add an explicit pre-import validation dependency to the worker or phase orchestration layer, then test terminal fail signalling and skipped import execution through active compiled tests.

Proposed first test after seam: `OnJobAsync_Migrate_WhenPreImportValidationFails_SignalsFailAndDoesNotExecuteImportPhase`.

## Summary

Keep:
  - Core `PackageValidator` production implementation in this pass.

Rewrite:
  - All plain `PackageValidatorTests` to use an in-memory `IArtefactStore` fake.
  - Read-only test to assert no store write/append methods are called.

Delete:
  - Filesystem setup/cleanup from validator unit tests.

Add:
  - Missing schema version test.
  - Invalid manifest JSON test.
  - Enumerated unreadable revision test.
  - Multiple invalid revisions test.
  - Non-revision artefact ignored test.

Highest risk missing protection:
  - Worker-level pre-import fail-fast orchestration remains unverified by active compiled tests.

Next best action:
  - Implement the in-memory validator unit test rebuild now, then open a follow-up for worker fail-fast orchestration once the intended seam is confirmed.