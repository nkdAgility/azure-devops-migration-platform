# Feature Specification: Fix — Tool Config Never Reaches the Agent (Config Travels in Package)

**Feature Branch**: `025-agent-config-package`  
**Created**: 2026-04-29  
**Status**: Draft  
**Input**: User description: "Fix: Tool Config Never Reaches the Agent (Config Travels in Package)"

## Clarifications

### Session 2026-04-29

- Q: CLI writing `migration-config.json` to the package conflicts with Rule 23 (CLI has no package write access). Amend Rule 23 or change the design? → A: Amend Rule 23 to allow CLI to write `migration-config.json` only (pre-submission step). Agent cannot write it before it receives the job.
- Q: US-3/FR-005 described graceful fallback on missing file. With all credentials in `migration-config.json`, fallback is impossible. Should the agent fail fast or use dual-mode? → A: Fail fast with a clear error instructing the operator to re-submit. US-3 updated; FR-005 updated.
- Q: What fields must remain in `MigrationJob` after all config moves to the package? → A: `jobId`, `packageUri` (or `workingFolder` for local filesystem), `mode`, and `configVersion`.

## Confirmed Design Flow

```
1. CLI reads migration.json
        ↓
2. CLI writes migration-config.json → Storage location (package root)
        ↓
3. CLI submits MigrationJob to ControlPlane
   (contains: jobId, packageUri/workingFolder, mode, configVersion — NO config)
        ↓
4. ControlPlane stores job, assigns to Agent
        ↓
5. Agent receives MigrationJob
        ↓
6. Agent opens package store using packageUri/workingFolder
        ↓
7. Agent reads migration-config.json from package
        ↓
8. Agent builds per-job IOptions<T> scope from package config
        ↓
9. Agent executes modules with correct config
```

The storage location is the single source of truth. The ControlPlane and `MigrationJob` are purely a dispatch mechanism — they carry no configuration beyond "where is the package and what mode to run."

## Architecture References

| Document | Status |
|---|---|
| `docs/architecture.md` | Confirmed accurate; fix aligns with Source → Package → Target principle |
| `docs/agent-hosting.md` | **Discrepancy** — no mention of reading `migration-config.json` from package; needs update |
| `.agents/30-context/domains/job-lifecycle.md` | **Discrepancy** — omission of tool config from `MigrationJob` is intentional but undocumented; needs note |
| `.agents/30-context/domains/migration-package-concept.md` | **Discrepancy** — `migration-config.json` well-known path not yet defined; needs update |
| `.agents/20-guardrails/core/architecture-boundaries.md` | **Guardrail Challenge** — Rule 23 (CLI has no package write access) conflicts with this design. Operator confirmed: **Option A — amend Rule 23** to permit CLI write of `migration-config.json` only. Rationale: config must be in the package before the job is dispatched; agent cannot write it before it has received the job. Rule 23 amendment required as part of this feature. |

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Tool Configuration Applied During Export (Priority: P1)

An operator configures field transform rules and node mappings in `migration.json` and runs an export. The rules should be applied to every work item revision as it is exported — renaming fields, remapping area paths, etc. Today, those rules are silently ignored because the agent never receives them.

**Why this priority**: This is the primary correctness bug. Without the fix, every migration that relies on field transforms or node mappings produces wrong output with no warning. This is the P1 because all other scenarios depend on config reaching the agent first.

**Independent Test**: Can be verified by configuring at least one `FieldTransform` rule and one `NodeTranslation` mapping, running a simulated export, and asserting the exported `revision.json` files reflect the applied transforms.

**Acceptance Scenarios**:

1. **Given** an operator has configured `FieldTransform` rules in `migration.json`, **When** a migration job is submitted and the agent runs the export, **Then** the exported work item revisions reflect the configured transforms (field values are renamed/mapped as specified).
2. **Given** an operator has configured `NodeTranslation` mappings in `migration.json`, **When** the agent runs the export, **Then** area and iteration path fields in exported revisions reflect the configured node translations.
3. **Given** a migration job is submitted, **When** the CLI writes config to the package before submission, **Then** a `migration-config.json` file exists at the package root after job dispatch.
4. **Given** the agent resumes a partially completed export job, **When** the agent re-reads config from the package, **Then** the same tool configuration is applied to remaining revisions — no divergence between first run and resume.

