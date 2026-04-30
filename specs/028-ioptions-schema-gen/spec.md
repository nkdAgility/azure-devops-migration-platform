# Feature Specification: Schema Generation from IOptions DI Registrations

**Feature Branch**: `028-ioptions-schema-gen`  
**Created**: 2026-04-30  
**Status**: Draft  
**Input**: User description: "Schema Generation from IOptions DI Registrations: migrate all config options classes to flat IOptions<T> self-registration pattern with SectionName constants, introduce SchemaOptionsEntry registry so schema generator derives JSON Schema from the same DI registrations the running system uses, add IAgentJobContext for cross-cutting scalar values, and wire schema generation into MSBuild and CI"

---

## Architecture References

The following documents were read as part of the architecture check for this spec:

| Document | Status |
| -------- | ------ |
| `docs/architecture.md` | Confirmed accurate — no conflicts with this feature |
| `docs/configuration.md` | **Has discrepancies** — see `discrepancies.md`. Does not describe SchemaOptionsEntry, IMigrationJobContext, json.schemas integration, or the DI-driven schema generation pattern |
| `.agents/guardrails/system-architecture.md` | Confirmed — rules 21 (mandatory reuse), 24 (module/tool identifier derivation), 25 (observability) apply. No conflicts. |
| `agents.md` | Confirmed — binding entry point read |

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Complete Config Schema via IDE IntelliSense (Priority: P1)

A platform operator opens a `migration.json` file in VS Code and receives IntelliSense completions and validation for every configurable key — including `Tools.FieldTransform`, `Tools.NodeTranslation`, `Tools.IdentityLookup`, all module options, and both connector endpoint types. No key is silently missing from the schema. The operator can discover and correctly configure the platform without reading documentation.

**Why this priority**: The schema gap between actual configurable keys and the generated schema causes silent misconfiguration. Operators set keys that are silently ignored, or cannot discover valid options. This is the primary user-visible defect this feature corrects.

**Independent Test**: With an operator-facing `migration.json` open in VS Code, all top-level sections (source, target, modules, Tools) offer correct completions and hover documentation. Adding an unknown key triggers a validation warning.

**Acceptance Scenarios**:

1. **Given** a VS Code workspace with the migration platform's `json.schemas` registration active, **When** an operator opens or creates a `migration.json` file, **Then** all top-level sections receive IntelliSense completions sourced from the schema file committed to the repository.
2. **Given** the schema is registered, **When** the operator types under `Tools.*`, **Then** completions appear for all tool sections (FieldTransform, NodeTranslation, IdentityLookup) with correct key names and value constraints.
3. **Given** the schema is registered, **When** the operator adds an unrecognised key at any nesting level, **Then** the IDE flags it as an unknown property.
4. **Given** the operator configures `source.type: TeamFoundationServer`, **When** the schema evaluates the `source` block, **Then** only the TFS-specific fields are offered (not ADO-specific fields), because the schema uses a `oneOf` discriminated union for endpoint types.

---

### User Story 2 — Schema Is Always Complete and Accurate (Priority: P1)

A platform developer adds a new options class for a new module or tool. After the build, the committed schema automatically includes the new section. No separate manual schema update is required. If the developer forgets to register the new options in DI, the section is missing from both the schema AND from the running system — the omission is immediately visible.

**Why this priority**: The current manual schema maintenance process is error-prone and has already produced a gap between configured `Tools.*` keys and the schema. Automating schema derivation from DI registrations eliminates this class of error entirely.

**Independent Test**: Can be fully tested by adding a new options class with a `SectionName`, registering it in the appropriate `Add*Services()` extension, rebuilding, and verifying the generated schema contains the new section. Delivers value independently of all other stories.

**Acceptance Scenarios**:

