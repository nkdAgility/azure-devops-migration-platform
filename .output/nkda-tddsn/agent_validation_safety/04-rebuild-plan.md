# Rebuild Plan: agent_validation_safety

## Priority 1: Stop critical validator drift

* Replace filesystem-backed `PackageValidatorTests` setup with an in-memory `IArtefactStore` fake.
* Preserve tests for valid package, missing manifest, unsupported schema, missing revision field, invalid revision JSON, and read-only behaviour.

Stopping point:
* Existing validator tests are expressed through the package port and no longer require temp filesystem setup.

## Priority 2: Add missing manifest boundary protection

* Add `ValidateAsync_MissingSchemaVersion_ReturnsFailed`.
* Add `ValidateAsync_InvalidManifestJson_ReturnsManifestError`.

Stopping point:
* Manifest existence, parseability, and schema support are all protected by unit tests.

## Priority 3: Add missing revision enumeration protection

* Add `ValidateAsync_RevisionListedButUnreadable_ReturnsFileNotFoundErrorForListedPath`.
* Add `ValidateAsync_MultipleInvalidRevisionFiles_ReturnsErrorForEachInvalidRevision`.
* Add `ValidateAsync_NonRevisionWorkItemsArtefact_IsIgnored`.

Stopping point:
* Revision path enumeration, unreadable listed paths, multi-error accumulation, and non-revision filtering are protected.

## Priority 4: Strengthen read-only contract

* Replace file-count assertion with interaction counters on the in-memory store.
* Assert no `WriteAsync`, `WriteBinaryAsync`, `WriteStreamAsync`, or `AppendAsync` calls occur during validation.

Stopping point:
* The validator read-only contract is tested through the abstraction it actually uses.

## Priority 5: Follow-up outside this minimal rebuild

* Confirm the intended pre-import fail-fast seam.
* Add active worker/orchestrator tests for failed validation preventing import execution.
* Implement minimal production wiring only after a failing target test exists.

Minimal Change Gate:
* No production code change is required for the PackageValidator target suite because current implementation already satisfies the newly specified validator behaviours by inspection.
* Production code changes for worker fail-fast are explicitly deferred until the orchestration seam is confirmed and protected by a failing test.