---

### User Story 2 — Config Audit Trail in the Package (Priority: P2)

After a migration completes, an operator or support engineer reviews the package to understand exactly which rules were active during that run — without needing to locate the original `migration.json`.

**Why this priority**: Correctness audit and support diagnostics. Knowing what config actually ran (vs. what the operator later claims to have used) is critical for diagnosing incorrect migrations.

**Independent Test**: Can be verified independently by inspecting the package root after a completed export and confirming `migration-config.json` is present and contains the operator-supplied rules.

**Acceptance Scenarios**:

1. **Given** an export has completed, **When** the operator opens the package folder, **Then** `migration-config.json` is present at the package root and contains the full tool configuration that was active during the run.
2. **Given** `migration-config.json` is present in an existing package, **When** the job is resumed on a new agent instance, **Then** the new agent reads config from the same file — not from any external source.

---

### User Story 3 — Legacy Package Fails Fast with Clear Error (Priority: P3)

An operator attempts to run a migration job against a package that was created before this fix (no `migration-config.json` present). Without `migration-config.json` the agent has no credentials and cannot connect to source or target — a silent fallback is not possible and would produce misleading failures. The agent must fail fast with a clear, actionable error message.

**Why this priority**: With all config in `migration-config.json`, an absent file means zero credentials. Proceeding silently produces confusing API errors downstream. An explicit error lets the operator re-submit the job, which writes the file.

**Independent Test**: Can be verified by running the agent against a package root that contains no `migration-config.json` and asserting the job fails immediately with an error message that explains the file is missing and instructs re-submission.

**Acceptance Scenarios**:

1. **Given** a package contains no `migration-config.json`, **When** the agent attempts to read config, **Then** the job fails immediately with a structured error: "migration-config.json not found. This package pre-dates config-in-package. Re-submit the job to create it."
2. **Given** the job fails due to a missing `migration-config.json`, **When** the operator re-submits the job via the CLI, **Then** `migration-config.json` is written and the job proceeds normally.

---

### Edge Cases

- What happens when `migration-config.json` is present but corrupt (invalid JSON)? → Agent must fail fast with a clear error rather than proceeding with silently incorrect config.
- What happens when the CLI fails to write `migration-config.json` before submitting the job? → Job submission must be aborted; no partial state.
- What happens when two concurrent jobs share the same package URI? → Each job writes its own config; concurrent writes are protected by the `IArtefactStore` atomic-write guarantee.
- What happens when `IdentityLookupOptions` contain sensitive data (e.g., email addresses)? → The file is persisted in the package; customer data fields must be scoped with `DataClassification.Customer` in any log output referencing them.
- What happens when the agent runs on a blob store with eventual consistency and reads `migration-config.json` before it is visible? → Agent must retry the read with back-off before failing fast. If the file is still unavailable after exhausting retries, the job must fail with a clear error. A silent fallback to defaults is forbidden (see FR-005).
- What happens when tool-consuming modules are inadvertently registered as Singleton in the root container? → They will silently see empty defaults from the host registration, not the per-job values. The implementation MUST audit that all tool-consuming modules are registered as Scoped.
- What happens when `migration-config.json` already exists in the package and the CLI attempts to re-write it for a resumed job? → CLI must warn and require explicit confirmation before overwriting; it must not silently replace the config that was used for previously exported revisions.
- What happens when `IPackageConfigStore.WriteAsync` is called before the package root directory exists? → WriteAsync must create the path if absent, consistent with `IArtefactStore` behaviour for new packages.

## Connector Coverage

**Result: PASS (N/A) — No connector-specific logic introduced.**

This feature implements `IPackageConfigStore`, a cross-cutting infrastructure service that operates at the package layer (`IArtefactStore`) rather than at the source/target connector layer. It does not introduce any feature that requires a Simulated, AzureDevOpsServices, or TeamFoundationServer connector implementation.

