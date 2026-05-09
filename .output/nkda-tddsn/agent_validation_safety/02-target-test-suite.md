# Target Test Suite: agent_validation_safety

## Proposed Test Classes

### `PackageValidatorTests`

1. `ValidateAsync_WellFormedPackage_ReturnsPassed`
   Type: Unit
   Status: rewrite
   Protects: Valid manifest and revision produce passed validation.
   Expected assertions:
   - `Passed` is true.
   - Error count is zero.

2. `ValidateAsync_MissingManifest_ReturnsFailed`
   Type: Unit
   Status: rewrite
   Protects: Missing `manifest.json` fails validation.
   Expected assertions:
   - `Passed` is false.
   - First error message mentions not found.

3. `ValidateAsync_UnsupportedSchemaVersion_ReturnsFailed`
   Type: Unit
   Status: rewrite
   Protects: Unsupported schema versions fail validation.
   Expected assertions:
   - `Passed` is false.
   - Error message mentions unsupported schema version.

4. `ValidateAsync_MissingSchemaVersion_ReturnsFailed`
   Type: Unit
   Status: add
   Protects: Manifest without `schemaVersion` fails validation.
   Expected assertions:
   - `Passed` is false.
   - Error path is `manifest.json`.
   - Error message mentions `schemaVersion`.

5. `ValidateAsync_InvalidManifestJson_ReturnsManifestError`
   Type: Unit
   Status: add
   Protects: Malformed manifest JSON is reported as a manifest error.
   Expected assertions:
   - `Passed` is false.
   - An error exists for `manifest.json` with `Invalid JSON` in the message.

6. `ValidateAsync_RevisionMissingWorkItemId_ReturnsFailed`
   Type: Unit
   Status: rewrite
   Protects: Required revision fields are enforced.
   Expected assertions:
   - `Passed` is false.
   - Error message mentions `workItemId`.

7. `ValidateAsync_InvalidRevisionJson_ReturnsFailed`
   Type: Unit
   Status: rewrite/rename from generic invalid JSON test
   Protects: Malformed revision JSON fails validation.
   Expected assertions:
   - `Passed` is false.
   - Error message mentions `Invalid JSON`.

8. `ValidateAsync_RevisionListedButUnreadable_ReturnsFileNotFoundErrorForListedPath`
   Type: Unit
   Status: add
   Protects: Enumerated revision paths that cannot be read are rejected.
   Expected assertions:
   - `Passed` is false.
   - Error path equals the enumerated path.
   - Error message is `File not found.`.

9. `ValidateAsync_MultipleInvalidRevisionFiles_ReturnsErrorForEachInvalidRevision`
   Type: Unit
   Status: add
   Protects: Validation does not stop after the first invalid revision.
   Expected assertions:
   - `Passed` is false.
   - Error count is 2.
   - One error mentions invalid JSON.
   - One error mentions missing `revisionIndex`.

10. `ValidateAsync_NonRevisionWorkItemsArtefact_IsIgnored`
    Type: Unit
    Status: add
    Protects: Current validator scope only inspects `revision.json` files.
    Expected assertions:
    - `Passed` is true when non-revision WorkItems artefact is malformed but revision is valid.

11. `ValidateAsync_IsReadOnly_NoPackageWritesPerformed`
    Type: Unit
    Status: rewrite
    Protects: Validation is read-only through the package abstraction.
    Expected assertions:
    - No `WriteAsync` calls.
    - No `WriteBinaryAsync` calls.
    - No `WriteStreamAsync` calls.
    - No `AppendAsync` calls.

## Existing Test Decisions

| Existing Test | Decision | Reason |
| ------------- | -------- | ------ |
| `ValidateAsync_WellFormedPackage_ReturnsPassed` | Rewrite | Replace filesystem with in-memory store. |
| `ValidateAsync_MissingManifest_ReturnsFailed` | Rewrite | Replace filesystem with in-memory store. |
| `ValidateAsync_UnsupportedSchemaVersion_ReturnsFailed` | Rewrite | Replace filesystem with in-memory store. |
| `ValidateAsync_RevisionMissingWorkItemId_ReturnsFailed` | Rewrite | Replace filesystem with in-memory store. |
| `ValidateAsync_InvalidJson_ReturnsFailed` | Rewrite/rename | Clarify that the invalid JSON is a revision file. |
| `ValidateAsync_IsReadOnly_NoFilesCreated` | Rewrite | File count is weaker than asserting no package write methods. |
| `PackageValidationSteps` | Keep | Feature mapping remains useful but not sufficient unit proof. |
| Worker fail-fast tests | Defer | Current active seam is unclear; follow-up required. |

## Target Suite Gate Status

Passed for the PackageValidator unit-test rebuild: every target test has a name, type, protected behaviour, and expected assertion. Worker-level fail-fast remains a documented follow-up outside the minimal validator rebuild.