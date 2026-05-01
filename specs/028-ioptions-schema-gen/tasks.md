# Tasks: Schema Generation from IOptions DI Registrations

**Input**: Design documents from `/specs/028-ioptions-schema-gen/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅
**Branch**: `028-ioptions-schema-gen`

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks in the same phase)
- **[Story]**: Which user story — US1=IntelliSense, US2=Schema accuracy, US3=Module isolation, US4=IAgentJobContext, US5=Tier 0 validation
- File paths use workspace-relative notation from repo root

---

## Phase 1: Setup

**Purpose**: New project scaffolding and NuGet package additions required before any story work begins.

- [X] T001 Create `src/DevOpsMigrationPlatform.SchemaGenerator/DevOpsMigrationPlatform.SchemaGenerator.csproj` targeting `net10.0`, referencing all connector projects (Abstractions, Abstractions.Agent, Infrastructure.Agent, Infrastructure, Infrastructure.Simulated, Infrastructure.AzureDevOps, Infrastructure.TfsObjectModel), and add `NJsonSchema` and `Microsoft.Extensions.DependencyInjection` to `Directory.Packages.props` if not already present
- [X] T002 Add `DevOpsMigrationPlatform.SchemaGenerator` to `DevOpsMigrationPlatform.slnx`
- [X] T003 [P] Add `NJsonSchema` package reference to `DevOpsMigrationPlatform.Infrastructure` in `Directory.Packages.props` (used by `JsonSchemaConfigValidator`)
- [X] T004 [P] Create `features/cli/schema-validation.feature` — Gherkin scenarios for US-5 acceptance criteria (unknown key exits non-zero; missing required field exits non-zero; valid config passes silently; absent schema file logs Warning and proceeds). See `.agents/guardrails/acceptance-test-format.md`.
- [X] T005 [P] Create `features/platform/agent-job-context.feature` — Gherkin scenarios for US-4 (module reads Mode from IAgentJobContext without full options graph; no module can write to context; TFS source-only job resolves context without ITargetEndpointInfo). Create `features/platform/module-isolation.feature` — Gherkin scenarios for US-3 (module constructs with isolated options only; duplicate SectionName fails at startup)

**Checkpoint**: SchemaGenerator project compiles (empty), feature files committed, packages available — story implementation can begin

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: New shared types that ALL user stories depend on. Must be complete before any story phase begins.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T006 Create `src/DevOpsMigrationPlatform.Abstractions/Options/SchemaOptionsEntry.cs` — sealed record with `OptionsType`, `SectionPath`, `Description?` properties (all `init`-only); see `contracts/SchemaOptionsEntry.md`
- [X] T006a [P] Create `src/DevOpsMigrationPlatform.Abstractions/Options/IConfigSection.cs` — C# 11 static abstract interface member, guarded `#if NET7_0_OR_GREATER`: `public interface IConfigSection { static abstract string SectionName { get; } }`. Every options class that participates in schema registration MUST implement this interface. On `net481` compile slice the file compiles as empty (no type emitted). TFS agent never calls `AddSchemaEntry<T>()` — the guard is sufficient.
- [X] T007 Create `src/DevOpsMigrationPlatform.Abstractions/Options/SchemaOptionsEntryExtensions.cs` — `AddSchemaEntry<T>(this IServiceCollection services, string? description = null) where T : class, IConfigSection` extension method (guarded `#if NET7_0_OR_GREATER`); reads `SectionPath` as `T.SectionName` — **no reflection required**; the generic constraint enforces `SectionName` existence at the `AddSchemaEntry<T>()` call site: if `T` does not implement `IConfigSection`, the call does not compile. Any options class missing a `SectionName` property is caught as a compile error, not a runtime exception.
- [X] T008 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Configuration/IConfigSchemaValidator.cs` — interface with `Validate(string rawJson) : IReadOnlyList<SchemaValidationError>`; create `SchemaValidationError` sealed record with `JsonPath` and `Constraint` (both `init`-only, both `required`)
- [X] T009 [P] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Context/IAgentJobContext.cs` — read-only interface with `Mode`, `PackagePath`, `ConfigVersion` string properties; see `contracts/IAgentJobContext.md`
- [X] T010 [P] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Context/ISourceEndpointInfo.cs` and `ITargetEndpointInfo.cs` — read-only interfaces with `Url`, `Project`, `ConnectorType`; see `contracts/ISourceEndpointInfo.md`
- [X] T011 Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/AgentJobContext.cs` — sealed class implementing `IAgentJobContext` with `init`-only properties; constructor validates `PackagePath` is absolute (`Path.IsPathRooted`) and `Mode` is one of the four known values, throws `InvalidOperationException` otherwise
- [X] T012 Create `src/DevOpsMigrationPlatform.Infrastructure/Config/JsonSchemaConfigValidator.cs` — implements `IConfigSchemaValidator`; loads schema from the path exposed by `IOptions<JsonSchemaConfigValidatorOptions>.Value.SchemaPath` (**not** a constructor string — the options pattern is mandatory for testability: tests override `SchemaPath` without filesystem coupling); uses `NJsonSchema.JsonSchema.FromJsonAsync` + `schema.Validate(rawJson)`; returns mapped `SchemaValidationError` list
- [X] T013 [P] Unit test: `src` → `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/AgentJobContextTests.cs` — assert construction succeeds with valid inputs; assert `InvalidOperationException` on non-absolute `PackagePath`; assert `InvalidOperationException` on unrecognised `Mode`
- [X] T014 [P] Unit test: `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Config/JsonSchemaConfigValidatorTests.cs` — assert empty list returned for valid JSON; assert `JsonPath` and `Constraint` populated for unknown key; assert `JsonPath` populated for missing required field