### Applicability Assessment

| Feature | Simulated | AzureDevOps | TFS | Rationale |
|---|---|---|---|---|
| `config.write` (CLI → package) | N/A | N/A | N/A | CLI operation; no connector involved |
| `config.read` (Agent ← package) | N/A | N/A | N/A | Package-layer operation via `IArtefactStore`; same implementation across all agent types |

### Agent Coverage Note

Although this feature has no connector-specific logic, it has **agent-type** coverage requirements:

| Agent | Requirement |
|---|---|
| `MigrationAgent` (.NET 10) | MUST call `IPackageConfigStore.ReadAsync` at job start (FR-002, FR-003) |
| `TfsMigrationAgent` (.NET 4.8) | MUST call `IPackageConfigStore.ReadAsync` at job start (FR-013) |

The `IPackageConfigStore` implementation MUST be net481-compatible (no .NET 10-only APIs). The interface lives in `Abstractions` (no framework branching required).

## Observability

### Operations

| Name | Type | Entry Point | Dependencies |
|---|---|---|---|
| `config.write` | `service` | `IPackageConfigStore.WriteAsync` | `IArtefactStore.WriteTextAsync` (package root) |
| `config.read` | `service` | `IPackageConfigStore.ReadAsync` | `IArtefactStore.ReadTextAsync` (package root) |

### Operator Decisions per Operation

| Operation | Decision | Question answered |
|---|---|---|
| `config.write` | Is it working? | Did the CLI successfully persist tool config to the package before job submission? |
| `config.write` | What failed? | Did write fail due to missing package path, serialisation error, or store error? |
| `config.read` | Is it working? | Did the agent successfully load tool config at job start? |
| `config.read` | What failed? | Did read fail due to missing file (legacy fallback), parse error, or store error? |
| `config.read` | Is it correct? | Was a fallback to defaults triggered, and on which jobs? |

### Metrics

| Metric Name | Instrument | Unit | Operation | Decision served |
|---|---|---|---|---|
| `migration.config.write.count` | `Counter<long>` | `{write}` | `config.write` | Is it working? |
| `migration.config.write.errors` | `Counter<long>` | `{error}` | `config.write` | What failed? |
| `migration.config.read.count` | `Counter<long>` | `{read}` | `config.read` | Is it working? |
| `migration.config.read.errors` | `Counter<long>` | `{error}` | `config.read` | What failed? |
| `migration.config.read.fallbacks` | `Counter<long>` | `{fallback}` | `config.read` | Is it correct? |

Meter: `WellKnownMeterNames.Migration` (`DevOpsMigrationPlatform.Migration`).  
All new metric name constants MUST be added to `WellKnownMetricNames`.

### Traces

| Component | Span Name | Tags | Parent | Decision served |
|---|---|---|---|---|
| `PackageConfigStore` | `config.write` | `job.id`, `package.uri`, `operation=write` | Root (`WellKnownActivitySourceNames.Migration`) | Is it working? / What failed? |
| `IArtefactStore` write | `artefactstore.write` | `path=migration-config.json` | `config.write` | What failed? / Where is it slow? |
| `PackageConfigStore` | `config.read` | `job.id`, `package.uri`, `operation=read`, `fallback=true|false` | Root (`WellKnownActivitySourceNames.Migration`) | Is it working? / Is it correct? |
| `IArtefactStore` read | `artefactstore.read` | `path=migration-config.json` | `config.read` | What failed? / Where is it slow? |

Context propagation: automatic via `Activity` hierarchy (W3C TraceContext). `job.id` injected into root span tag from the calling job context.

### Logging

