# Feature Specification: Close DSL Migration Gaps

**Feature Branch**: `038-close-dsl-gaps`  
**Created**: 2026-06-03  
**Status**: Draft  
**Input**: User description: "close the analysis\dsl-gaps-detected.md"

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Resolve Identity Matching Gaps (Priority: P1)

As a platform contributor closing out the DSL migration, I need the `IdentitiesModule`
Prepare phase to implement UPN/email and display-name matching against the target tenant
so that the blocked `IdentitiesOrchestrator` scenario (GAP-001) can be removed from the
gaps log and the corresponding feature-file scenario can be tested and retired.

**Why this priority**: GAP-001 represents a broken behaviour contract — the docstring
documents a four-step resolution order of which steps 2 and 3 are unimplemented. This
misleads operators and blocks the identity migration feature acceptance test. Correctness
of identity resolution is foundational to every import module that translates identities.

**Independent Test**: Can be fully tested by running `IdentitiesModule.PrepareAsync`
with a source descriptor whose UPN matches a target identity, then calling
`IIdentityTranslationTool.Translate()` and verifying the resolved identity matches the
target — without any explicit entry in `mapping.json`.

**Acceptance Scenarios**:

1. **Given** a source identity `bob@source.com` with no explicit override in `mapping.json`,
   **When** `PrepareAsync` runs and the `IIdentityAdapter` finds a UPN match in the target,
   **Then** `IIdentityTranslationTool.Translate()` returns `bob@target.com` for that source.

2. **Given** a source identity whose UPN has no match in the target but whose display name
   `Bob Smith` matches exactly one target identity,
   **When** `PrepareAsync` runs,
   **Then** `IIdentityTranslationTool.Translate()` returns the display-name-matched target identity.

3. **Given** a source identity whose display name matches two or more target identities,
   **When** `PrepareAsync` runs,
   **Then** the match is treated as ambiguous, a warning is logged including the display
   name and the count of conflicting matches, and the configured default identity is used.

4. **Given** a source identity with neither UPN nor display-name match in the target
   and no explicit override,
   **When** `PrepareAsync` runs,
   **Then** `IIdentityTranslationTool.Translate()` returns the configured default identity.

5. **Given** an explicit override exists in `mapping.json` for the source identity,
   **When** `IIdentityTranslationTool.Translate()` is called,
   **Then** the override is returned without querying the target (step 1 takes priority).

6. **Given** the `IIdentityAdapter` query fails (network error, timeout, or non-success response)
   during `PrepareAsync`,
   **When** `PrepareAsync` processes that source identity,
   **Then** the failure is logged as a structured warning, the identity is recorded as
   unresolved in `prepare-report.json`, and Prepare continues without throwing.

7. **Given** `IIdentityTranslationTool.IsEnabled` is `false`,
   **When** `IIdentityTranslationTool.Translate()` is called for any source identity,
   **Then** the source identity is returned unchanged without consulting the orchestrator.

8. **Given** `AzureDevOpsServices` connector — same scenarios 1–7 above apply, with the
   target-tenant query implemented by `AzureDevOpsIdentityAdapter` via the ADO Graph API.

9. **Given** `TeamFoundationServer` connector — same scenarios 1–7 apply, with the
   target-tenant query implemented by `TfsIdentityAdapter` via the TFS Identity Service.
   If the TFS Identity Service does not expose UPN or display-name search, the adapter
   MUST log a structured warning and return no candidates, causing fallback to step 4.

10. **Given** `Simulated` connector — same scenarios 1–7 apply, with the target-tenant
    query implemented by `SimulatedIdentityAdapter` against the in-memory simulated store.

---

### User Story 2 - Fix NodesModule Configuration Conflict — GAP-002 and GAP-003 (Priority: P2)

As a platform contributor, I need the `NodesModule` configuration and feature scenarios
to correctly reflect which options class owns which property, and the module's skip-guard
behaviour to be consistent with the documented contract, so that **GAP-002 and GAP-003**
are closed and the classification-tree import tests accurately verify observable behaviour.

**Why this priority**: GAP-002 and GAP-003 are tightly coupled — both stem from
`AutoCreateNodes` being attributed to the wrong options class, causing two feature
scenarios to assert unreachable or non-existent behaviour (`INodeEnsurer` does not exist).

**Independent Test**: Can be fully tested by configuring `NodesModule` with
`ReplicateSourceTree = false` and verifying `ImportAsync` returns `Skipped` without
calling the orchestrator.

**Acceptance Scenarios**:

1. **Given** `NodesModule` is configured with `ReplicateSourceTree = false`,
   **When** `ImportAsync` is invoked,
   **Then** the orchestrator is not called and the result is `Skipped`.

2. **Given** `NodesModule` is configured with `Enabled = false`,
   **When** `ImportAsync` is invoked,
   **Then** the orchestrator is not called (regardless of `ReplicateSourceTree`).

3. **Given** `ReplicateSourceTree = true`,
   **When** `NodesModule.ImportAsync` runs,
   **Then** `INodesOrchestrator.ImportAsync` is called as before.

4. **Given** `NodeTranslationOptions.AutoCreateNodes = true` and the `NodeTranslationTool`
   is enabled,
   **When** the `NodeTranslationTool` pre-creates nodes for a given source path,
   **Then** `INodesOrchestrator.EnsurePathAsync` is called for that path.

5. **Given** the codebase prior to this change references `INodeEnsurer`,
   **When** the build is run after this change,
   **Then** no symbol named `INodeEnsurer` is resolvable in the codebase — all
   references have been replaced by `INodesOrchestrator`.

6. **Given** the feature-file scenario that incorrectly attributes `AutoCreateNodes`
   to `NodesModuleOptions`,
   **When** this spec is implemented,
   **Then** that scenario is deleted from the feature file and the deletion is recorded
   as `Status: RESOLVED` in `analysis/dsl-gaps-detected.md`.

---

### User Story 3 - Fix TeamImportOrchestrator Path-Translation Fallback (Priority: P2)