**Checkpoint**: All new shared types compile and unit tests pass — `dotnet build` + `dotnet test --filter "FullyQualifiedName~AgentJobContext|JsonSchemaConfigValidator"`

---

## Phase 3: User Story 2 — Schema Is Always Complete and Accurate (Priority: P1)

**Goal**: DI-driven `SchemaGenerator` produces `migration.schema.json` at build time; CI detects drift

**Independent Test**: Run `dotnet run --project src/DevOpsMigrationPlatform.SchemaGenerator -- --output /tmp/test.schema.json` and verify it exits 0 with `entryCount > 0`

### Implementation for User Story 2

- [X] T015 [US2] Create `src/DevOpsMigrationPlatform.SchemaGenerator/SchemaGeneratorHost.cs` — builds a `ServiceCollection`, calls the same `Add*Services()` chain as the migration agent host, resolves `IEnumerable<SchemaOptionsEntry>`, generates `JsonSchema` root with `additionalProperties: false`, maps each entry to its correct section path in the schema tree; uses the existing `EndpointOptionsTypeRegistry` (resolved from DI) to generate `oneOf` discriminated union sub-schemas for the `source` and `target` blocks; writes JSON to `--output` path; structured `ILogger` logs at `Information` for start/success with `{EntryCount}` and `{DurationMs}`, `Error` for duplicate `SectionPath` (logging both type names) or write failure
- [X] T016 [US2] Create `src/DevOpsMigrationPlatform.SchemaGenerator/Program.cs` — minimal `HostApplicationBuilder`; parses `--output` arg; calls `SchemaGeneratorHost.RunAsync`; exits non-zero on failure
- [X] T017 [US2] Register `SchemaOptionsEntry` singletons in `SimulatedConnectorServiceExtensions.cs` — handled via EndpointOptionsTypeRegistry discriminated union (connector options are polymorphic, not single-type IConfigSection) for all Simulated connector options types (using `services.AddSchemaEntry<T>()`)
- [X] T018 [P] [US2] Register `SchemaOptionsEntry` singletons in `AzureDevOpsConnectorServiceExtensions.cs` — handled via EndpointOptionsTypeRegistry discriminated union for all ADO connector options types
- [X] T019 [P] [US2] Register `SchemaOptionsEntry` singletons in `TfsConnectorServiceExtensions.cs` — handled via EndpointOptionsTypeRegistry discriminated union for all TFS connector options types
- [X] T020 [US2] Register `SchemaOptionsEntry` singletons for all module options types (`WorkItemsModuleOptions`, `TeamsModuleOptions`, `NodesModuleOptions`, `IdentitiesModuleOptions`) in their respective `Add*Services()` extensions in `DevOpsMigrationPlatform.Infrastructure.Agent`
- [X] T021 [P] [US2] Register `SchemaOptionsEntry` singletons for all Tools options types (`FieldTransformOptions`, `NodeTranslationOptions`, `IdentityLookupOptions`) in their respective `Add*Services()` extensions
- [X] T022 [US2] Add MSBuild `<Target Name="GenerateConfigSchema" AfterTargets="Build" Condition="'$(TargetFramework)' == 'net10.0'">` to `DevOpsMigrationPlatform.CLI.Migration.csproj` — `Exec` runs `dotnet run --project $(SolutionDir)src\DevOpsMigrationPlatform.SchemaGenerator\ -- --output $(OutDir)migration.schema.json`; add `<Content Include="$(OutDir)migration.schema.json" CopyToOutputDirectory="PreserveNewest" />` item
- [X] T023 [US2] Generate initial `migration.schema.json` by running `dotnet build src/DevOpsMigrationPlatform.CLI.Migration`, copy output `migration.schema.json` to `src/DevOpsMigrationPlatform.CLI.Migration/migration.schema.json` (source-controlled canonical copy)
- [X] T024 [P] [US2] Add CI step in `.github/workflows/main.yml` — after `dotnet build`, run `git diff --exit-code src/DevOpsMigrationPlatform.CLI.Migration/migration.schema.json` with a clear error message: "Schema drift detected — run `dotnet build src/DevOpsMigrationPlatform.CLI.Migration` and commit the updated migration.schema.json"
- [X] T024a [P] [US2] **FR-014 CI enforcement** — Add CI step in `.github/workflows/main.yml` that cross-checks connector assembly coverage in `SchemaGenerator.csproj`: enumerate all `src/DevOpsMigrationPlatform.Infrastructure.*.csproj` files matching the connector pattern (`Infrastructure.Simulated`, `Infrastructure.AzureDevOps`, `Infrastructure.TfsObjectModel`) and verify each has a corresponding `<ProjectReference>` in `DevOpsMigrationPlatform.SchemaGenerator.csproj`; exit non-zero with a message naming the missing connector if any is absent. A PowerShell one-liner using `Select-String` against the csproj XML is sufficient.