| Event | Level | Fields | Operation | Decision served |
|---|---|---|---|---|
| Config write started | `Information` | `operationId`, `package.uri` | `config.write` | Is it working? |
| Config write completed | `Information` | `operationId`, `package.uri`, `durationMs` | `config.write` | Is it working? |
| Config write failed | `Error` | `operationId`, `package.uri`, `errorType`, `errorMessage`, `durationMs` | `config.write` | What failed? |
| Config read started | `Information` | `operationId`, `package.uri` | `config.read` | Is it working? |
| Config read completed | `Information` | `operationId`, `package.uri`, `durationMs` | `config.read` | Is it working? |
| Config read fallback to defaults | `Warning` | `operationId`, `package.uri`, `reason` | `config.read` | Is it correct? |
| Config read failed (parse error) | `Error` | `operationId`, `package.uri`, `errorType`, `errorMessage` | `config.read` | What failed? |

Field values that are project names, org URLs, or user-entered strings MUST use `DataClassification.Customer` scope. Package URIs are operator-controlled configuration values — treat as non-customer data unless they contain project/org identifiers.

### Correlation

| Field | Source | Scope |
|---|---|---|
| `operationId` / `traceId` | `Activity.Current.TraceId` | All telemetry |
| `job.id` | Job context passed into `OnMigrationJobAsync` | All telemetry within a job |
| `package.uri` | Job artefacts URI | Config operation spans and logs |

### Validation Queries

```kql
// Failure identification: config write or read failures by job
customMetrics
| where name in ("migration.config.write.errors", "migration.config.read.errors")
| summarize errors=sum(value) by bin(timestamp, 5m), name
| order by timestamp desc

// Load observation: how many jobs fell back to default config (legacy packages)
customMetrics
| where name == "migration.config.read.fallbacks"
| summarize fallbacks=sum(value) by bin(timestamp, 1h)

// End-to-end trace: config read for a specific job
dependencies
| where name == "config.read"
| where customDimensions["job.id"] == "<job-id>"
| project timestamp, duration, customDimensions

// Error diagnosis: parse errors on config read
traces
| where severityLevel >= 3  // Error
| where message contains "config.read"
| project timestamp, message, customDimensions
```

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The CLI MUST write the fully resolved migration configuration — including source/target options, credentials, module settings, policies, and all tool options — to a well-known file (`migration-config.json`) at the package root before submitting the job to the control plane.
- **FR-002**: The agent MUST read `migration-config.json` from the package root after opening the package store, before executing any module.
- **FR-003**: The agent MUST construct a per-job configuration scope from the content of `migration-config.json`, making the correct `IOptions<T>` values available to all modules and tools during that job's execution. This includes source/target connection options and credentials.
- **FR-004**: The `MigrationJob` wire contract MUST be reduced to a minimal pointer — package URI and execution mode — with no typed config options. All configuration (credentials, endpoints, tool options, policies) travels exclusively via `migration-config.json` in the package.
- **FR-005**: If `migration-config.json` is absent from the package, the agent MUST fail the job immediately with a structured error message instructing the operator to re-submit. Silent fallback to defaults is forbidden because all credentials are in this file and no useful work can be performed without it.
- **FR-006**: If `migration-config.json` is present but cannot be parsed, the agent MUST fail the job with a clear error message rather than proceeding with incorrect defaults.
- **FR-007**: The CLI write and job submission MUST be atomic from the operator's perspective — if writing `migration-config.json` fails, the job MUST NOT be submitted.
- **FR-008**: A new abstraction (`IPackageConfigStore`) MUST be defined in the `Abstractions` layer, callable from CLI (write) and Agent (read). The control plane MUST NOT participate in config transport.
- **FR-009**: The per-job configuration scope MUST use the same two-phase DI pattern already used by `ExportContext`/`ImportContext` — root container holds infrastructure singletons; per-job child scope holds the `IOptions<T>` values loaded from the package.
- **FR-010**: The well-known package path for config (`migration-config.json`) MUST be defined as a constant in `PackagePaths` in `DevOpsMigrationPlatform.Abstractions.Agent` (specifically `Abstractions.Agent/Lease/PackagePaths.cs`) and referenced from all callers.
- **FR-011**: Because `migration-config.json` contains credentials, the package MUST be protected from unauthorised access. When the package resides on a blob store, the `IArtefactStore` MUST be configured with access controls that restrict read access to the submitting operator and the assigned agent. The CLI MUST NOT write `migration-config.json` to a package URI that is publicly readable. Credentials in the file MUST NOT appear in log output; any logging of config content MUST redact credential fields.
- **FR-012**: Once `migration-config.json` has been written to a package, the CLI MUST NOT silently overwrite it if one already exists. If a file already exists, the CLI MUST emit a warning and require explicit operator action (e.g., a `--force` flag or user confirmation) before overwriting. This protects resume consistency across job runs.
- **FR-013**: The agent-side config reading (FR-002, FR-003) applies equally to `TfsMigrationAgent` (the net481 TFS export agent). Both agents MUST read `migration-config.json` from the package root and construct a per-job configuration scope before executing any module.
- **FR-014**: `IPackageConfigStore.WriteAsync` and `ReadAsync` MUST emit O-1 activity spans and O-3 structured log entries (Information on success, Warning on fallback to defaults, Error on parse failure).