As a platform contributor, I need `TeamImportOrchestrator.TranslatePath()` to return
`null` when the translation tool cannot map a path (rather than silently passing through
the source path), so that GAP-005 is resolved and the skip-on-untranslatable scenarios
for area paths and iteration paths reflect actual, testable behaviour.

**Why this priority**: Silent pass-through of untranslatable paths can corrupt imported
teams with source-side paths that have no meaning in the target project. The current
behaviour is a data-integrity risk that spans two feature files.

**Independent Test**: Can be fully tested by configuring the `NodeTranslationTool` to
return `null` for a given path and asserting that the path is excluded from the
`SetAreaPathsAsync` call.

**Acceptance Scenarios**:

1. **Given** `NodeTranslationTool` returns `null` for an included area path,
   **When** `TeamImportOrchestrator` processes team area paths,
   **Then** the untranslatable path is excluded and a structured warning is logged
   including the skipped path value.

2. **Given** `NodeTranslationTool` returns `null` for the default area path,
   **When** `TeamImportOrchestrator` processes team area paths,
   **Then** `SetAreaPathsAsync` is not called at all.

3. **Given** `NodeTranslationTool` returns `null` for an iteration path,
   **When** `TeamImportOrchestrator` processes team iterations,
   **Then** the untranslatable iteration is skipped and a structured warning is logged
   including the skipped path value.

4. **Given** all other callers of `TranslatePath()` in the codebase,
   **When** `TranslatePath()` returns `null`,
   **Then** each caller handles the null result explicitly — either by skipping the
   path or by substituting a specified fallback — with no `NullReferenceException`.

---

### User Story 4 - Fix TeamImportOrchestrator Member Identity Skip (Priority: P3)

As a platform contributor, I need `TeamImportOrchestrator` to skip adding a member
when the identity resolves to the configured default (indicating an unresolvable
identity), logging a warning instead of silently importing the member under the wrong
identity, so that GAP-006 is closed.

**Why this priority**: Importing members under the default identity silently pollutes
team membership data. Detecting and warning on unresolvable members enables operators
to correct mappings and re-import.

**Independent Test**: Can be fully tested by configuring `IdentityMappingService` to
return the default identity for a given descriptor and verifying `AddMemberAsync` is
not called.

**Acceptance Scenarios**:

1. **Given** `IIdentityTranslationTool.Translate()` returns the configured default identity
   for a member descriptor,
   **When** `TeamImportOrchestrator` processes team members,
   **Then** `AddMemberAsync` is not called for that member and a structured warning is
   logged including the original member descriptor.

2. **Given** `IIdentityTranslationTool.Translate()` returns a non-default resolved identity,
   **When** `TeamImportOrchestrator` processes team members,
   **Then** `AddMemberAsync` is called with the resolved descriptor.

---

### User Story 5 - Close Default Team Assignment Gap (Priority: P3)

As a platform contributor, I need to formally document the Azure DevOps API limitation
that prevents explicit default-team assignment, delete the aspirational scenario from
the feature file, and ensure the import orchestrator emits a structured warning so that
GAP-004 is resolved.

**Why this priority**: The gap is a known permanent limitation of the Azure DevOps API.
Leaving it open blocks gap-log closure without adding value. The implementation
already has the correct behaviour (log and continue) — this story validates and
documents it.

**Independent Test**: Can be fully tested by running `TeamImportOrchestrator` with
a package containing a default-team and asserting the specific structured warning is
emitted.

**Acceptance Scenarios**:

1. **Given** the import package contains a team with `IsDefault = true`,
   **When** `TeamImportOrchestrator` processes that team,
   **Then** a structured warning is logged containing the team name and the message
   `"target API does not support explicit default team assignment"`, and import
   continues without error.

2. **Given** operator guidance documentation for the teams import module,
   **When** an operator reviews the teams import documentation,
   **Then** the documentation states that default team assignment is not performed
   automatically and instructs the operator to set the default team manually via
   Project Settings → Teams in the target Azure DevOps project.

---

### User Story 6 - Close GAP-007 by Deleting Architecturally Impossible Scenario (Priority: P3)

As a platform contributor, I need the aspirational `@us1-write-idempotency` scenario
deleted from the config-applied-on-export feature file and GAP-007 marked resolved,
because the CLI has no access to the package filesystem — ever — and the pre-submission
check it describes is architecturally impossible.

**Why this priority**: The CLI communicates with the control plane only. It has no
visibility into the package path. The scenario assumed a local filesystem check that
cannot exist in this architecture. The agent already handles the existing-file case
via resume semantics (overwrite if compatible, reject if endpoints changed) — that
behaviour is correct and needs documenting, not replacing.

**Independent Test**: Can be fully verified by confirming the scenario is absent from
the feature file and the gap entry is marked `Status: RESOLVED` with the architectural
rationale recorded.

**Acceptance Scenarios**:

1. **Given** the `@us1-write-idempotency` scenario in
   `features/export/config-in-package/config-applied-on-export.feature`,
   **When** this gap is closed,
   **Then** the scenario is deleted and its deletion is recorded in
   `analysis/dsl-gaps-detected.md` as `Status: RESOLVED` with the rationale:
   "CLI has no access to the package filesystem; pre-submission check is
   architecturally impossible. Agent resume semantics handle the existing-file case."

2. **Given** operator documentation for the config-applied-on-export behaviour,
   **When** an operator submits a job where `migration-config.json` already exists,
   **Then** the documentation states that the agent applies resume semantics:
   overwrites if endpoints are unchanged, rejects with `InvalidOperationException`
   if endpoints changed.

---

### User Story 7 - Resolve OTel Counter Test Infrastructure (Priority: P4)

As a platform contributor, I need the OTel metric counter and histogram scenarios
(GAP-008 and GAP-009) to be wired up with an in-memory exporter scoped per test,
so that the export-execution-metrics and export-payload-metrics gap entries can be
closed with verifiable, deterministic unit tests.