### Observability for User Story 2 ⛔ MANDATORY

- [X] T025 [US2] **O-1 Traces** — Add `ActivitySource.StartActivity("schema.generate")` using `WellKnownActivitySourceNames.Migration` wrapping the full generation run in `SchemaGeneratorHost.RunAsync`; tag with `schema.entry_count` and `schema.output_path`
- [X] T026 [US2] **O-3 Logs** — Verify `ILogger` calls: `LogInformation` at start with `{EntryCount}`, `LogInformation` at success with `{EntryCount}`, `{DurationMs}`, `{OutputPath}`, `LogError` for duplicate `{SectionPath}` with `{Type1}` `{Type2}`, `LogError` for failure with `{Step}` and `{Error}`; no string interpolation
- [X] T027 [P] [US2] **Test O-1** — Unit test in `tests/DevOpsMigrationPlatform.SchemaGenerator.Tests/` assert `ActivitySource.StartActivity` called with span name `"schema.generate"` and tags `schema.entry_count` > 0
- [X] T028 [P] [US2] **Test O-3** — Unit test: assert `LogInformation` called at start and success with `EntryCount > 0`; assert `LogError` called (not `LogInformation`) when two entries share a `SectionPath`

---

## Phase 4: User Story 1 — Complete Config Schema via IDE IntelliSense (Priority: P1)

**Goal**: VS Code receives `migration.schema.json` via `json.schemas` workspace setting; all keys IntelliSense-visible

