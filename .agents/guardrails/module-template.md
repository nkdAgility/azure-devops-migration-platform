# New Module Checklist

Use this checklist when adding a new module. Every item is required unless explicitly marked optional.

See [docs/modules.md](../../docs/modules.md) for the full `IModule` contract and [.agents/guardrails/system-architecture.md](system-architecture.md) for hard guardrails.

## 1. Schema

- [ ] Define the on-disk JSON schema for the module's artefacts.
- [ ] Assign a `schemaVersion` (start at `"1.0"`).
- [ ] Document all required and optional fields.
- [ ] Add the schema version to `manifest.json` under `schemaVersions`.
- [ ] Write a JSON Schema or equivalent validator.

## 2. Folder Layout

- [ ] Define the folder structure under `PackageRoot/<ModuleName>/`.
- [ ] Document the naming convention for all files and folders.
- [ ] Confirm the layout is deterministic (same input → same paths) and human-readable.

## 3. Cursor Format

- [ ] Define the cursor file at `.migration/Checkpoints/<modulename>.cursor.json`.
- [ ] Document the `lastProcessed` field semantics (what path or key it holds).
- [ ] Document all valid `stage` values for this module.
- [ ] Implement resume logic that reads the cursor and skips already-processed items.

## 4. IModule Implementation

- [ ] Implement `Name` — must match the key used in config `modules[].name` and in `manifest.json`.
- [ ] Implement `DependsOn` — declare all modules that must complete before this one.
- [ ] Implement `ExportAsync` — write only via `IArtefactStore`.
- [ ] Implement `PrepareAsync` — read from package via `IArtefactStore`, query target via injected services, write validation/mapping artefacts into the module's package folder (e.g. `<ModuleName>/prepare-report.json`). Must be idempotent (re-run overwrites output). Must not modify operator-edited mapping files.
- [ ] Implement `ImportAsync` — read via `IArtefactStore`, write state via `IStateStore`.
- [ ] Implement `ValidateAsync` — no side effects; validate schema and required fields only.

## 5. Validate Steps

- [ ] `ValidateAsync` checks that all required fields are present in every artefact file.
- [ ] `ValidateAsync` checks schema version compatibility.
- [ ] `ValidateAsync` reports anomalies to `.migration/Logs/` rather than failing silently.
- [ ] `ValidateAsync` fails fast on missing required fields.

## 5a. Prepare Steps

- [ ] `PrepareAsync` reads exported artefacts from the module's package folder via `IArtefactStore`.
- [ ] `PrepareAsync` queries the target system via injected services (not direct API calls).
- [ ] `PrepareAsync` writes a `<ModuleName>/prepare-report.json` with validation results.
- [ ] `PrepareAsync` reports each issue with enough detail for the operator to resolve it.
- [ ] `PrepareAsync` does NOT modify operator-edited mapping files.
- [ ] `PrepareAsync` is idempotent — re-running produces identical output for identical inputs.

## 6. Identity Mapping (if applicable)

- [ ] If the module writes user or group references, consume `IIdentityMappingService`.
- [ ] Do not implement identity resolution inline.
- [ ] Declare dependency on `IdentitiesModule` in `DependsOn`.

## 7. Tests Required

- [ ] Write acceptance scenarios in `features/<operation>/<module>[/<sub-module>]/<feature-name>.feature` before implementation.
- [ ] Reqnroll step definitions generated from the `.feature` file (`<ModuleName>Steps.cs` + `<ModuleName>Context.cs`).
- [ ] Unit tests for `ValidateAsync` covering valid and invalid artefact schemas.
- [ ] Unit tests for `ExportAsync` with a mock `IArtefactStore`.
- [ ] Unit tests for `PrepareAsync` with a mock `IArtefactStore` and mock target services.
- [ ] Unit tests for `ImportAsync` with a mock `IArtefactStore` and `IStateStore`.
- [ ] Unit tests for cursor resume — simulate a mid-run crash and verify correct resume behaviour.
- [ ] Integration test against a real or sandbox target (optional but strongly recommended).

## 8. Documentation

- [ ] Add a `docs/<modulename>.md` file describing the module's schema, folder layout, cursor, and any module-specific rules.
- [ ] Add the module to the table in [docs/modules.md](../../docs/modules.md).
- [ ] Add the module name to the `includedTypes` example in [.agents/context/package-format.md](../context/package-format.md) if it is a standard module.

## 9. Full Connector Coverage

- [ ] **Simulated** implementation is complete — deterministic, no external connectivity, generates realistic test data.
- [ ] **AzureDevOpsServices** implementation is complete — full REST API integration via .NET 10.
- [ ] **TeamFoundationServer** implementation is complete — TFS Object Model via the .NET 4.8 subprocess bridge, or explicitly exempted with a structured warning when the TFS OM API does not support the capability.
- [ ] No `throw new NotImplementedException()` or equivalent placeholder in any connector implementation.
- [ ] No connector implementation deferred to a follow-up PR or future task.

## 10. Observability ⛔ MANDATORY — zero exceptions

Observability is not optional for any module. Every item below must be implemented before the module is declared done. These checks are verified by the Phase Observability Gate in `speckit.implement` after every implementation phase.

Read `.agents/context/telemetry-architecture.md` and the `WellKnownMetricNames.cs`, `WellKnownActivitySourceNames.cs`, and `WellKnownMeterNames.cs` source files before implementing any of the items below.