**Why this priority**: These gaps require test-infrastructure work (OTel in-memory
exporter) rather than production code changes. Resolving them ensures the gap log
reaches zero open items.

**Independent Test**: Can be tested by running the export orchestrator with the OTel
in-memory exporter wired and asserting counter values after a sample export run.

**Acceptance Scenarios**:

1. **Given** the export orchestrator runs and processes a known number of work items,
   **When** the run completes,
   **Then** the `migration.workitems.attempted` counter (instrument type: counter)
   reflects exactly the number of items processed.

2. **Given** transient failures occur during export and retries succeed,
   **When** the run completes,
   **Then** the `migration.workitems.retried` counter (instrument type: counter)
   increments once per retry attempt.

3. **Given** the export orchestrator processes a batch of known work items,
   **When** the run completes,
   **Then** `MetricSnapshot` histogram properties (`RevisionCountMean`, `FieldCountMean`,
   `PayloadBytesMean`) reflect aggregated histogram values from that run (instrument
   type: histogram).

4. **Given** any test that asserts OTel metric values,
   **When** the test runs,
   **Then** it uses a `MeterProvider` scoped to that test's lifetime so counter values
   from other tests do not accumulate into the assertion.

---

### Edge Cases

- **Null/invalid configured default identity**: `PrepareAsync` is the validation gate. It
  checks every source identity for existence on the target; any identity that does not match
  (UPN, then display name) is recorded as **unresolved** in `prepare-report.json` with a
  structured warning. Resolution therefore does not depend on a valid configured default — the
  unresolved set is surfaced at Prepare time for the operator to correct. If
  `IdentityTranslationOptions.DefaultIdentity` is null/empty, `Translate()` returns the source
  identity unchanged (no throw); the identity remains flagged unresolved by Prepare.
- **Empty/whitespace vs. null path in `TranslatePath`**: a null, empty, or whitespace-only path
  input is treated identically as **untranslatable** — `TranslatePath()` returns `null` and the
  caller skips the path and logs the structured warning. There is no distinct empty-string path.

## Clarifications

### Session 2026-06-04

- Q: How is the "configured default identity" modelled, and what happens if it is null/invalid? → A: `PrepareAsync` is the validation gate — it verifies every source identity exists on the target and records non-matches as unresolved in `prepare-report.json` with a warning. `IdentityTranslationOptions` carries an optional `DefaultIdentity` (carried over from `IdentityLookupOptions`); when null/empty, `Translate()` returns the source unchanged. Correctness does not depend on a valid default — the unresolved set is surfaced at Prepare for operator correction.
- Q: How does `TranslatePath` treat empty/whitespace input vs. null? → A: Identically — null, empty, and whitespace-only inputs are all untranslatable: return `null`, caller skips and logs a warning.
- Q: Must `#if` guards in touched files follow the documented runtime compatibility procedure, and must non-compliant guards be refactored before feature edits? → A: Yes — mandatory. `.agents/20-guardrails/core/runtime-compatibility-net10-net481.md` Rule 11 (Refactor-First) applies to every touched file. Guards used for DI hiding, optional enablement, or architectural exclusion are non-compliant and must be remediated first. `TfsIdentityAdapter` belongs in the TFS agent project at the project-boundary seam, not behind `#if` guards. `IIdentitiesOrchestrator` must not contain `#if` guards at the interface level.

### Session 2026-06-03

- Q: What is the correct architecture for identity translation — what interfaces, roles, and call direction? → A: `IIdentityTranslationTool` (Tool, replaces `IIdentityLookupTool`, configured under `MigrationPlatform:Tools:IdentityTranslation`) is the cross-cutting seam used by all consumers. It calls `IIdentitiesOrchestrator` for cached results. `IdentitiesModule` also calls the Orchestrator for phase lifecycle. The Orchestrator calls `IIdentityAdapter` (connector-specific Adapter) during PrepareAsync. An ordered list of `IIdentityMatchingStrategy` implementations (`UpnIdentityMatchingStrategy`, `DisplayNameIdentityMatchingStrategy`) is applied by the Orchestrator during PrepareAsync. `Translate()` stays synchronous — no async promotion.
- Q: Which phase owns UPN/display-name matching — Import or Prepare? → A: PrepareAsync owns all live target-tenant queries via `IIdentityAdapter`. Import and translate-time calls use cached results only.
- Q: Should matching use a single hierarchical strategy or an ordered list of strategies? → A: Ordered list of `IIdentityMatchingStrategy` — each independently testable and injectable. The Orchestrator walks the list; the Adapter provides candidates once per identity per Prepare run.
- Q: What is the correct name for the node path mapping interface? → A: `INodeTranslationTool` (not `INodeTransformTool` — "Transform" belongs to the field-transform domain).
- Q: What metric naming segment should new identity metrics use? → A: `platform.identities.prepare.*` for Prepare-phase metrics (drop the `lookup` sub-segment); existing import-phase metrics retain `platform.identities.import.*`.

## Observability

### Operations

| Name | Type | Entry Point | Dependencies |
|---|---|---|---|
| `identity.prepare` | `module` | `IdentitiesModule.PrepareAsync()` → `IIdentitiesOrchestrator.PrepareAsync()` | `IIdentityAdapter` (target-tenant query, connector-specific), `IIdentityMatchingStrategy[]` (ordered match chain) |
| `identity.export` | `module` | `IdentitiesModule.ExportAsync()` → `IIdentitiesOrchestrator.ExportAsync()` | Package filesystem (reads source identity descriptors) |
| `identity.import` | `module` | `IdentitiesModule.ImportAsync()` → `IIdentitiesOrchestrator.ImportAsync()` | `IIdentitiesOrchestrator` (cached prepare results) |
| `identity.translate` | `tool` | `IIdentityTranslationTool.Translate()` | `IIdentitiesOrchestrator` (cached prepare results) |
| `nodes.import` | `module` | `NodesModule.ImportAsync()` | `INodesOrchestrator.ImportAsync()` |
| `team.paths.translate` | `module` | `TeamImportOrchestrator.TranslatePath()` | `INodeTranslationTool.TranslatePath()` |
| `team.members.import` | `module` | `TeamImportOrchestrator.ImportTeamAsync()` (member loop) | `IIdentityTranslationTool.Translate()`, `ITeamTarget.AddMemberAsync()` |
| `cli.queue` | `command` | `QueueCommand.ExecuteAsync()` | `ControlPlaneClient.SubmitAsync()` |