1. **Given** a new options class is registered in DI with a `SectionName`, **When** the build runs, **Then** the schema generator detects the registration and includes the new section in the generated schema.
2. **Given** a developer adds a new options class but does NOT register it in DI, **When** the build runs, **Then** the section is absent from the generated schema — consistent with it also being absent from the running system's configuration binding.
3. **Given** a committed schema is present, **When** CI detects that a rebuild produces a schema that differs from the committed version, **Then** the CI job fails with a diff, prompting the developer to commit the updated schema.
4. **Given** the schema is generated from DI registrations, **When** the running system binds configuration, **Then** every key in the generated schema corresponds to a real configuration binding — there are no schema keys that the running system silently ignores.

---

### User Story 5 — Config File Validated Against Schema Before Queue Submission (Priority: P1)

When an operator runs `devopsmigration queue`, the CLI validates the raw `migration.json` file against `migration.schema.json` (deployed alongside the CLI binary) before deserialising it or submitting the job. Unknown keys, wrong value types, and missing required fields are reported immediately as Tier 0 errors — no job is submitted and no network call is made. The operator sees the exact JSON path and constraint that failed.

**Why this priority**: The current `ValidateConfiguration` step works on the already-deserialised object — unknown keys are silently dropped by the deserialiser before validation even runs. JSON Schema validation on the raw file catches structural problems that object-level validation cannot, and it catches them locally before any control plane interaction.

**Independent Test**: Can be fully tested by running `devopsmigration queue` with a `migration.json` containing an unknown top-level key. The command must exit with a non-zero code and print the violation before attempting to contact the control plane.

**Acceptance Scenarios**:

1. **Given** a `migration.json` with an unknown key at any nesting level, **When** the operator runs `devopsmigration queue`, **Then** the CLI exits with a non-zero code and prints the JSON path and a description of the violation — no job is submitted.
2. **Given** a `migration.json` with a required field missing (e.g. `source.type` absent), **When** the operator runs `devopsmigration queue`, **Then** the CLI exits with a non-zero code and identifies the missing field — no job is submitted.
3. **Given** a `migration.json` that is fully valid against the schema, **When** the operator runs `devopsmigration queue`, **Then** schema validation passes silently and the command proceeds to Tier 1 (connectivity checks).
4. **Given** `migration.schema.json` is absent from the CLI output directory, **When** the operator runs `devopsmigration queue`, **Then** the CLI logs a warning that schema validation was skipped and proceeds — schema validation is strict when the schema is present, but its absence is not a hard error.

---

### User Story 3 — Module Developers Inject Only Their Own Config Slice (Priority: P2)

A module developer writing or maintaining a module constructor only needs to declare a dependency on that module's own configuration type. They do not need to receive the entire platform options graph and navigate into it. This makes the module's config requirements explicit and discoverable from the constructor signature alone.

**Why this priority**: Injecting the full options graph into every module creates invisible coupling — adding a field to one module's config requires navigating the shared graph, and the module cannot be tested without constructing the entire graph. Isolated injection makes modules independently testable and their config contracts explicit.

**Independent Test**: A module unit test can construct the module under test by providing only that module's own options, without constructing any other module's options or the full platform options object.

**Acceptance Scenarios**:

1. **Given** a module that has been migrated to isolated config injection, **When** its unit test is written, **Then** the test only needs to supply that module's options class — no other options are required.
2. **Given** all modules are migrated, **When** a new module is added, **Then** following the established pattern produces a module with an explicit, minimal config dependency from the start.
3. **Given** a developer misconfigures a module's options section path, **When** the host starts, **Then** the options validation throws a clear, actionable error identifying the misconfigured section — not a silent default-values bind.

---

### User Story 4 — Cross-Cutting Job Context Available Without Monolithic Options (Priority: P2)

A module that legitimately needs the current migration `Mode` (Export/Import/Migrate), the package path, or the config version can obtain these values through a focused, read-only service (`IAgentJobContext`) without injecting the full platform options graph. The module remains isolated from all other modules' config slices while still having access to the values it needs for job-level decisions.

**Why this priority**: Without a sanctioned cross-cutting context service, modules that need Mode or PackagePath will reach for `ActiveJobConfigState` as the only available path — reintroducing the coupling the isolated injection pattern was intended to remove.