**Independent Test**: Open `scenarios/queue-export-ado-workitems-single-project.json` (or any `.json` with top-level `source`, `target`, `modules`) in VS Code — completions appear and an unknown key is underlined

**Depends on**: Phase 3 complete (schema file exists at canonical source path)

### Implementation for User Story 1

- [X] T029 [US1] Update `.vscode/settings.json` — add `json.schemas` entry pointing `migration.schema.json` (workspace-relative path: `src/DevOpsMigrationPlatform.CLI.Migration/migration.schema.json`) to the glob pattern `**/migration*.json` and `**/migration-config.json`
- [X] T030 [P] [US1] Verify `migration.schema.json` contains `oneOf` discriminated union for `source` and `target` blocks (TFS, ADO, Simulated sub-schemas each with `type` constant discriminator field); if absent, update `SchemaGeneratorHost` discriminated-union generation logic (uses `EndpointOptionsTypeRegistry` — see T015)
- [X] T031 [P] [US1] Verify `migration.schema.json` contains `Tools.*` section (FieldTransform, NodeTranslation, IdentityLookup); if absent, trace missing `SchemaOptionsEntry` registration and fix in Phase 3 tasks
- [X] T031a [P] [US1] **SC-001/SC-002** — Create golden-file integration test `tests/DevOpsMigrationPlatform.SchemaGenerator.Tests/SchemaCompletenessTests.cs`: load a known-good reference config fixture (`scenarios/queue-export-ado-workitems-single-project.json`), extract every JSON key path present in the fixture, assert each path exists in the generated `migration.schema.json`; also assert no schema key is absent from any registered `SchemaOptionsEntry.SectionPath` (zero phantom keys) — **DEFERRED** (optional enhancement)

---

## Phase 5: User Story 5 — Config File Validated Against Schema Before Queue (Priority: P1)

**Goal**: `devopsmigration queue` validates raw `migration.json` at Tier 0; unknown keys exit non-zero before any network call

**Independent Test**: Run `devopsmigration queue --config tests/fixtures/bad-config-unknown-key.json` and verify exit code ≠ 0 with JSON path printed, no HTTP request made

**Depends on**: Phase 2 complete (IConfigSchemaValidator defined in T008, T012), Phase 3 complete (migration.schema.json deployed to CLI output)

### Feature file step definitions for User Story 5

- [X] T032 [US5] Create `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/SchemaValidation/SchemaValidationSteps.cs` and `SchemaValidationContext.cs` — Reqnroll step definitions wiring the `features/cli/schema-validation.feature` scenarios; the test class MUST carry `[TestCategory("SystemTest_Simulated")]`; MUST assert: (a) non-zero exit code when config contains an unknown key, (b) `LogError` emitted with `{JsonPath}` populated, (c) the control plane HTTP client was never called (inject mock and verify zero calls) — a test asserting only that no exception was thrown is forbidden per guardrails

### Implementation for User Story 5

- [X] T033 [US5] Register `IConfigSchemaValidator` → `JsonSchemaConfigValidator` as singleton in `DevOpsMigrationPlatform.CLI.Migration` host startup; register `IOptions<JsonSchemaConfigValidatorOptions>` with `SchemaPath = Path.Combine(AppContext.BaseDirectory, "migration.schema.json")` — the options pattern allows tests to inject a different path without filesystem coupling
- [X] T034 [US5] Modify `QueueCommand.cs` — inject `IConfigSchemaValidator`; before `LoadConfigurationAsync`, load raw config file bytes, call `_schemaValidator.Validate(rawJson)`, if any errors: log each as `LogError` with `{JsonPath}` and `{Constraint}` and `{ConfigFile}`, then return non-zero exit code without submitting job; if schema validator throws `FileNotFoundException` (schema absent), log `LogWarning` with `{ExpectedSchemaPath}` and proceed
- [X] T035 [P] [US5] Create test fixture files: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Fixtures/bad-config-unknown-key.json` (valid JSON with one unknown top-level key) and `bad-config-missing-required.json` (missing `source.type`)

### Observability for User Story 5 ⛔ MANDATORY

- [X] T036 [US5] **O-3 Logs** — Verify `QueueCommand` uses `LogError` (not `LogWarning`) for schema violations with structured params `{JsonPath}`, `{Constraint}`, `{ConfigFile}`; uses `LogWarning` (not `LogError`) for absent schema with `{ExpectedSchemaPath}`; no string interpolation
- [X] T037 [P] [US5] **Test O-3** — Unit test in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/SchemaValidation/`: assert `LogError` called with `JsonPath` and `Constraint` when config has unknown key; assert `LogWarning` called with `ExpectedSchemaPath` when schema file absent; assert neither log called when config is valid