### O-1 Distributed Tracing

- [ ] Every public method that performs an operation (`ExportAsync`, `ImportAsync`, `ValidateAsync`, `PrepareAsync`, per-item processing) wraps its work in `using var activity = ActivitySource.StartActivity("<span-name>")`.
- [ ] Span name follows `WellKnownActivitySourceNames` conventions (`export.<module>`, `import.<module>`, etc.).
- [ ] Activity has at minimum these tags: `job.id`, `module.name`, `connector.type`.
- [ ] Per-item spans include `item.id` or equivalent identifier.
- [ ] Error paths call `activity?.SetStatus(ActivityStatusCode.Error, ex.Message)` and `activity?.RecordException(ex)`.

### O-2 Metrics

- [ ] `IMigrationMetrics` (or `IDiscoveryMetrics`) is injected as a constructor parameter (non-optional, but called with `?.` to handle null in test contexts).
- [ ] `RecordAttempt(tags)` is called before each operation begins.
- [ ] `RecordCompleted(tags)` is called when an operation succeeds.
- [ ] `RecordError(tags)` is called in every `catch` block.
- [ ] `RecordDuration(elapsed, tags)` is called with `Stopwatch`-measured elapsed time.
- [ ] `RecordInFlight(+1, tags)` is called at the start and `RecordInFlight(-1, tags)` in a `finally` block.
- [ ] If new metric instruments are required: constant added to `WellKnownMetricNames.cs`, method added to `IMigrationMetrics`, implementation added to `MigrationMetrics`, meter registered in both host registration sites.

### O-3 Structured Logging

- [ ] `ILogger<T>` is injected as a constructor parameter.
- [ ] `LogInformation("Starting {Module} export for {Count} items", Name, count)` (or equivalent) at operation start.
- [ ] `LogInformation("Completed {Module} export: {Processed} processed, {Skipped} skipped", Name, processed, skipped)` at operation end.
- [ ] `LogWarning("Skipping {Id}: {Reason}", id, reason)` on every skip/no-op path.
- [ ] `LogDebug("Processing {Path}", path)` per-item (gated by `IsEnabled(LogLevel.Debug)` if in a hot loop).
- [ ] `LogError(ex, "Failed processing {Id}: {Message}", id, ex.Message)` in error paths.
- [ ] **No string interpolation** in any log call — structured parameters only.

### O-4 Progress Events

- [ ] `IProgressSink?` is injected as an optional constructor parameter (`IProgressSink? progressSink`).
- [ ] `await _progressSink.EmitAsync(new ProgressEvent { Module = Name, Stage = "Exporting", ... })` at operation start.
- [ ] `await _progressSink.EmitAsync(...)` per item or per batch ≤50 items.
- [ ] `await _progressSink.EmitAsync(...)` at operation completion with final counts.
- [ ] Completion `ProgressEvent.Metrics.Migration.{ModuleName}` is populated with a `ModuleCounters` instance containing `Processed`, `Skipped`, `Failed` counts.
- [ ] If `_progressSink` is null (no sink registered), the module continues without error.

### O-4 CLI Visibility

- [ ] `MigrationCounters` (in `Abstractions/ControlPlaneApi/`) has a property for this module's `ModuleCounters` (e.g. `public TeamsCounters? Teams { get; init; }`).
- [ ] `SnapshotMetricExporter.cs` (in `Infrastructure.ControlPlane/Metrics/`) is updated to extract this module's OTel metrics and map them into the `JobMetrics.Migration.{Module}` property.
- [ ] `QueueCommand.BuildProgressRenderable` (CLI) has a progress bar row for this module in correct execution order (Identities → Nodes → Teams → WorkItems → ...). The row is rendered when the module counter is non-null.

### Observability Tests

- [ ] Unit test: verify `ActivitySource.StartActivity` is called with the correct span name (use `TestActivityListener` helper or verify via mock).
- [ ] Unit test: verify `IMigrationMetrics.RecordAttempt`, `RecordCompleted`, and `RecordDuration` are called with a `TagList` containing the correct `job.id` and `module.name` tags (inject `Mock<IMigrationMetrics>`).
- [ ] Unit test: verify `IProgressSink.EmitAsync` is called at start, per-item (or per batch), and completion with correct `Stage` and non-null `Metrics` on completion (inject `Mock<IProgressSink>`).
- [ ] Unit test: verify `ILogger` receives `LogInformation` with correct structured parameters at start and end (inject `Mock<ILogger<T>>` or use `FakeLogger`).
- [ ] Simulated system test (`[TestCategory("SystemTest_Simulated")]`): run a full export+import scenario end-to-end; assert the CLI output contains a visible progress row for this module.

## 11. DI Wiring Verification

- [ ] All classes implementing interfaces are registered in a `Add<ModuleName>Services(this IServiceCollection services)` extension method.
- [ ] That extension method is called from the host startup (`MigrationAgentServiceExtensions`, `TfsMigrationPlatformHost`, or equivalent).
- [ ] Constructor injection only — no `serviceProvider.GetRequiredService<T>()` inside module logic.
- [ ] The module is registered in the module registry so it can be resolved by name at runtime.
- [ ] End-to-end test or scenario run confirms the module activates without `InvalidOperationException` for missing services.