**Independent Test**: Can be fully tested by constructing the context service with known scalar values and verifying a module under test reads the correct values from it.

**Acceptance Scenarios**:

1. **Given** a module that needs the migration Mode, **When** it reads `IAgentJobContext`, **Then** it receives the resolved Mode value (e.g. "Export") without accessing any other module's config.
2. **Given** `IAgentJobContext` is read-only, **When** a module calls it, **Then** no module can write to it or observe another module's side effects through it.
3. **Given** `IAgentJobContext` carries only scalar values, **When** a module tries to navigate into source/target connector config through it, **Then** no such access is possible — the module must instead use the connector abstraction services.

---

### Edge Cases

- What happens when two options classes declare the same `SectionName`? The DI registration must detect the conflict at startup and fail with a clear error identifying both types.
- What happens when a `SectionName` path points to a JSON array? The schema generator must correctly render the section as a JSON array schema with an appropriate `items` definition.
- What happens when the schema generator is run but the committed schema file does not yet exist? The generator must create it; CI must treat a missing committed schema as a drift violation only if the build already produced one.
- What happens when a polymorphic endpoint type (TFS, ADO, Simulated) is absent from the type registry? The schema generator must produce a schema that only includes the registered types; adding a new connector type requires only registering it in the type registry.
- What happens when the `SectionName` is a colon-delimited path that does not match the JSON structure of a config file? Config validation on startup must detect the mismatch and fail fast with a clear error.

---

## Observability

This feature introduces two observable operations: a **build-time schema generation process** and **startup-time options validation** per registered options class. Neither is a runtime migration module, so the O-4 ProgressEvent / CLI counter pattern does not apply. O-1/O-2/O-3 requirements apply at the appropriate layer for each operation.

### Operations

| Name | Type | Entry Point | Observable Boundary |
| ---- | ---- | ----------- | ------------------- |
| `schema.generate` | build tool | `SchemaGeneratorHost.RunAsync` | Build step — stdout, exit code, schema file written |
| `schema.validate-config` | CLI Tier 0 | `QueueCommand` before deserialisation | Structured log + non-zero exit on violation; `Warning` log if schema absent |
| `config.validate` | startup workflow | `IValidateOptions<T>` per registered type | Host startup — validation errors surfaced before first request |
| `job.context.resolve` | startup workflow | `IAgentJobContext` factory/registration | Per-job DI container build |

### Operator Decisions

| Decision | Operation | Signal |
| -------- | --------- | ------ |
| Is schema generation working? | `schema.generate` | Exit code 0; structured console output with entry count and output path |
| Is schema complete? | `schema.generate` | Entry count in console output equals expected registered options count |
| Did schema drift? | CI drift check | CI step exit code and diff output |
| Did config validation fail? | `config.validate` | Structured log at `Error` on host startup with offending section path and options type name |
| Is a wrong SectionName causing a silent empty bind? | `config.validate` | `ValidateOnStart()` failure logged at `Error` before any migration logic executes |
| Did schema validation catch a bad config? | `schema.validate-config` | Structured log at `Error` with JSON path and constraint; non-zero exit code |
| Was schema file missing at queue time? | `schema.validate-config` | Structured log at `Warning` identifying the expected schema path |

### O-1 Traces

- `schema.generate`: A single root Activity span wrapping the entire generation run, tagged with `schema.entry_count` and `schema.output_path`. Source: `WellKnownActivitySourceNames.Migration` (build-tool process, so span is written to console/stdout only, not to an OTLP exporter).
- `config.validate`: No new span required — startup validation failures propagate as host startup exceptions, which are already captured by the host's Activity lifecycle.

### O-2 Metrics

Schema generation is a build-time tool; runtime OTel metrics are not emitted. The only numeric signal is the structured console output entry count. No `IMigrationMetrics` calls are added for schema generation.

Options validation at startup: no new metric instruments. Validation failures are already surfaced via `ValidateOnStart()` exception propagation.

### O-3 Structured Logging