### Key Entities

- **`IPackageConfigStore`**: Abstraction in `Abstractions` for reading and writing `migration-config.json`. Write called by CLI; Read called by Agent (both MigrationAgent and TfsMigrationAgent). Never called by the control plane.
- **`migration-config.json`**: Well-known file at the package root. Contains the full serialised `MigrationOptions` — source/target endpoints and credentials, module enable/disable flags, policies, and all tool options (`MigrationPlatform.Tools.*`). Written once by the CLI before job submission; read by every agent job at start. Provides an audit trail (excluding credential values in logs) and enables resume determinism. Access MUST be restricted to the operator and agent.
- **Per-job IServiceScope**: A child DI scope created per job inside `OnMigrationJobAsync` (and the TFS equivalent). Contains `IOptions<T>` overrides loaded from `migration-config.json`. Disposed when the job ends. All tool-consuming modules MUST be registered as `Scoped` (not `Singleton`) to receive per-job values.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100% of configured field transform rules and node translation mappings are applied to exported revisions — zero silently ignored rules in any tested scenario.
- **SC-002**: Every successfully submitted job package contains a `migration-config.json` file at the package root that exactly matches the tool config the operator specified.
- **SC-003**: Agent resume of a previously started job applies the same tool config as the initial run — no divergence between runs on the same package.
- **SC-004**: Agent processing of a legacy package (no `migration-config.json`) fails immediately with a structured error message — it does not attempt to connect to source or target. The error message includes the instruction to re-submit the job.
- **SC-005**: The job submission payload (`MigrationJob`) retains only the minimal dispatch fields (`jobId`, `packageUri`/`workingFolder`, `mode`, `configVersion`, `guardrails`, `diagnostics`, `resume`) — all credential, endpoint, tool, and policy config is removed from the wire contract. Its size is constant regardless of the number or complexity of configured tool rules, endpoints, or policies.

## Assumptions

- The full `MigrationOptions` (source, target, credentials, modules, policies, tools) is fully resolved and validated by the CLI before being written to `migration-config.json`. No deferred resolution is required on the agent side.
- `MigrationJob` becomes a minimal pointer with six fields: `jobId`, `packageUri` (or `workingFolder` for local filesystem jobs), `mode`, `configVersion`, `guardrails`, and `diagnostics` (and `resume` for checkpoint state). Credentials and all other config travel exclusively via `migration-config.json`. `source`, `target`, `modules`, `policies`, and `configHash` are removed. This is a **breaking change** to the `MigrationJob` schema and requires a version increment and upgrader per guardrail Rule 9.
- The `IArtefactStore` write operations are sufficiently atomic for the single-writer CLI case (no concurrent CLI writes to the same package root are expected at job-submission time).
- The per-job child `IServiceScope` pattern does not require changes to any existing module or tool beyond wiring — modules already consume `IOptions<T>` via constructor injection.
- All `MigrationOptions` option types travel in the file. New option types added in future are automatically included without requiring a spec amendment.
- Docs read: `docs/architecture.md`, `docs/agent-hosting.md`, `.agents/30-context/domains/job-lifecycle.md`, `.agents/30-context/domains/migration-package-concept.md`, `.agents/20-guardrails/core/architecture-boundaries.md`.