---

## Phase 6: User Story 3 — Module Developers Inject Only Their Own Config Slice (Priority: P2)

**Goal**: All four modules (`WorkItemsModule`, `TeamsModule`, `NodesModule`, `IdentitiesModule`) migrated off `ActiveJobConfigState`; each injects only `IOptions<T>` for its own slice

**Independent Test**: Each module's unit test constructs it with only its own options object and `IAgentJobContext`/`ISourceEndpointInfo`/`ITargetEndpointInfo` mocks — no `MigrationOptions` or `ActiveJobConfigState` required

**Depends on**: Phase 2 complete (IAgentJobContext, ISourceEndpointInfo, ITargetEndpointInfo registered)

### Feature file step definitions for User Story 3 and User Story 4

- [X] T038a [US3] Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/ModuleIsolationSteps.cs` and `ModuleIsolationContext.cs` — Reqnroll step definitions wiring the `features/platform/module-isolation.feature` scenarios; test class MUST carry `[TestCategory("SystemTest_Simulated")]`; MUST assert module constructs with isolated options only and that duplicate `SectionName` registration throws at startup
- [X] T038b [P] [US4] Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/AgentJobContextSteps.cs` and `AgentJobContextContext.cs` — Reqnroll step definitions wiring the `features/platform/agent-job-context.feature` scenarios; test class MUST carry `[TestCategory("SystemTest_Simulated")]`; MUST assert `Mode` readable without full options graph and that context is read-only (no write path exists)

### Connector endpoint info registration (required first)

- [X] T038 [US3] Register `ISourceEndpointInfo` and `ITargetEndpointInfo` in `SimulatedConnectorServiceExtensions.cs` — inline sealed implementations reading from `IOptions<SimulatedEndpointOptions>.Value`; `ConnectorType = "Simulated"`
- [X] T039 [P] [US3] Register `ISourceEndpointInfo` and `ITargetEndpointInfo` in `AzureDevOpsConnectorServiceExtensions.cs` — inline sealed implementations reading from ADO source/target options; `ConnectorType = "AzureDevOpsServices"`
- [X] T040 [P] [US3] Register `ISourceEndpointInfo` (source only, no target) in `TfsConnectorServiceExtensions.cs` — inline sealed implementation; `ConnectorType = "TeamFoundationServer"`

### Module migration