| Event | Level | Fields |
| ----- | ----- | ------ |
| Schema generation started | `Information` | `entryCount`, `outputPath` |
| Schema generation succeeded | `Information` | `entryCount`, `durationMs`, `outputPath` |
| Schema generation failed | `Error` | `error`, `step` (e.g. "resolve", "write") |
| Duplicate SectionName detected | `Error` | `sectionPath`, `type1`, `type2` |
| Schema validation violation | `Error` | `jsonPath`, `constraint`, `configFile` |
| Schema file absent at queue time | `Warning` | `expectedSchemaPath` |
| Options validation failure at startup | `Error` | `optionsType`, `sectionPath`, `failures[]` |
| IAgentJobContext resolved | `Debug` | `mode`, `configVersion` (PackagePath is `DataClassification.Customer` — omit from log or scope appropriately) |

### O-4 Progress Events

Not applicable — schema generation is a build-time tool and options validation is a host startup concern. Neither exposes `IProgressSink` or contributes to `MigrationCounters` in the CLI progress display.

### Validation Queries

To verify observability is working in a test run:

- Schema generator emits structured log lines at `Information` for start and success with `entryCount > 0`.
- A misconfigured `SectionName` produces an `Error` log at startup before any module executes, identifying the offending type.
- A duplicate `SectionName` registration produces an `Error` log during schema generation (not silently overwritten).

---

## Connector Coverage

This feature is primarily infrastructure and cross-cutting — it introduces no new export, import, discovery, or validation capability that connectors must implement. The traditional connector-coverage check (Simulated / AzureDevOps / TFS parity for module operations) does not apply in full. Two connector-specific DI registration requirements do exist, documented below.

**CONNECTOR COVERAGE CHECK: PASS (N/A for module operations)**

### Features

| Feature | Type | Abstraction | Simulated | AzureDevOps | TFS |
| ------- | ---- | ----------- | --------- | ----------- | --- |
| `options.schema-entry` | infrastructure | `SchemaOptionsEntry` | Required — connector options types must self-register | Required — connector options types must self-register | Required — TFS connector options types must self-register |
| `connector.endpoint-info` | infrastructure | `ISourceEndpointInfo` / `ITargetEndpointInfo` | Required — simulated connector must register endpoint info | Required — ADO connector must register endpoint info | Required — TFS connector must register source endpoint info |

### Acceptance Scenario Mapping

| Feature | Connector | Scenario(s) |
| ------- | --------- | ----------- |
| `options.schema-entry` | Simulated | US-2 SC-3: Simulated endpoint options appear in generated schema with correct discriminator |
| `options.schema-entry` | AzureDevOps | US-2 SC-3: ADO endpoint options appear in generated schema with correct discriminator |
| `options.schema-entry` | TFS | US-2 SC-3: TFS endpoint options appear in generated schema with correct discriminator |
| `connector.endpoint-info` | Simulated | US-4 SC-1: Module reads Mode from IAgentJobContext; Simulated endpoint info resolves without error |
| `connector.endpoint-info` | AzureDevOps | US-4 SC-1: Module reads Mode from IAgentJobContext; ADO endpoint info resolves without error |
| `connector.endpoint-info` | TFS | US-4 SC-1: Module reads Mode from IAgentJobContext; TFS source endpoint info resolves without error |

### TFS Exemptions

No TFS exemptions — TFS connector options types can self-register `SchemaOptionsEntry` entries in the same way as other connectors. TFS-specific endpoint info is source-only (no target); the TFS connector must register `ISourceEndpointInfo` and leave `ITargetEndpointInfo` absent (not registered for TFS source jobs).

### Gaps