### Operator Decisions

| Operation | Decision | Question |
|---|---|---|
| `identity.prepare` | Is it working? | Did PrepareAsync complete with expected resolved and unresolved counts? |
| `identity.prepare` | Is it fast enough? | Is the target-tenant adapter query completing within acceptable latency? |
| `identity.prepare` | What failed? | Which source identity failed to match and why? |
| `identity.prepare` | Is it correct? | How many identities were resolved by UPN vs display-name vs default? |
| `identity.export` | Is it working? | Did ExportAsync complete and write all source identity descriptors? |
| `identity.export` | What failed? | Which source identity descriptor could not be exported? |
| `identity.import` | Is it working? | Did ImportAsync complete and initialise the translation cache? |
| `identity.import` | Is it correct? | Do resolved counts in the cache match PrepareAsync's prepared count? |
| `identity.translate` | Is it working? | Is the translation tool returning results for all callers? |
| `identity.translate` | What failed? | Which source identity returned the default (unresolved)? |
| `nodes.import` | Is it working? | Are node-import runs completing or being skipped as expected? |
| `nodes.import` | What failed? | Did an orchestrator call fail when it should have been skipped? |
| `team.paths.translate` | Is it working? | How many paths are being skipped as untranslatable? |
| `team.paths.translate` | Is it correct? | Are all untranslatable paths producing warnings rather than silent pass-through? |
| `team.members.import` | Is it working? | Are members being added at the expected rate? |
| `team.members.import` | What failed? | Which members were skipped due to unresolvable identity? |
| `cli.queue` | Is it working? | Are queue-command submissions succeeding? |
| `cli.queue` | What failed? | Did the control-plane submission fail, and why? |

### Metrics

Existing metric names are reused from `WellKnownAgentMetricNames` where semantics match. New names follow `platform.<domain>.<phase>.<measure>`.

| Metric Name | Instrument | Unit | Operation | Decision |
|---|---|---|---|---|
| `platform.identities.import.resolved` *(existing)* | `Counter<long>` | `{resolution}` | `identity.translate` | Is it working? / Is it correct? |
| `platform.identities.import.unresolved` *(existing)* | `Counter<long>` | `{resolution}` | `identity.translate` | Is it correct? |
| `platform.identities.import.duration_ms` *(existing)* | `Histogram<double>` | `ms` | `identity.translate` | Is it fast enough? |
| `platform.identities.import.errors` *(existing)* | `Counter<long>` | `{error}` | `identity.translate` | What failed? |
| `platform.identities.prepare.upn_matched` *(new)* | `Counter<long>` | `{resolution}` | `identity.prepare` | Is it correct? |
| `platform.identities.prepare.displayname_matched` *(new)* | `Counter<long>` | `{resolution}` | `identity.prepare` | Is it correct? |
| `platform.identities.prepare.ambiguous` *(new)* | `Counter<long>` | `{resolution}` | `identity.prepare` | Is it correct? / What failed? |
| `platform.identities.prepare.match_errors` *(new)* | `Counter<long>` | `{error}` | `identity.prepare` | What failed? |
| `platform.identities.prepare.duration_ms` *(new)* | `Histogram<double>` | `ms` | `identity.prepare` | Is it fast enough? |
| `platform.nodes.import.replicate.skipped` *(existing)* | `Counter<long>` | `{skip}` | `nodes.import` | Is it working? |
| `platform.nodes.import.replicate.errors` *(existing)* | `Counter<long>` | `{error}` | `nodes.import` | What failed? |
| `platform.teams.import.iterations.unresolvable` *(existing)* | `Counter<long>` | `{path}` | `team.paths.translate` | Is it working? / Is it correct? |
| `platform.teams.import.areas.unresolvable` *(new)* | `Counter<long>` | `{path}` | `team.paths.translate` | Is it working? / Is it correct? |
| `platform.teams.import.members.unresolved` *(existing)* | `Counter<long>` | `{member}` | `team.members.import` | What failed? |
| `platform.teams.import.members.count` *(existing)* | `Counter<long>` | `{member}` | `team.members.import` | Is it working? |
| `platform.command.execute.invocations` *(existing)* | `Counter<long>` | `{command}` | `cli.queue` | Is it working? |
| `platform.command.execute.errors` *(existing)* | `Counter<long>` | `{command}` | `cli.queue` | What failed? |
| `platform.command.execute.duration_ms` *(existing)* | `Histogram<double>` | `ms` | `cli.queue` | Is it fast enough? |

### Traces

Activity source: `DevOpsMigrationPlatform.Migration` for agent operations; `DevOpsMigrationPlatform.CLI` for CLI operations.

| Component | Span Name | Tags | Parent | Decision |
|---|---|---|---|---|
| `IdentitiesOrchestrator` | `identity.prepare` | `job.id`, `operation=prepare`, `module=Identities` | Root | Is it working? / Is it fast enough? |
| `IIdentityAdapter` | `identity.adapter.upn` | `job.id`, `operation=prepare` | `identity.prepare` | What failed? / Where is it slow? |
| `IIdentityAdapter` | `identity.adapter.displayname` | `job.id`, `operation=prepare` | `identity.prepare` | What failed? / Where is it slow? |
| `IIdentityTranslationTool` | `identity.translate` | `job.id`, `operation=import`, `module=Identities` | Root | Is it working? |
| `NodesModule` | `nodes.import` | `job.id`, `operation=import`, `module=Nodes` | Root | Is it working? |
| `INodesOrchestrator` | `nodes.import.orchestrate` | `job.id`, `operation=import` | `nodes.import` | What failed? |
| `TeamImportOrchestrator` | `team.paths.translate` | `job.id`, `operation=import`, `module=Teams` | Root | Is it working? / Is it correct? |
| `INodeTranslationTool` | `nodes.translate` | `job.id`, `operation=import` | `team.paths.translate` | Where is it slow? |
| `TeamImportOrchestrator` | `team.members.import` | `job.id`, `operation=import`, `module=Teams` | Root | Is it working? |
| `ITeamTarget` | `team.target.addmember` | `job.id`, `operation=import` | `team.members.import` | What failed? |
| `QueueCommand` | `cli.queue` | `command=queue`, `exit.code` | Root | Is it working? / What failed? |