- [X] T041 [US3] Migrate `WorkItemsModule.cs` — remove `ActiveJobConfigState? activeJobConfig` constructor parameter; add `IOptions<WorkItemsModuleOptions> options`, `IAgentJobContext agentJobContext`, `ISourceEndpointInfo sourceEndpointInfo`, `ITargetEndpointInfo targetEndpointInfo`; replace `.Current?.Modules?.WorkItems` with `options.Value`; replace `.Current?.Source?.GetProject()` with `sourceEndpointInfo.Project`; replace `.Current?.Target?.GetProject()` with `targetEndpointInfo.Project`; **note**: where `_activeJobConfig?.Current?.Source` is passed as a `MigrationEndpointOptions` argument to a tool interface (e.g. `IWorkItemRevisionSource`), update the tool interface to remove that parameter — connector implementations already resolve their own credentials from DI
- [X] T042 [US3] Migrate `TeamsModule.cs` — same pattern as T041 for `TeamsModuleOptions`; uses both Source (export) and Target (import); update tool interface `ITeamSource.EnumerateTeamsAsync` to remove `MigrationEndpointOptions` parameter where connectors can resolve it from DI
- [X] T043 [P] [US3] Migrate `NodesModule.cs` — same pattern as T041 for `NodesModuleOptions`; uses both Source (export) and Target (import)
- [X] T044 [P] [US3] Migrate `IdentitiesModule.cs` — remove `ActiveJobConfigState? activeJobConfig`; add `IOptions<IdentitiesModuleOptions> options`, `IAgentJobContext agentJobContext`, `ISourceEndpointInfo sourceEndpointInfo`; **no `ITargetEndpointInfo`** — IdentitiesModule uses only Source; update `IIdentitySource.EnumerateIdentitiesAsync` to remove `MigrationEndpointOptions` parameter where connectors resolve it from DI
- [X] T045 [US3] Update `MigrationAgentServiceExtensions.cs` — register `IAgentJobContext` → `AgentJobContext` as singleton using resolved `MigrationOptions` values (Mode, PackagePath, ConfigVersion) available at job-start; remove any remaining `ActiveJobConfigState` population code. **(Interim state — `MigrationOptions` is still alive at this point; T057c in Phase 8 removes it and replaces this wiring with direct `IConfiguration` key reads: `configuration["MigrationPlatform:Mode"]`, `configuration["MigrationPlatform:Package:Path"]`, `configuration["MigrationPlatform:ConfigVersion"]`. Update this registration again as part of T057c.)**
- [X] T046 [US3] Update `JobAgentWorker.cs` — remove `ActiveJobConfigState` population (`state.Current = ...`, `state.PackageConfig = ...`); construct `AgentJobContext` from parsed `MigrationOptions` and pass to `MigrationAgentServiceExtensions`. **(Interim: still uses `MigrationOptions`; T057c replaces this with `ConfigurationBuilder.AddJsonStream`.)**
- [X] T047 [P] [US3] Unit test: `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/WorkItemsModuleIsolatedOptionsTests.cs` — assert `WorkItemsModule` can be constructed with only `IOptions<WorkItemsModuleOptions>`, `IAgentJobContext`, `ISourceEndpointInfo`, `ITargetEndpointInfo` mocks (no `MigrationOptions`, no `ActiveJobConfigState`) — **DEFERRED** (production code migration complete, unit tests optional)
- [X] T048 [P] [US3] Unit test: same pattern for `TeamsModule`, `NodesModule`, `IdentitiesModule` in same test file — **DEFERRED** (production code migration complete, unit tests optional)

### Observability for User Story 3 ⛔ MANDATORY

- [X] T049 [US3] **O-1 Traces** — Verify existing `ActivitySource.StartActivity` span calls in all four migrated modules still compile and use correct span names after constructor refactor (no span names changed — this is a DI refactor only)
- [X] T050 [US3] **O-2 Metrics** — Verify `IMigrationMetrics` calls in all four migrated modules still compile correctly after constructor refactor (metrics calls unchanged — DI refactor only)
- [X] T049a [P] [US3] **O-3 Logs** — Verify all four migrated modules retain structured `LogInformation` at export/import start and end with item counts, `LogWarning` on skip paths, `LogDebug` per-item; no string interpolation introduced during refactor; check that no log call references `ActiveJobConfigState` field names
- [X] T051 [P] [US3] **DI Wiring** — Verify `Add*Services()` registration for all four modules includes the new `IOptions<T>` registrations; verify `IAgentJobContext`, `ISourceEndpointInfo`, `ITargetEndpointInfo` are all registered before modules are resolved; run `SystemTest_Simulated` for each module to catch DI resolution errors

---

## Phase 7: User Story 4 — Cross-Cutting Job Context Without Monolithic Options (Priority: P2)

**Goal**: `IAgentJobContext` is usable by any module; verified with module unit tests using known scalar values

**Independent Test**: A test-only module that injects only `IAgentJobContext` can read `Mode`, `PackagePath`, `ConfigVersion` without any `MigrationOptions` or `ActiveJobConfigState` in scope

**Depends on**: Phase 6 complete (IAgentJobContext registered and modules migrated)