None — all connector-specific requirements are captured in Functional Requirements FR-010 and FR-014.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every options class that participates in the configuration file MUST declare a canonical section path constant that uniquely identifies its position in the configuration tree.
- **FR-002**: Every options class registered for configuration binding MUST also register itself with a schema registry at the time of its DI registration — no separate manual step is required.
- **FR-003**: The schema generator MUST derive the full JSON Schema by resolving all schema registry entries from the same DI container that the production host uses — not by walking type graphs or hardcoded lists.
- **FR-004**: The generated schema MUST represent polymorphic endpoint types (source, target) as discriminated unions, with one sub-schema per registered concrete type.
- **FR-005**: The generated schema file MUST be committed to source control and its path registered in the VS Code workspace settings so that editors apply it automatically to `migration.json` files.
- **FR-006**: The schema generator MUST run automatically as part of the build, and CI MUST fail if the committed schema does not match what the current build would produce.
- **FR-006a**: `migration.schema.json` MUST be copied to the root of the CLI output directory as a build artefact, so that it is present alongside the CLI binary in every published or local build.
- **FR-006b**: The `queue` command MUST validate the raw `migration.json` file against `migration.schema.json` as a Tier 0 step — before deserialisation, before any object-level validation, and before any network call. This is a local, offline check. Violations MUST be reported with the full JSON path and constraint description, and the command MUST exit non-zero without submitting a job.
- **FR-006c**: If `migration.schema.json` is absent from the CLI output directory, schema validation MUST be skipped with a structured `Warning` log — the absence of the schema file is not a hard error.
- **FR-007**: Every module and tool MUST inject only its own options slice via `IOptions<T>`. The current pattern of injecting `ActiveJobConfigState` and navigating `.Current.Modules.*`, `.Current.Source`, or `.Current.Target` to reach config values MUST be removed. `ActiveJobConfigState` MUST be fully deleted once all consumers are migrated — it must not be retained as a fallback or convenience accessor.
- **FR-008**: A read-only `IAgentJobContext` service (in `Abstractions.Agent`) MUST be available to any module that requires the migration Mode, package path, or config version — these values MUST NOT require the module to inject the full platform options graph.
- **FR-009**: `IAgentJobContext` MUST be read-only and carry only resolved scalar values — it MUST NOT expose sub-graphs, options objects, or connector configuration.
- **FR-010**: Each connector assembly MUST register an endpoint-info service exposing the resolved source or target URL and project name, so that modules requiring hyperlink generation or log context can use a connector-agnostic abstraction rather than injecting connector-specific options.
- **FR-011**: Incorrect section path constants MUST produce a detectable startup failure (options validation) rather than silently binding to default values.
- **FR-012**: The migration from the current monolithic options binding to isolated per-slice injection MUST be performed incrementally, with the Tools section migrated first as the pilot, followed by Modules, Policies, and Package sections. When an options class is migrated to per-slice injection, its corresponding properties MUST be removed from `MigrationOptions` in the same step to eliminate dual-binding ambiguity.
- **FR-013**: The TFS agent (net481) MUST continue to function throughout and after the migration — the schema generator is net10.0 only and the TFS agent has no schema generation dependency.
- **FR-014**: The schema generator host MUST explicitly reference every connector assembly present in the solution. CI MUST verify that the set of connector assemblies referenced by the schema generator matches the set of connector assemblies in the solution — an unreferenced connector assembly is a build error.
- **FR-015**: The schema generation step MUST fail if two options classes are registered with the same section path, regardless of whether the conflict is detected at startup or at build time. The CI schema generation job is the primary enforcement point.

### Key Entities