**Context propagation:** Automatic via `Activity` hierarchy (W3C TraceContext). `Activity.Current.TraceId` is the correlation root for all child spans within a job run.

### Logging

| Event | Level | Fields | Operation | Decision |
|---|---|---|---|---|
| Identity prepare started | `Information` | `operationId`, `module=Identities`, `sourceCount` | `identity.prepare` | Is it working? |
| Identity resolved via UPN | `Information` | `operationId`, `resolutionStep=UPN` | `identity.prepare` | Is it correct? |
| Identity resolved via display name | `Information` | `operationId`, `resolutionStep=DisplayName` | `identity.prepare` | Is it correct? |
| Identity resolution ambiguous | `Warning` | `operationId`, `displayName`, `matchCount` | `identity.prepare` | Is it correct? / What failed? |
| Identity adapter query failed | `Warning` | `operationId`, `errorType`, `errorMessage` | `identity.prepare` | What failed? |
| Identity resolved via default | `Information` | `operationId`, `resolutionStep=Default` | `identity.prepare` | Is it correct? |
| Identity prepare completed | `Information` | `operationId`, `module=Identities`, `resolvedCount`, `unresolvedCount`, `durationMs` | `identity.prepare` | Is it working? / Is it fast enough? |
| Identity translate returned default | `Information` | `operationId`, `resolutionStep=Default` | `identity.translate` | What failed? |
| Nodes import skipped | `Information` | `operationId`, `module=Nodes`, `reason` | `nodes.import` | Is it working? |
| Path translation untranslatable | `Warning` | `operationId`, `module=Teams`, `pathType` (area/iteration), `sourcePath` | `team.paths.translate` | Is it correct? |
| Default area path untranslatable | `Warning` | `operationId`, `module=Teams`, `pathType=DefaultArea` | `team.paths.translate` | Is it correct? |
| Member identity unresolvable | `Warning` | `operationId`, `module=Teams`, `memberDescriptor` | `team.members.import` | What failed? |
| Default team detected | `Warning` | `operationId`, `module=Teams`, `teamName`, `isDefault=true`, `message="target API does not support explicit default team assignment"` | `team.members.import` | What failed? |
| CLI queue started | `Information` | `operationId`, `command=queue` | `cli.queue` | Is it working? |
| CLI queue completed | `Information` | `operationId`, `command=queue`, `outcome`, `durationMs` | `cli.queue` | Is it working? |
| CLI queue failed | `Error` | `operationId`, `command=queue`, `errorType`, `errorMessage`, `durationMs` | `cli.queue` | What failed? |
| Per-identity prepare detail | `Debug` | `operationId`, `step`, `lookupType` | `identity.prepare` | (diagnostic only) |

> Debug and Trace levels are disabled by default.

### Correlation

| Field | Source | Scope |
|---|---|---|
| `operationId` / `traceId` | `Activity.Current.TraceId` (or generated GUID when no ambient activity) | All telemetry for every operation |
| `parentId` | `Activity.Current.ParentSpanId` | Child spans and their log events |
| `job.id` | Job context injected via `MigrationTagList` | All agent-side telemetry |
| `operation` | Low-cardinality constant (`"import"`, `"queue"`) | Root spans and metric tags |
| `module` | Low-cardinality module name (`"Identities"`, `"Nodes"`, `"Teams"`) | Root spans and metric tags |
| `command` | CLI command name (`"queue"`) | CLI span tags and metric tags |

### Validation Queries

#### Failure Identification
```kql
// Identity prepare failures and ambiguous matches by job
customMetrics
| where name == "platform.identities.import.errors"
    or name == "platform.identities.prepare.match_errors"
    or name == "platform.identities.prepare.ambiguous"
| summarize count() by name, tostring(customDimensions["job.id"]), bin(timestamp, 1m)
```

#### Latency Analysis
```kql
// P50/P95/P99 latency for identity prepare (adapter queries) and translation
customMetrics
| where name == "platform.identities.prepare.duration_ms"
    or name == "platform.identities.import.duration_ms"
| summarize
    p50 = percentile(value, 50),
    p95 = percentile(value, 95),
    p99 = percentile(value, 99)
  by tostring(customDimensions["job.id"]), bin(timestamp, 5m)
```

#### Load Observation
```kql
// Unresolvable team path and member counts per job
customMetrics
| where name in (
    "platform.teams.import.areas.unresolvable",
    "platform.teams.import.iterations.unresolvable",
    "platform.teams.import.members.unresolved"
  )
| summarize total = sum(value) by name, tostring(customDimensions["job.id"])
```

#### End-to-End Trace
```kql
// Trace identity prepare (adapter queries) and translate calls end-to-end
dependencies
| where name startswith "identity."
| where operation_Id == "<traceId>"
| project timestamp, name, duration, success, operation_ParentId
| order by timestamp asc
```

#### Error Diagnosis
```kql
// Correlate CLI queue submission failures with the command span
traces
| where customDimensions["command"] == "queue" and severityLevel >= 3
| join kind=leftouter (
    dependencies | where name == "cli.queue"
  ) on operation_Id
| project timestamp, message, customDimensions, duration
```

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `IdentitiesOrchestrator.PrepareAsync()` MUST implement UPN/email matching
  as step 2 of the resolution order by calling `IIdentityAdapter` to retrieve target
  candidates, then applying `UpnIdentityMatchingStrategy` from the ordered strategy list.