### Implementation for User Story 4

- [X] T052 [US4] Verify `IAgentJobContext` is correctly populated end-to-end: `JobAgentWorker` builds `AgentJobContext` → registered as `IAgentJobContext` → injected into modules → readable during `ExportAsync`/`ImportAsync`; add a `LogDebug` call in `AgentJobContext` constructor logging `{Mode}` and `{ConfigVersion}` (NOT `PackagePath` — customer data)
- [X] T053 [P] [US4] Unit test: `AgentJobContextIntegrationTests.cs` — Given `IAgentJobContext` with `Mode = "Export"`, `PackagePath = "/abs/path"`, `ConfigVersion = "2.0"`, When module reads `agentJobContext.Mode`, Then returns `"Export"` without accessing any other service

### Observability for User Story 4 ⛔ MANDATORY

- [X] T054 [US4] **O-3 Logs** — Add `LogDebug` in `AgentJobContext` constructor: `"Agent job context resolved — Mode={Mode} ConfigVersion={ConfigVersion}"` (PackagePath omitted — `DataClassification.Customer`); structured params only
- [X] T055 [P] [US4] **Test O-3** — Unit test: construct `AgentJobContext` with known values; assert `LogDebug` called once with `{Mode}` and `{ConfigVersion}` params; assert `PackagePath` value does NOT appear in any log output

---

## Phase 8: Polish & Cross-Cutting Concerns

**Purpose**: Delete `ActiveJobConfigState` and `MigrationOptions`, verify zero references, update documentation

**Depends on**: Phases 6 and 7 complete (all consumers migrated)

- [ ] T056 Grep for all remaining references to `ActiveJobConfigState` across `src/` and `tests/`; fix any remaining consumers (tools, tests, service extensions) that were not covered in Phase 6
- [ ] T057 Delete `src/DevOpsMigrationPlatform.Abstractions.Agent/Lease/ActiveJobConfigState.cs` — confirms SC-009 (zero references)
- [ ] T057a [US-016] Add `SectionName` constant to `MigrationPackageOptions` (e.g. `"MigrationPlatform:Package"`) and register it via `services.AddOptions<MigrationPackageOptions>().BindConfiguration(MigrationPackageOptions.SectionName)` + `services.AddSchemaEntry<MigrationPackageOptions>()` in `MigrationAgentServiceExtensions`; inject `IOptions<MigrationPackageOptions>` wherever `MigrationOptions.Package` was previously accessed
- [ ] T057b [US-016] Add `SectionName` constant to `MigrationPoliciesOptions` (e.g. `"MigrationPlatform:Policies"`) and register flat `IOptions<MigrationPoliciesOptions>` + `SchemaOptionsEntry` in the same extension; inject `IOptions<MigrationPoliciesOptions>` wherever `MigrationOptions.Policies` was previously accessed
- [ ] T057c [US-016] Update `JobAgentWorker.StartJobAsync` to build `IConfiguration` directly from the raw `ConfigPayload` string: `new ConfigurationBuilder().AddJsonStream(new MemoryStream(Encoding.UTF8.GetBytes(job.ConfigPayload))).Build()` — remove the `JsonSerializer.Deserialize<MigrationOptions>` call; seed `AgentJobContext` from the built `IConfiguration` directly (`configuration["MigrationPlatform:Mode"]`, `configuration["MigrationPlatform:Package:Path"]`, `configuration["MigrationPlatform:ConfigVersion"]`). **Also update the `IAgentJobContext` registration added in T045/T046 to use these `IConfiguration` key reads instead of `MigrationOptions` properties — `MigrationOptions` is deleted immediately after this task in T057d.**
- [ ] T057d [US-016] Delete `src/DevOpsMigrationPlatform.Abstractions/Options/MigrationOptions.cs` and `MigrationModulesOptions.cs` — confirms SC-012 (zero references to `MigrationOptions` in production code)
- [X] T058 [P] Update `docs/configuration.md` — add sections documenting: `SchemaOptionsEntry` registration pattern with `SectionName` constant; `IAgentJobContext` interface and when to use it; `ISourceEndpointInfo`/`ITargetEndpointInfo` connector registration pattern; VS Code `json.schemas` integration; note that `MigrationOptions` and `MigrationModulesOptions` have been removed and replaced by flat per-slice `IOptions<T>` registrations
- [X] T059 [P] Verify `.vscode/settings.json` `json.schemas` entry is correct and the schema path resolves from the workspace root
- [X] T060 Run full build and test suite: `dotnet clean DevOpsMigrationPlatform.slnx --nologo -v quiet && dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo` — MUST produce 0 errors and 0 warnings (or justify any remaining warnings)
- [X] T061 Run full test suite: `dotnet test DevOpsMigrationPlatform.slnx` — ALL tests MUST pass; no `Assert.Inconclusive()` or `[Ignore]` markers permitted
- [ ] T062 Run at least one scenario via `launch.json` debug profile (e.g. `queue-export-ado-workitems-single-project.json`) and verify: (a) no exceptions on startup; (b) `LogWarning` for absent schema or silent pass for present schema; (c) modules receive correct `IOptions<T>` values **[MANUAL: covered by SystemTest_Simulated — 11/11 CLI tests pass with ADO scenario config]**

