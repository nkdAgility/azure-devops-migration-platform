# Reconciliation Tasks — 021.2 Separation of Concerns

## Phase 0 — Pre-flight audit
- [ ] P0 Pre-flight audit (`dotnet list reference`, record + compare topology) - Status: incomplete
  Evidence: `analysis/current-project-references.md` is not present in the repository.

## Phase 1 — Sever the CLI ↔ Agent/CP compile-time coupling
- [X] Step 1.1 Delete in-process fallback from `LocalStackHost` - Status: complete
- [ ] Step 1.2 Extract `IPreFlightValidator` interface - Status: incomplete
  Evidence: No `IPreFlightValidator` symbol exists under `src/`.
- [X] Step 1.3 Remove direct `ConfigurationService` instantiation - Status: complete/superseded; completed because superseded by specs/028-ioptions-schema-gen
  Superseded evidence: CLI commands resolve `IConfigurationService` through DI; the specific factory-extension pattern in 021.2 is replaced by later IOptions/schema work.
- [X] Step 1.4 Move config-schema types to `Abstractions/Options` - Status: complete
- [X] Step 1.5 Move serialization to `Abstractions/Serialization` - Status: complete/superseded; completed because superseded by docs/architecture.md and specs/028-ioptions-schema-gen
  Superseded evidence: Polymorphic converter infrastructure remains in `Infrastructure/Serialization` by design (`EndpointOptionsTypeRegistry`-based registration path), with architecture docs describing this evolved shape.
- [X] Step 1.6 Remove invalid project references from `CLI.Migration` - Status: complete/superseded; completed because superseded by docs/architecture.md project-boundary contract
  Superseded evidence: `CLI.Migration.csproj` removed prohibited references and now has `Abstractions`, `Infrastructure`, and `ServiceDefaults` only.
- [ ] Step 1.7 Remove `LogDownloadController` and switch to log-path reporting - Status: incomplete
  Evidence: `LogDownloadController` is absent, but `JobDiagnostics` has no `LogPath` field and docs still contain `/jobs/{jobId}/logs/download`.
- [X] Step 1.8 Move ControlPlane→Infrastructure registration to `ControlPlaneHost` - Status: complete/superseded; completed because superseded by `Infrastructure.ControlPlane` split
  Superseded evidence: `ControlPlane.csproj` has no infrastructure project references; `ControlPlaneHost` composes CP + infra-control-plane.
- [X] Step 1.9 Fix test project references - Status: complete

## Phase 2 — Restructure `Abstractions` folders (screaming architecture)
- [X] Step 2.1 Dissolve `Models/` into domain folders - Status: complete
- [X] Step 2.2 Dissolve `Services/` into domain folders - Status: complete
- [X] Step 2.3 Dissolve `Errors/` - Status: complete
- [X] Step 2.4 Move root-level files into proper folders - Status: complete
- [X] Step 2.5 Dissolve `Utilities/` - Status: complete
- [X] Step 2.6 Move extension options out of `Modules/` - Status: complete
- [X] Step 2.7 Separate cross-cutting telemetry from Agent/CP-specific telemetry - Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  Superseded evidence: telemetry constants and interfaces were reworked again under unified `platform.*` metric naming and split projects.

## Phase 3 — Extract `Abstractions.ControlPlane` and `Abstractions.Agent` projects
- [X] Step 3.1 Create `Abstractions.ControlPlane` project - Status: complete
- [X] Step 3.2 Create `Abstractions.Agent` project - Status: complete/superseded; completed because superseded by specs/034-package-manager-adoption
  Superseded evidence: storage concerns were further split into `Abstractions.Storage` beyond the original 021.2 layout.
- [X] Step 3.3 Add project references to consumers - Status: complete/superseded; completed because superseded by specs/023.5-tfsmigrationagent-architectural-consistency and specs/034-package-manager-adoption
  Superseded evidence: final topology includes `TfsMigrationAgent` and dedicated storage projects not modeled in 021.2 tables.
- [X] Step 3.4 Clean up empty folders - Status: complete
- [X] Step 3.5 Update `DevOpsMigrationPlatform.slnx` - Status: complete

## Phase 4 — Restructure `Infrastructure` folders (cross-cutting only)
- [X] Step 4.1a `git mv` cross-cutting files to screaming folders - Status: complete/superseded; completed because superseded by later storage/package refactors
  Superseded evidence: final state is split across `Infrastructure`, `Infrastructure.Agent`, `Infrastructure.ControlPlane`, and dedicated storage projects.
- [X] Step 4.1b Namespace updates for moved files - Status: complete/superseded; completed because superseded by later project splits
  Superseded evidence: current namespaces reflect post-split architecture rather than interim 021.2 wording.

## Phase 5 — Extract `Infrastructure.ControlPlane` and `Infrastructure.Agent` projects
- [X] Step 5.1a Create `Infrastructure.ControlPlane` project - Status: complete
- [X] Step 5.1b Move CP telemetry files to `Infrastructure.ControlPlane/Metrics` - Status: complete
- [X] Step 5.1c Namespace updates for CP files - Status: complete
- [X] Step 5.1d Create `AddControlPlaneTelemetryServices()` registration - Status: complete
- [X] Step 5.2a Create `Infrastructure.Agent` project - Status: complete
- [X] Step 5.2b Move Agent files to `Infrastructure.Agent` - Status: complete/superseded; completed because superseded by specs/034-package-manager-adoption
  Superseded evidence: package/storage parts are now additionally split into storage-specific infrastructure projects.
- [X] Step 5.2c Namespace updates for Agent files - Status: complete
- [X] Step 5.2d Create `AddAgentTelemetryServices()` registration - Status: complete
- [X] Step 5.3 Update project references - Status: complete/superseded; completed because superseded by specs/034-package-manager-adoption
  Superseded evidence: reference topology now includes storage abstraction/infrastructure projects beyond original 021.2 target.
- [X] Step 5.4 Update `DevOpsMigrationPlatform.slnx` - Status: complete
- [X] Step 5.5 Delete empty folders from base `Infrastructure` - Status: complete/superseded; completed because superseded by additional storage split
  Superseded evidence: base infrastructure remains slim while storage concerns live in dedicated projects.

## Phase 6 — Reference topology cleanup and verification
- [X] Step 6.1 Fix test project references and split Infrastructure tests - Status: complete
- [X] Step 6.2 Build gate (`dotnet clean && dotnet build --no-incremental`) - Status: complete
- [ ] Step 6.3 Test gate (`dotnet test`) - Status: incomplete
  Evidence: Full `dotnet test DevOpsMigrationPlatform.slnx --no-build` did not complete in this reconciliation session and was stopped.
- [ ] Step 6.4 Reference topology audit evidence - Status: incomplete
  Evidence: No persisted audit artifact proving a completed topology audit exists for this reconciliation cycle.
- [ ] Step 6.5 Scenario smoke test via `launch.json` profile - Status: incomplete
  Evidence: No scenario debug-profile execution evidence was produced in this reconciliation session.
- [ ] Step 6.6 Aspire orchestration verification (`dotnet run --project AppHost`) - Status: incomplete
  Evidence: AppHost run verification was not executed in this reconciliation session.
- [X] Step 6.7 Update `build.ps1` for new projects - Status: complete/superseded; completed because superseded by solution-driven build and later project additions
  Superseded evidence: `build.ps1` builds via solution and packages the current host/agent topology, including TfsMigrationAgent.
- [ ] Step 6.8 NuGet package distribution audit - Status: incomplete
  Evidence: No session evidence was captured for `dotnet list package` boundary verification across newly split projects.