- **SchemaOptionsEntry**: A registration record linking an options type to its canonical section path. Registered as a singleton by each `Add*Services()` call that also registers `IOptions<T>`. Resolved in bulk by the schema generator.
- **IAgentJobContext**: A read-only service (in `Abstractions.Agent`) scoped to a single agent job execution. Exposes: `Mode` (the operation being run), `PackagePath` (the resolved output directory), `ConfigVersion` (for upgrader checks). No sub-graphs.
- **ISourceEndpointInfo / ITargetEndpointInfo**: Connector-registered services exposing the resolved URL and project for the source and target endpoints respectively. Registered by each connector assembly's own DI extension.
- **Schema Registry**: The collection of all `SchemaOptionsEntry` singletons in the DI container. Queried exclusively by the schema generator host; not used at runtime.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Every key present in a valid `migration.json` file is present in the generated schema — zero keys are silently unrecognised by the schema.
- **SC-002**: Every key present in the generated schema corresponds to a real configuration binding in the running system — zero schema-only keys exist.
- **SC-003**: Adding a new options class and registering it in DI produces an updated schema in the next build without any additional manual step.
- **SC-004**: A build in which the committed schema differs from the generated schema fails CI with a meaningful diff, preventing undetected schema drift. The comparison MUST be semantic (normalised JSON, not text diff) to avoid false positives from whitespace or key-ordering differences.
- **SC-010**: Running `devopsmigration queue` with a `migration.json` containing an unknown key exits with a non-zero code and identifies the offending path before any control plane contact is made.
- **SC-011**: A published CLI build contains `migration.schema.json` in the same directory as the CLI binary.
- **SC-005**: A module unit test requires only that module's own options class to construct the module — no other options classes are needed.
- **SC-006**: A developer with VS Code open on a `migration.json` file receives IntelliSense completions for all configurable keys without installing any additional extension.
- **SC-007**: A wrong section path constant is detected and reported at application startup — not at the point of first use during a migration run.
- **SC-008**: All existing tests pass after the migration without modification of test assertions — the observable behaviour of the running system is identical before and after this feature.
- **SC-009**: `ActiveJobConfigState` is deleted from the codebase — zero references remain in any module, tool, or test after the migration is complete.

---

## Assumptions

- The `Tools` section options (FieldTransform, NodeTranslation, IdentityLookup) already use the correct self-registration pattern and serve as the pilot for the migration.
- `MigrationOptions` is a **deserialisation-only** type. Config files are operator-authored JSON; the platform never writes config files. `SaveConfigurationAsync`, `ConfigNewCommand`, and `ConfigureCommand` are out of scope — config authoring is entirely the operator's responsibility and the platform has no legitimate need to serialise a `MigrationOptions` object to JSON. `MigrationOptions` survives only as a transient deserialisation target in `JobAgentWorker` during the bootstrap window before the per-job `IConfiguration` is built; once the agent can build `IConfiguration` directly from the raw JSON payload without that intermediate step, `MigrationOptions` can be deleted entirely.
- The schema generator will be implemented as a separate host project (not a hidden CLI command) to keep schema generation concerns cleanly separated from the CLI binary.
- Schema generation is net10.0 only; the TFS agent (net481) is entirely unaffected.
- The `EndpointOptionsTypeRegistry` already provides the list of registered concrete endpoint types and will be reused by the schema generator for discriminated union generation.
- Container classes (`MigrationToolsOptions`, `MigrationModulesOptions`) are retained as pure schema-structure markers with no constructor injection — only leaf options classes are injected via `IOptions<T>`.
- VS Code is the primary IDE target for the `json.schemas` registration; other editors are out of scope.
- The schema validation in the `queue` command uses `NJsonSchema` or equivalent for JSON Schema v7 validation against the raw file bytes — it does not re-implement schema parsing.
- All other commands that load a config file (`prepare`, `validate`, etc.) are out of scope for schema validation in this feature — only `queue` is required. Other commands can be added in a follow-up.
- The per-job DI container in the Migration Agent (introduced by feature 025) already provides the correct `IConfiguration` source for all `IOptions<T>` bindings at runtime; this feature targets CLI-side DI cleanup and schema generation only.
- `ActiveJobConfigState.PackageConfig` (`IConfiguration`) is currently used for polymorphic endpoint-type binding. Once connectors register `ISourceEndpointInfo`/`ITargetEndpointInfo` from their own DI extensions (which already have access to `IConfiguration` at registration time), this bridge is no longer needed and `ActiveJobConfigState` is deleted in full.
- `ConfigPayload` in the job is already set by reading the raw JSON file bytes directly (`File.ReadAllTextAsync`) — it is never produced by serialising a `MigrationOptions` object. This confirms `MigrationOptions` has no legitimate serialisation role today.