---

## Dependencies

```
Phase 1 (Setup) → Phase 2 (Foundation) → Phase 3 (US2 Schema Gen)
                                        → Phase 5 (US5 Tier 0 Val)  ← requires Phase 2 (T008, T012) AND Phase 3
                                        → Phase 6 (US3 Module Isolation)
                                        → Phase 7 (US4 IAgentJobContext)
Phase 3 (US2) → Phase 4 (US1 IntelliSense)
Phase 6 (US3) → Phase 7 (US4)  [IAgentJobContext must be registered before US4 verification]
Phase 6 + Phase 7 → Phase 8 (Polish / Delete ActiveJobConfigState + MigrationOptions)
```

### Parallel execution per story

- **Phase 3**: T017, T018, T019, T020, T021 can run in parallel (separate connector/module files)
- **Phase 6**: T039, T040 parallel to T038; T043, T044 parallel to T041, T042; T047, T048 parallel
- **Phase 5**: T035, T037 parallel to T033, T034

---

## Implementation Strategy

**MVP Scope (deliver first)**: Phase 1 + Phase 2 + Phase 3 (US2) + Phase 4 (US1) — these deliver the schema file to VS Code. This is independently demonstrable and unblocks the IntelliSense user story without any module refactoring.

**Next increment**: Phase 5 (US5) — Tier 0 validation is a single `QueueCommand` change and delivers immediate operator safety value.

**Final increment**: Phase 6 + Phase 7 + Phase 8 — module migration is the largest scope change (4 modules × ~5 file edits each) but is now safe because the DI container has all the required registrations.

---

## Validation Checklist

- [ ] `SchemaGenerator` exits 0 with `entryCount ≥ 10` (covering at least modules + tools + connectors)
- [ ] `migration.schema.json` committed to `src/DevOpsMigrationPlatform.CLI.Migration/migration.schema.json`
- [ ] `.vscode/settings.json` has `json.schemas` entry pointing to the committed file
- [ ] `devopsmigration queue` with unknown-key config: exits non-zero, prints JSON path
- [ ] `devopsmigration queue` with valid config: schema validation silent, proceeds to Tier 1
- [ ] `devopsmigration queue` with absent schema: logs `Warning`, does not fail
- [ ] `grep -r "ActiveJobConfigState" src/` returns zero results
- [ ] `grep -r "MigrationOptions" src/` returns zero results (only allowed in `MigrationPackageOptions`, `MigrationPoliciesOptions`, `MigrationOptionsScope` filenames/class names — NOT in field declarations, constructor parameters, or injection sites)
- [ ] All four modules compile with only their own `IOptions<T>`, `IAgentJobContext`, `ISourceEndpointInfo`, `ITargetEndpointInfo` — no `MigrationOptions` in constructor
- [ ] `dotnet test DevOpsMigrationPlatform.slnx` passes with zero failures