- **FR-002**: `IdentitiesOrchestrator.PrepareAsync()` MUST implement display-name matching
  as step 3 by applying `DisplayNameIdentityMatchingStrategy` when UPN matching yields no
  result. When multiple target identities share the same display name the match is
  ambiguous: a structured warning is logged and the identity is recorded as unresolved.
- **FR-003**: Display-name comparison MUST be case-insensitive and MUST normalise both
  strings using Unicode NFC and strip leading/trailing whitespace before comparing.
- **FR-004**: `IIdentityTranslationTool` is an optional Tool extension configured under
  `MigrationPlatform:Tools:IdentityTranslation`. It replaces `IIdentityLookupTool` as the
  cross-cutting identity seam injected into `TeamImportOrchestrator`, `RevisionFolderProcessor`,
  `WorkItemsModule`, and `IdentitiesModule`. `Translate()` remains synchronous — it returns
  cached Prepare-phase results via `IIdentitiesOrchestrator`; no live I/O occurs at translate time.
  `IdentityTranslationOptions` carries an optional `DefaultIdentity` (carried over from
  `IdentityLookupOptions.DefaultIdentity`); when null/empty, `Translate()` returns the source
  identity unchanged. Target-existence validation is owned by `PrepareAsync`, not by this default.
- **FR-005**: `IIdentityAdapter` MUST be implemented for all three connectors:
  `SimulatedIdentityAdapter` (in-memory deterministic store), `AzureDevOpsIdentityAdapter`
  (ADO Graph API), and `TfsIdentityAdapter` (TFS Identity Service REST). The TFS adapter
  MUST log a structured warning and return no candidates when the TFS Identity Service
  does not expose UPN or display-name search, causing fallback to the configured default.
- **FR-006**: `NodesModuleOptions` MUST NOT contain `AutoCreateNodes`; the feature-file
  scenario referencing `NodesModule` and `AutoCreateNodes` MUST be deleted and the
  deletion recorded as `Status: RESOLVED` in `analysis/dsl-gaps-detected.md`.
- **FR-007**: `NodesModule.ImportAsync` MUST return `Skipped` when
  `ReplicateSourceTree = false` or when `Enabled = false`, without calling the
  orchestrator.
- **FR-008**: All references to `INodeEnsurer` in the codebase MUST be replaced with
  `INodesOrchestrator`. No symbol named `INodeEnsurer` may exist after this change.
- **FR-009**: `TeamImportOrchestrator.TranslatePath()` MUST return `null` when
  `result.TargetPath` is null, and MUST also return `null` when the input path is null,
  empty, or whitespace-only (all treated as untranslatable). All call sites of `TranslatePath()` in the codebase MUST
  be audited and updated to handle a null return value explicitly — either by skipping
  the path or by applying a documented fallback — before this spec is considered
  complete. No `NullReferenceException` may result from a null return.
- **FR-010**: `TeamImportOrchestrator` MUST skip `AddMemberAsync` and log a structured
  warning (including the original member descriptor) when identity resolution returns
  the configured default identity.
- **FR-011**: `TeamImportOrchestrator` MUST log a structured warning containing the
  team name and the text `"target API does not support explicit default team
  assignment"` when a team with `IsDefault = true` is detected, and MUST continue
  import without error.
- **FR-012**: The `@us1-write-idempotency` scenario in
  `features/export/config-in-package/config-applied-on-export.feature` MUST be deleted.
  The CLI has no access to the package filesystem; a pre-submission config-exists check
  is architecturally impossible. The gap MUST be marked `Status: RESOLVED` with this
  rationale recorded. No production code change is required for this gap.
- **FR-013**: Export metrics (`migration.workitems.attempted`, `migration.workitems.retried`,
  `migration.workitem.duration.ms`) MUST be verifiable in unit tests via an OTel
  in-memory exporter scoped to the test lifetime.
- **FR-014**: `MetricSnapshot` payload histogram values MUST be verifiable via the OTel
  in-memory exporter scoped to the test lifetime, without requiring the full platform
  pipeline.
- **FR-015**: Every gap entry in `analysis/dsl-gaps-detected.md` (GAP-001 through
  GAP-009) MUST be marked `Status: RESOLVED` with a resolution date upon completion of
  the corresponding fix. Deletion rationale for scenarios removed from feature files
  MUST be recorded in the `Status: RESOLVED` entry.
- **FR-016**: `IIdentityLookupTool` MUST be **renamed** (not deleted-and-recreated) to
  `IIdentityTranslationTool` to preserve git history. Use `git mv` for the interface,
  its implementation (`IdentityLookupTool` → `IdentityTranslationTool`), the options class
  (`IdentityLookupOptions` → `IdentityTranslationOptions`), and the DI extension files, then
  rename the symbols in place. The method `Resolve()` is renamed to `Translate()`. Every
  consumer that references the type (the full set — **16 source files** including the
  `IRevisionFolderProcessorFactory` / `IWorkItemsOrchestratorFactory` abstractions and the
  WorkItem-resolution factory/runtime chain — see discrepancies D-001) is updated by the
  rename. The config section moves from `MigrationPlatform:Tools:IdentityLookup` to
  `MigrationPlatform:Tools:IdentityTranslation`. No symbol named `IIdentityLookupTool`,
  `IdentityLookupTool`, `IdentityLookupOptions`, or field `_identityLookupTool` may exist
  after this change. (`IdentityTranslationOptions` retains the existing `DefaultIdentity`
  property by preservation — see FR-004.)
- **FR-017**: The field `_NodeTransformTool` in `TeamImportOrchestrator` MUST be
  renamed to `_nodeTranslationTool` to match the canonical interface name
  `INodeTranslationTool`. No field or variable named `_NodeTransformTool` may exist
  after this change is complete.
- **FR-018**: Every file touched by this spec that contains a `#if` / `#if !NET481`
  preprocessor guard MUST be assessed against
  `.agents/20-guardrails/core/runtime-compatibility-net10-net481.md` before any
  feature edits are made in that file (Refactor-First, Rule 11). Non-compliant guards
  — those used for DI hiding, optional enablement, or architectural exclusion rather
  than crash-prevention — MUST be refactored to comply with the Implementation
  Hierarchy (target-specific files, partials, or separate assemblies) before the
  feature change proceeds. Evidence must be provided for all seven Required Review
  Questions in the guardrail. Missing evidence is treated as non-compliance.
- **FR-019**: `TfsIdentityAdapter` MUST be placed in the TFS agent project
  (`DevOpsMigrationPlatform.TfsMigrationAgent` or equivalent net481 project), NOT
  in a shared project behind `#if NET481` guards. The project boundary is the correct
  runtime isolation seam. The adapter's reduced capability (returning empty candidates
  when UPN/display-name search is unavailable) MUST be modeled explicitly in its
  contract result — not hidden in preprocessor guards, DI registration, or comments.
- **FR-020**: `IIdentitiesOrchestrator` MUST NOT contain `#if`/`#if !NET481` guards
  at the interface level. Runtime-specific behavior MUST be isolated to target-specific
  implementations. The current `#if !NET481` guard around `ImportAsync` in the
  interface definition is non-compliant and MUST be removed as part of the
  Refactor-First step before feature edits to `IdentitiesOrchestrator`.

### Key Entities

- **`IIdentityTranslationTool`**: Optional Tool extension (configured under
  `MigrationPlatform:Tools:IdentityTranslation`). The cross-cutting identity seam used by
  `TeamImportOrchestrator`, `RevisionFolderProcessor`, `WorkItemsModule`, and `IdentitiesModule`.
  `Translate()` is synchronous — delegates to `IIdentitiesOrchestrator` for cached results.
  Replaces `IIdentityLookupTool` as the canonical Tool seam across all consumers.
- **`IIdentitiesOrchestrator`**: Called by `IdentitiesModule` for all three phase
  lifecycle methods (`PrepareAsync`, `ExportAsync`, `ImportAsync`) and by
  `IIdentityTranslationTool` for cached resolution queries at translate time.
  `PrepareAsync` calls `IIdentityAdapter` and runs the ordered `IIdentityMatchingStrategy`
  list, caching results for the remaining phases. `ExportAsync` reads and exports source
  identity descriptors from the package. `ImportAsync` loads the cached prepare results
  to initialise the translation seam for the import run.
- **`IIdentityAdapter`**: Connector-specific abstraction for querying the live target
  tenant by UPN or display name. Implemented by `AzureDevOpsIdentityAdapter`,
  `TfsIdentityAdapter`, and `SimulatedIdentityAdapter`. Called by `IIdentitiesOrchestrator`
  during `PrepareAsync` only — not during import or translate.
- **`IIdentityMatchingStrategy`**: Pluggable matching variant (Strategy). Ordered list
  applied by the Orchestrator during PrepareAsync. Implementations:
  `UpnIdentityMatchingStrategy`, `DisplayNameIdentityMatchingStrategy`.
- **`NodesModuleOptions`**: Configuration for the `NodesModule` — controls `Enabled` and
  `ReplicateSourceTree` only. Does not contain `AutoCreateNodes`.
- **`NodeTranslationOptions`**: Separate options class owning `AutoCreateNodes` under
  config path `MigrationPlatform:Tools:NodeTranslation`.
- **`INodesOrchestrator`**: The actual interface for node operations (replaces the
  non-existent `INodeEnsurer`).
- **`TeamImportOrchestrator`**: Orchestrates team import including area paths, iteration
  paths, and member assignment. `TranslatePath()` now returns `null` for untranslatable
  paths.
- **`TranslatePath` result**: Null means no mapping exists and the path must be skipped
  by the caller.
- **OTel in-memory exporter**: Test-infrastructure component that captures metric
  counters and histograms for assertion. Must be scoped per test to prevent
  counter accumulation.

## Connector Coverage

### Features

| Feature | Type | Abstraction | Simulated | AzureDevOps | TFS |
|---|---|---|---|---|---|
| `identity.target-lookup` | `prepare` | `IIdentityAdapter` (new) | Required | Required | Required |
| `nodes.import.skip-guard` | `import` | `NodesModule` (internal) | N/A | N/A | N/A |
| `team.paths.translate` | `import` | `TeamImportOrchestrator` (internal) | N/A | N/A | N/A |
| `team.members.skip-unresolved` | `import` | `TeamImportOrchestrator` (internal) | N/A | N/A | N/A |

**Note:** `nodes.import.skip-guard`, `team.paths.translate`, and `team.members.skip-unresolved`
are connector-agnostic changes — they modify internal orchestration logic that does not interact
with source or target system APIs. Connector Coverage is N/A for those features.

**Note:** GAP-007 (the former `cli.queue.config-check`) is resolved by *deletion* of the
architecturally impossible scenario (FR-012) — the CLI has no package-filesystem access. No
connector-relevant feature remains for it, so it is intentionally absent from this table.

**Note:** `IIdentityLookupTool` currently exists and is used for package-based resolution.
It is superseded by `IIdentityTranslationTool` (the new cross-cutting Tool seam) in this spec.
`IIdentityAdapter` is the new connector-specific abstraction for querying the live target tenant
during PrepareAsync. It is distinct from `IIdentityTranslationTool` — the Adapter is called by
the Orchestrator, not by external consumers.

### Acceptance Scenario Mapping

| Feature | Connector | Scenario(s) |
|---|---|---|
| `identity.target-lookup` | Simulated | US1 Scenario 10 — "Simulated connector: same scenarios 1–7, resolved against in-memory simulated identity store" |
| `identity.target-lookup` | AzureDevOps | US1 Scenario 8 — "AzureDevOpsServices connector: same scenarios 1–7, via Azure DevOps Graph API abstraction" |
| `identity.target-lookup` | TFS | US1 Scenario 9 — "TeamFoundationServer connector: same scenarios 1–7, via TFS Identity Service; graceful fall-through with structured warning if TFS Identity Service does not expose the required search" |

### TFS Exemptions (if any)

| Feature | Reason | Graceful Behaviour |
|---|---|---|
| `identity.target-lookup` (partial) | The TFS Identity Service REST endpoint (`_apis/identities`) may not support UPN or display-name search on older TFS versions (pre-2017). The TFS agent runs .NET 4.8 but the `IdentityMappingService` runs in .NET 10 and queries TFS via REST — this is supported on TFS 2017+. For older TFS, the search returns no results. | When the TFS REST Identity Service returns no match for UPN or display-name, the connector MUST log a structured `Warning` with the TFS version and fall through to the configured default identity. It MUST NOT throw. |

### Gaps (if any)

| Feature | Connector | Gap | Required Action |
|---|---|---|---|
| `identity.target-lookup` | Simulated | No scenarios | Covered by US1 Scenario 10 (added in this spec) |
| `identity.target-lookup` | AzureDevOps | No scenarios | Covered by US1 Scenario 8 (added in this spec) |
| `identity.target-lookup` | TFS | No scenarios | Covered by US1 Scenario 9 (added in this spec) |

No missing scenarios remain. All Required feature-connector pairs have acceptance scenario coverage.

### Verdict

**PASS** — All connector-relevant features have complete connector coverage in the specification.
All three connectors (Simulated, AzureDevOpsServices, TeamFoundationServer) have explicit
acceptance scenarios in User Story 1. TFS partial exemption is documented with graceful
behaviour specified. Connector-agnostic features are correctly marked N/A.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All 9 open gaps in `analysis/dsl-gaps-detected.md` — GAP-001, GAP-002,
  GAP-003, GAP-004, GAP-005, GAP-006, GAP-007, GAP-008, and GAP-009 — are marked
  `Status: RESOLVED` and no new open gaps are introduced.
- **SC-002**: All previously blocked feature-file scenarios referenced in the gap log
  either pass in the test suite or are formally deleted. Deletion rationale MUST be
  recorded as the `Status: RESOLVED` entry in `analysis/dsl-gaps-detected.md`.
- **SC-003**: Identity resolution correctly produces the target identity for UPN-matched
  and display-name-matched sources in 100% of test cases across all three connectors.
- **SC-004**: Untranslatable area and iteration paths in team import produce a structured
  warning and are excluded — verified by 100% of relevant test scenarios passing.
- **SC-005**: The `@us1-write-idempotency` scenario is absent from the feature file,
  GAP-007 is marked `Status: RESOLVED`, and the architectural rationale is recorded
  in `analysis/dsl-gaps-detected.md`.
- **SC-006**: OTel counter and histogram values for export metrics are assertable in
  deterministic, per-test-scoped unit tests without standing up the full platform.

## Assumptions

- `IIdentityLookupTool` currently exists and is superseded by `IIdentityTranslationTool`
  in this spec. `IIdentityTranslationTool` is the new optional Tool (configured under
  `MigrationPlatform:Tools:IdentityTranslation`) replacing `IIdentityLookupTool` as the
  cross-cutting identity seam in all consumers.
- `IIdentityTranslationTool.Translate()` remains synchronous — live target-tenant queries
  happen in PrepareAsync via `IIdentityAdapter`, not at translate time. No async promotion
  of the translate seam is required.
- `IIdentityAdapter` is a new connector-specific Adapter abstraction called only by
  `IIdentitiesOrchestrator` during PrepareAsync. Implementations required:
  `AzureDevOpsIdentityAdapter`, `TfsIdentityAdapter`, `SimulatedIdentityAdapter`.
- `IIdentityMatchingStrategy` is a new Strategy abstraction applied by the Orchestrator
  in PrepareAsync. Two implementations required: `UpnIdentityMatchingStrategy` and
  `DisplayNameIdentityMatchingStrategy`.
- Display-name matching uses Unicode NFC normalisation and case-insensitive exact match
  (no fuzzy matching). If the match is ambiguous (multiple target identities), the
  configured default is used and a warning is logged.
- GAP-004 (default team assignment) is a permanent Azure DevOps API limitation;
  resolution is documentation + scenario deletion + confirming the existing warning
  log is correctly structured.
- The OTel in-memory exporter (`AddInMemoryExporter`) requires verifying the exact
  NuGet package version available in the project's pinned dependencies before use.
  If not already a dependency, it must be added and pinned in `Directory.Packages.props`.
- GAP-007 resolution requires no production code change. The CLI has no access to the
  package filesystem by architectural design. The gap is closed by deleting the
  aspirational scenario and documenting the agent's existing resume semantics.
- `TranslatePath()` returning null is a breaking API change. The caller-audit scope
  covers all orchestrators and services in the codebase that call this method —
  not limited to `TeamImportOrchestrator`. Every call site must be updated.
- The `null!` first argument to `AddMemberAsync` in the current code is a pre-existing
  defect in the method signature (context parameter not yet wired). Fixing it is out of
  scope for this spec unless it directly blocks a GAP-006 acceptance scenario.
- Guard compliance is non-negotiable. Every `#if`/`#if !NET481` guard in a file
  touched by this spec must be assessed against the runtime compatibility guardrail
  before feature edits in that file. Non-compliant guards are refactored first — not
  deferred, not left as-is. `TfsIdentityAdapter` uses no guards: it lives in the TFS
  agent project (net481 project boundary is the isolation seam). `IIdentitiesOrchestrator`
  must remove its interface-level `#if !NET481` guard on `ImportAsync`; runtime-specific
  behavior moves to target-specific implementations. Existing `#if !NET481` guards in
  `IdentitiesModule.cs` around `IIdentityLookupTool` are removed by FR-016 (deletion
  of `IIdentityLookupTool`) — this is sufficient remediation for those guards.
