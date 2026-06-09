# DSL Migration Gaps Detected

This file is a **running engineering log** maintained automatically by the `nkda-testdsl-*` skills. Every time a `.feature` file or scenario is skipped, blocked, or fails conversion for an engineering reason, the skills append an entry here.

**Do not edit manually to resolve gaps** — fix the underlying engineering issue and re-run the migration. Mark resolved entries with a `Status: RESOLVED` line and the date.

---

## Gap Type Reference

| gap-type | Meaning |
|---|---|
| `unmatched-step` | Step definition exists in `.feature` but no matching step binding or DSL method can be found or inferred |
| `intent-unknown` | Scenario is `unwired`/`miswired` and intent cannot be safely inferred from scenario text alone |
| `parity-gap` | Converted test exists but does not cover equivalent assertions to the original scenario |
| `behaviour-conflict` | Converted test assertion contradicts observed production behaviour |
| `test-failure` | Converted test was written but fails and the failure cannot be resolved at migration time |
| `validity-gate` | Intent-derived test fails the validity gate (does not prove a real behaviour) |
| `dsl-missing-builder` | Required DSL builder or runner not yet implemented in `DevOpsMigrationPlatform.Testing` |
| `dsl-missing-assertion` | Required DSL assertion method not yet implemented |
| `infrastructure` | External infrastructure reason (e.g., test project missing, build broken) |
| `other` | Any other engineering reason — must include a specific detail |

---

## Open Gaps

Gaps surfaced during feature-to-DSL migration where a scenario's expected behaviour
cannot be confirmed against observed production code.

---

## GAP-001: IdentityMappingService — UPN and display-name matching unimplemented

**Detected during:** migration of `features/import/identities/identity-mapping-resolution.feature` (scenario 2)
**Status:** RESOLVED (2026-06-04) — IdentitiesOrchestrator.PrepareAsync now implements UPN matching (step 2) and display-name matching (step 3) against the target tenant via IIdentityAdapter and an ordered IIdentityMatchingStrategy list; results are cached and read by IIdentityTranslationTool.Translate. Implemented for all three connectors (SimulatedIdentityAdapter, AzureDevOpsIdentityAdapter via SDK IdentityHttpClient, TfsIdentityAdapter reduced-capability). Verified by IdentitiesOrchestratorPrepareTests, Upn/DisplayName strategy tests, SimulatedIdentityAdapterTests, CompositeIdentityAdapterTests, TfsIdentityAdapterTests, and live ADO SystemTests.

### What the docstring promises

`src/DevOpsMigrationPlatform.Infrastructure.Agent/Identity/IdentityMappingService.cs:21`

```
/// Resolution order:
/// 1. Explicit override from Identities/mapping.json
/// 2. UPN/email matching
/// 3. Display name matching
/// 4. Configured default identity (falls back to the source identity when not set)
```

### What the implementation does

`Resolve()` (lines 69–88) implements only steps 1 and 4:

```csharp
if (_overrides.TryGetValue(sourceIdentity, out var mapped))
    return mapped;          // step 1 only

return FallbackIdentity(sourceIdentity);  // step 4 — skips 2 and 3 entirely
```

### Blocked scenario

```gherkin
Scenario: Automatic UPN match resolves identity
  Given a source identity "bob@source.com" with display name "Bob Smith"
  And the mapping.json file has no override for "bob@source.com"
  When the identity is resolved
  Then the resolved identity is "bob@target.com"
```

This outcome is impossible with the current implementation — `bob@target.com` would
never be produced unless it were an explicit mapping entry.

### Resolution options

1. **Implement UPN/email and display-name matching** — query the target tenant for
   identities matching the source UPN and auto-resolve. The docstring then becomes
   accurate and the scenario can be retired.

2. **Remove the unimplemented steps from the docstring** — if auto-matching is out of
   scope, correct the docstring to reflect two-step resolution (explicit override →
   configured default) and delete the blocked scenario.

---

## GAP-002: NodesModule — AutoCreateNodes attributed to wrong options class

**Detected during:** migration of `features/import/nodes/import-classification-tree.feature` (scenario 2)
**gap-type:** `behaviour-conflict`
**Status:** RESOLVED (2026-06-04) — INodeEnsurer references eliminated (already absent); AutoCreateNodes confirmed on NodeTranslationOptions (not NodesModuleOptions); the misattributed feature scenario was deleted from import-classification-tree.feature.

### What the feature claims

```gherkin
Scenario: AutoCreateNodes ensures referenced paths exist on target
  And NodesModule is configured with AutoCreateNodes = true
  Then INodeEnsurer.EnsureReferencedPathsAsync is invoked
```

### What the code shows

`NodesModuleOptions` (`src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/NodesModuleOptions.cs`) has only `Enabled` and `ReplicateSourceTree`. There is no `AutoCreateNodes` property.

`AutoCreateNodes` exists on `NodeTranslationOptions` (`src/DevOpsMigrationPlatform.Abstractions/Options/NodeTranslationOptions.cs:56`) under config path `MigrationPlatform:Tools:NodeTranslation`. This controls node pre-creation before the work-item revision loop — a different concern from the classification-tree import driven by `NodesModule`.

`NodesModule.ImportAsync` never reads `AutoCreateNodes`.

### Resolution options

1. **Accept the current design** — clarify in the feature that `AutoCreateNodes` is a `NodeTranslation` tool option, not a `NodesModule` option, and rewrite the scenario to target `NodeTranslationOptions`. Delete or rewrite the blocked scenario.
2. **Add AutoCreateNodes to NodesModuleOptions** — if the intent is that NodesModule should also support an `AutoCreateNodes` mode, add the property and wire it through `NodesModule.ImportAsync`.

---

## GAP-004: TeamsModule — Default team assignment not implemented; target API unsupported

**Detected during:** migration of `features/import/teams/import-default-team-detection.feature` (scenario 1)
**gap-type:** `behaviour-conflict`
**Status:** RESOLVED (2026-06-04) — Permanent Azure DevOps API limitation (no explicit default-team assignment). TeamImportOrchestrator logs a structured Warning containing the team name and the exact text "target API does not support explicit default team assignment" and continues import (FR-011). Verified by TeamsModuleTests (GAP004 warning test).

### What the feature claims

```gherkin
Scenario: Source default team maps to target default team by IsDefault flag not by name
  Then the default team settings from the source are applied to the target default team
  And no name-matching is used to determine the default team
```

### What the code shows

`TeamImportOrchestrator.ImportTeamAsync` (`src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs:64`):

```csharp
if (teamPackage.Definition.IsDefault)
{
    _logger.LogWarning(
        "[Teams] Default team '{Name}' detected — target API does not support explicit default team assignment. " +
        "Ensure the target project's default team matches the source.",
        teamPackage.Definition.Name);
}
```

The default team is detected but no settings are applied to the target's default team. The comment explicitly states the target API does not support this operation.

### Resolution options

1. **Implement default team assignment** — add an `ITeamTarget.SetDefaultTeamAsync` method (if/when the ADO API supports it) and call it in `TeamImportOrchestrator` when `IsDefault=true`.
2. **Delete the scenario** — if this is a known permanent limitation, remove the scenario and document the limitation in operator guidance.

---

## GAP-005: TeamImportOrchestrator — TranslatePath() always falls back to source; skip-on-untranslatable unreachable

**Detected during:** migration of `features/import/teams/import-team-area-paths.feature` (scenarios 2 and 3)
**gap-type:** `behaviour-conflict`
**Status:** RESOLVED (2026-06-04) — TeamImportOrchestrator.TranslatePath no longer falls back to the source path: it returns null when the tool cannot map a path (and for null/empty/whitespace input), so callers skip the path and log a structured warning instead of corrupting the target with source-side paths (FR-009). Private method; the three internal callers already handle null. Verified by TeamsModuleTests (incl. ImportAsync_SkipsIteration_WhenPathUntranslatable_GAP005).

### What the feature claims

- Scenario 2: when `NodeTransformTool` returns null for an included area path, the path is skipped and a warning is logged.
- Scenario 3: when `NodeTransformTool` returns null for the default area path, `SetAreaPathsAsync` is not called at all.

### What the code shows

`TeamImportOrchestrator.TranslatePath()` (`src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs:190-200`):

```csharp
var result = _NodeTransformTool.TranslatePath(fieldName, sourcePath!, projectMapping);
return result.TargetPath ?? sourcePath;
```

When `result.TargetPath` is null, it returns the original `sourcePath` — never null for a non-empty path. This means:

- The `else _logger.LogWarning(...)` branch at line 150 (included paths loop) is unreachable for non-empty paths.
- The `if (defaultPath is not null)` guard at line 142 always passes for a non-empty default path.

In practice: untranslatable paths are silently passed through as-is, not skipped.

### Resolution options

1. **Change the fallback semantics** — return null from `TranslatePath` when `result.TargetPath` is null (remove `?? sourcePath`), making untranslatable paths genuinely skippable. Update callers to handle null explicitly.
2. **Use a dedicated "not found" signal** — check `result.Translated` or a similar flag instead of null-checking `TargetPath`, and skip when the translation tool reports the path as unresolvable.
3. **Accept pass-through as correct** — if falling back to the source path is intentional, delete scenarios 2 and 3 and document the pass-through behaviour.

**Additional occurrence:** `features/import/teams/import-team-iterations.feature` scenario 2 ("Unresolvable iteration path is skipped with a warning") hits the same unreachable branch in the iterations loop (`TeamImportOrchestrator.cs:~93`). Fix GAP-005 to resolve both.

---

## GAP-006: TeamImportOrchestrator — No skip-on-unresolvable for member identity; warning only on AddMemberAsync failure

**Detected during:** migration of `features/import/teams/import-team-members.feature` (scenario 2)
**gap-type:** `behaviour-conflict`
**Status:** RESOLVED (2026-06-04) — TeamImportOrchestrator now skips AddMemberAsync and logs a structured Warning (member descriptor + display name) when identity translation resolves a member to the configured default identity, instead of importing under the wrong identity (FR-010). IIdentityTranslationTool.DefaultIdentity exposes the default for the comparison. Verified by TeamsModuleTests (GAP006 skip + add tests).

### What the feature claims

```gherkin
Scenario: Unresolvable member identity is skipped with warning
  And the IdentityMappingService returns the default identity for "src-unknown"
  Then a warning is logged for the unresolvable member
```

### What the code shows

`TeamImportOrchestrator.ImportTeamAsync` (`src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs:116-135`):

```csharp
var resolvedDescriptor = extensions.IdentityLookup && _identityLookupTool?.IsEnabled == true
    ? _identityLookupTool.Resolve(member.Descriptor)
    : member.Descriptor;
var resolvedMember = member with { Descriptor = resolvedDescriptor };
await _teamTarget.AddMemberAsync(null!, projectName, targetTeamId, resolvedMember, ct);
```

`AddMemberAsync` is always called with whatever `Resolve()` returns — no check for "did the identity resolve to a default?" before adding. The `_logger.LogWarning` in the `catch` block fires only when `AddMemberAsync` itself throws, not when the identity fell back to the configured default.

### Resolution options

1. **Add an unresolved-identity check** — if `_identityLookupTool.Resolve()` returns the configured default (or a sentinel indicating fallback), log a warning and skip `AddMemberAsync`.
2. **Accept always-add behaviour** — if adding members with the default identity is intentional, delete the scenario and document that unresolvable members are imported under the default identity.

---

## GAP-003: NodesModule — INodeEnsurer does not exist; no skip-when-both-false guard

**Detected during:** migration of `features/import/nodes/import-classification-tree.feature` (scenario 3)
**gap-type:** `behaviour-conflict`
**Status:** RESOLVED (2026-06-04) — NodesModule.ImportAsync now returns Skipped without calling INodesOrchestrator when ReplicateSourceTree is false (FR-007); _NodeTransformTool renamed to _nodeTranslationTool (FR-017); INodeEnsurer-based scenarios removed. Verified by NodesModuleTests.

### What the feature claims

```gherkin
Scenario: Import is skipped when both ReplicateSourceTree and AutoCreateNodes are false
  Given NodesModule is configured with ReplicateSourceTree = false and AutoCreateNodes = false
  When NodesModule ImportAsync runs
  Then neither INodeEnsurer method is invoked
```

### What the code shows

1. `INodeEnsurer` does not exist anywhere in the codebase. The actual interface used is `INodesOrchestrator`.
2. `NodesModule.ImportAsync` (`src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs:240`) always calls `_orchestrator.ImportAsync(...)` when `Enabled = true`. There is no "both-false → skip" guard at the module level.

### Resolution options

1. **Add a skip guard to NodesModule.ImportAsync** — when both `ReplicateSourceTree = false` and `AutoCreateNodes = false` (if GAP-002 is resolved by adding `AutoCreateNodes` to `NodesModuleOptions`), return `Skipped` early without calling the orchestrator.
2. **Accept the current design** — if calling the orchestrator with `false` is intentional (allowing the orchestrator to decide), remove or rewrite the scenario to reflect actual observable behaviour.

---

## GAP-007: config-applied-on-export — CLI has no fail-fast when migration-config.json already exists

**Status:** RESOLVED (2026-06-04) — The `@us1-write-idempotency` scenario was deleted: the CLI has NO access to the package filesystem (Principle VI, Separation of Planes — the CLI talks only to the control plane), so a pre-submission config-exists check is architecturally impossible. No production code change. The existing-file case is handled by the agent's resume semantics (overwrite if endpoints unchanged; reject with `InvalidOperationException` if changed) — documented in docs/configuration-reference.md.

- **gap-type:** `other`
- **family:** `config-applied-on-export`
- **file:** `features/export/config-in-package/config-applied-on-export.feature`
- **scenario:** CLI fails with a clear error when migration-config.json already exists (`@us1-write-idempotency`)
- **wiring state:** unwired
- **detail:** The CLI (`QueueCommand`) does not check whether `migration-config.json` exists in the package before job submission. The agent uses resume semantics: if the file exists and endpoints are unchanged it overwrites; if endpoints changed it throws `InvalidOperationException`. There is no CLI-level pre-submission validation that returns a non-zero exit code with an "already exists" message. The scenario intent is aspirational and conflicts with the agent's resume-compatible overwrite design.

### Resolution options

1. **Add CLI pre-submission check** — before calling `client.SubmitAsync`, check if `{packagePath}/.migration/migration-config.json` exists on the local filesystem and fail with a clear error (only applicable when package path is a local path, not a remote URI).
2. **Rewrite scenario to reflect actual behaviour** — instead of fail-fast, assert that the agent handles the existing file through resume semantics (overwrite if compatible, reject if endpoints changed).

---

## GAP-008: export-execution-metrics — OTel counter assertions require infrastructure setup

**Status:** RESOLVED (2026-06-04) — Export counters are assertable via an OpenTelemetry in-memory exporter scoped per test (`OpenTelemetry.Exporter.InMemory`). `ExportMetricsTests` builds a per-test `MeterProvider` (`AddMeter(WellKnownMeterNames.Agent)` + `AddInMemoryExporter`), records via `PlatformMetrics`, `ForceFlush`es, and asserts `platform.workitems.export.attempted`/`.retried` counters and the duration histogram — no full pipeline needed; counter values are isolated per test scope.

- **gap-type:** `other`
- **family:** `export-execution-metrics`
- **file:** `features/export/work-items/export-execution-metrics.feature`
- **scenarios:** All 3 (Successful export emits counters, Transient failures increment retried, Duration histogram records measurements)
- **wiring state:** unwired
- **detail:** The scenarios assert on OpenTelemetry metric counters (`migration.workitems.attempted`, `migration.workitems.retried`, `migration.workitem.duration.ms`). Verifying these requires either the OTel in-memory exporter or the full platform metrics pipeline. No existing unit tests in the codebase verify these counter values in isolation. Building reliable unit tests for OTel counter behavior requires substantial instrumentation harness that is out of scope for this migration cycle.

### Resolution options

1. **Add OTel in-memory exporter tests** — wire up `AddInMemoryExporter` in a test build and assert counter values after running the export orchestrator.
2. **Map to progress sink tests** — the `ExportAsync_EmitsProgressPerWorkItem` test already verifies that progress events are emitted per work item; extend it to capture metric values.

---

## GAP-009: export-payload-metrics — MetricSnapshot requires full export pipeline

**Status:** RESOLVED (2026-06-04) — Payload/complexity histograms (`platform.workitems.export.revisions.count`, `.fields.count`, `.payload.bytes`) are assertable via the same per-test in-memory `MeterProvider` in `ExportMetricsTests` — no full export pipeline required. Same infrastructure as GAP-008.

- **gap-type:** `other`
- **family:** `export-payload-metrics`
- **file:** `features/export/work-items/export-payload-metrics.feature`
- **scenarios:** Both (Payload histograms reflect complexity, MetricSnapshot shows batch mean values)
- **wiring state:** unwired
- **detail:** The scenarios assert on `MetricSnapshot` properties (`RevisionCountMean`, `FieldCountMean`, `PayloadBytesMean`) that are computed from OTel histograms across a full export run. No unit tests currently verify these aggregated histogram values. Same root cause as GAP-008.

### Resolution options

Same as GAP-008: add OTel in-memory exporter instrumentation to the export orchestrator tests.

---

## GAP-010: commands-execute-successfully — discovery inventory command does not exist

**File:** `features/cli/execute/commands-execute-successfully.feature`
**Scenario:** Discovery inventory command fails gracefully with invalid config
**Family:** `commands-execute-successfully`
**Wiring:** `unwired`
**Gap type:** `behaviour-conflict`
**Detected:** 2026-06-08
**Status:** OPEN

### Engineering detail

The feature scenario invokes `devopsmigration discovery inventory --config invalid-path.json`. The CLI binary (`src/DevOpsMigrationPlatform.CLI.Migration/Program.cs`) registers the following top-level commands: `prepare`, `queue`, `manage`, `controlplane`, `agent`, `config`. There is no `discovery` command or `discovery inventory` sub-command registered anywhere in the application.

Running the CLI with `["discovery", "inventory", "--config", "invalid-path.json"]` produces:

```
Unhandled exception. Spectre.Console.Cli.CommandParseException: Unknown command 'discovery'.
```

This is an unhandled exception, not a graceful failure. The feature specifies "no unhandled exceptions should occur" and a non-zero exit code with a clear error message — but the actual behaviour is an unhandled `CommandParseException` propagated to stderr as a raw stack trace.

The test `CliCommand_DiscoveryInventory_InvalidConfigPath_FailsGracefully` was built using the in-process `MigrationPlatformHost.CreateDefaultBuilder`, which does not run Spectre.Console.Cli. It builds the DI host only, so the invalid config path is silently ignored (the JSON file is `optional: true`) and the host exits with code 0. The test therefore gets exit code 0 and empty stderr — asserting non-zero exit fails.

To unblock conversion, either:
1. Add a `discovery inventory` command to the CLI (matching the feature intent), or
2. Rewrite the scenario to use an existing command (e.g. `queue --config invalid-path.json`) and verify graceful failure with that command.

---

## GAP-011: commands-execute-successfully — discovery inventory --help command does not exist

**File:** `features/cli/execute/commands-execute-successfully.feature`
**Scenario:** Help text displays correctly for all commands
**Family:** `commands-execute-successfully`
**Wiring:** `unwired`
**Gap type:** `behaviour-conflict`
**Detected:** 2026-06-08
**Status:** OPEN

### Engineering detail

The feature scenario invokes `devopsmigration discovery inventory --help`. The CLI does not have a `discovery inventory` command (see GAP-010). Running out-of-process via `CliRunner`, the CLI exits non-zero with `CommandParseException: Unknown command 'discovery'` on stderr instead of displaying help text and exiting with code 0.

The test `CliCommand_DiscoveryInventory_HelpFlag_DisplaysHelpAndExitsZero` uses `CliRunner.RunOutOfProcessAsync` and asserts exit code 0 and stdout containing `"inventory"` and `"--config"`. All assertions fail because the command does not exist.

To unblock conversion, either:
1. Add a `discovery inventory` command to the CLI, or
2. Rewrite the scenario to test `--help` on an existing command (e.g. `queue --help`) and assert relevant help-text keywords for that command.

---

## GAP-012: commands-execute-successfully — missing required parameters scenario maps to non-error in-process path

**File:** `features/cli/execute/commands-execute-successfully.feature`
**Scenario:** Commands handle missing required parameters gracefully
**Family:** `commands-execute-successfully`
**Wiring:** `unwired`
**Gap type:** `behaviour-conflict`
**Detected:** 2026-06-08
**Status:** OPEN

### Engineering detail

The feature scenario runs `devopsmigration discovery inventory` (no required parameters) and expects a non-zero exit code with a help suggestion. The command does not exist (see GAP-010), so the production CLI would throw `CommandParseException: Unknown command 'discovery'`.

The test `CliCommand_MissingRequiredParameters_ShowsErrorAndSuggestsHelp` uses the in-process `MigrationPlatformHost.CreateDefaultBuilder` path, which does not invoke Spectre.Console.Cli argument parsing. Running `["discovery", "inventory"]` through the host builder simply builds DI, finds no app entrypoint to invoke, and exits with code 0 with empty stderr. The test asserts non-zero exit code and help suggestion — both fail.

To unblock conversion, either:
1. Add a `discovery inventory` command with required parameters to the CLI, or
2. Rewrite the scenario to test an existing command invoked with missing required args (e.g. `queue` with no `--config`) and verify graceful argument-validation error behaviour through out-of-process invocation via `CliRunner`.

---

## GAP-013: system-test-local-execution — Valid env configuration scenario requires live ADO subprocess

**File:** `features/cli/inventory/system-test-local-execution.feature`
**Scenario:** `Developer runs system test with valid environment configuration`
**Family:** `system-test-local-execution`
**Wiring:** `unwired`
**Gap type:** `test-failure`
**Detected:** 2026-06-09
**Status:** OPEN

### Engineering detail

The mapped test `ValidEnvConfiguration_ExecutesSuccessfully` (file:
`tests/DevOpsMigrationPlatform.CLI.Migration.Tests/SystemTests/SystemTestLocalExecutionTests.cs:23`)
invokes `dotnet test` as a subprocess against
`tests/DevOpsMigrationPlatform.CLI.Migration.Tests` with filter
`TestCategory=SystemTest` and a 30-second timeout.

On 2026-06-09 the test run showed:
- Environment variables `AZDEVOPS_SYSTEM_TEST_ORG` and `AZDEVOPS_SYSTEM_TEST_PAT`
  appear to be set (the `SkipIfNotConfigured()` guard did not trigger).
- The subprocess started but was killed after 30 seconds (exit code -1).
- The `ShouldSucceed()` assertion then failed on exit code -1.

Root causes:
1. The 30-second timeout is insufficient for `dotnet test` to build and run a
   system test that makes live ADO network calls.
2. Scenario 1's full pass requires a live Azure DevOps organization accessible
   from the test machine, which is not guaranteed in the current environment.

To unblock conversion:
1. Increase the subprocess timeout (e.g., `TimeSpan.FromMinutes(5)`) and run in
   a dedicated system-test gate where `AZDEVOPS_SYSTEM_TEST_ORG` and
   `AZDEVOPS_SYSTEM_TEST_PAT` point to a real, accessible ADO organization.
2. Alternatively, narrow the scenario to assert only that `ValidateConnectivityAsync`
   succeeds with valid credentials (an in-process assertion) rather than running a
   full `dotnet test` subprocess — this avoids the network call and subprocess
   overhead while still covering the connectivity intent.

---

## GAP-014: tfs-export — Export validates TFS server URL before starting

**File:** `features/cli/export/tfs-export.feature`
**Scenario:** `Export validates TFS server URL before starting`
**Family:** `tfs-export`
**Wiring:** `unwired`
**Gap type:** `behaviour-conflict`
**Detected:** `2026-06-09`
**Status:** OPEN

### Engineering detail

The feature specifies that the CLI must emit a validation error when the TFS server URL is
not a valid HTTP or HTTPS URL (e.g. "not-a-url"). Actual production behaviour in
`QueueCommand.ExecuteAdoExportAsync` (lines 724-728) performs only an
`IsNullOrWhiteSpace(orgUrl)` check — no HTTP/HTTPS format validation exists. A non-empty
but non-HTTP/HTTPS URL such as "not-a-url" passes the guard and proceeds to the
control-plane submission, which fails for an unrelated reason.

The `AssertValidationErrorUrlRequired()` assertion in `TfsConfigValidationTests` would
be a false positive: it matches "http" in the control-plane connection error URL
(`http://localhost:<ephemeral>`), not in a URL validation message.

The test `TfsExport_InvalidServerUrl_ValidationErrorShown` in
`tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Cli/TfsExport/TfsConfigValidationTests.cs`
carries `[Ignore]` until this gap is resolved.

To unblock:
1. Add HTTP/HTTPS URL format validation to `QueueCommand.ExecuteAdoExportAsync` (or to
   the JSON schema's `Source.Url` field as `"format": "uri"`) before the package-URI check.
2. The error message must reference "HTTP" or "HTTPS" or "URL" so that
   `AssertValidationErrorUrlRequired()` matches on the correct message.
3. Remove the `[Ignore]` attribute and re-run the test to confirm.

---

## GAP-015: tfs-export — TFS export being unavailable produces a clear error before any export begins

**File:** `features/cli/export/tfs-export.feature`
**Scenario:** `TFS export being unavailable produces a clear error before any export begins`
**Family:** `tfs-export`
**Wiring:** `unwired`
**Gap type:** `behaviour-conflict`
**Detected:** `2026-06-09`
**Status:** OPEN

### Engineering detail

The feature specifies that when TFS export is unavailable, the CLI emits a clear error
before any export begins. The DSL's `WithTfsUnavailable()` builder method sets an internal
flag (`_tfsAvailable = false`) and the design intended to wire `ThrowingTfsJobServiceFactory`
into the DI container via `RunInProcessAsync`. However:

1. `RunInProcessAsync` only calls `MigrationPlatformHost.CreateDefaultBuilder(...).Build().StopAsync()`.
   This constructs and tears down the DI container but does not execute any `ICommand` handler;
   `QueueCommand.ExecuteInternalAsync` is never called. The service override has no effect.
2. The `ThrowingTfsJobServiceFactory` stub in `TfsExportTestDoubles.cs` is commented out
   because the test project does not reference `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel`.
3. `RunOutOfProcessAsync` launches a subprocess where no DI override is possible.
   The test would pass because the CLI prints "Exporting from..." before the control-plane
   submission fails, and `AssertTfsUnavailableErrorShown` matches "export" in "Exporting".
   This is a false positive.

The test `TfsExport_TfsUnavailable_ClearErrorBeforeStart` in
`tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Cli/TfsExport/TfsExportFaultHandlingTests.cs`
carries `[Ignore]` until this gap is resolved.

To unblock one of the following approaches is needed:
a. Add `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel` project reference to the test
   project, implement an in-process command execution path that runs `QueueCommand` directly
   (not just builds/stops the host), and uncomment `ThrowingTfsJobServiceFactory`.
b. OR add a CLI environment variable or config flag that causes `QueueCommand` to skip TFS
   service creation, producing the "TFS export unavailable" error — then the subprocess
   approach can work.

---

## GAP-016: tfs-export — Successful TFS export streams live progress to the terminal

**File:** `features/cli/export/tfs-export.feature`
**Scenario:** `Successful TFS export streams live progress to the terminal`
**Family:** `tfs-export`
**Wiring:** `unwired`
**Gap type:** `infrastructure`
**Detected:** `2026-06-09`
**Status:** OPEN

### Engineering detail

The scenario requires a live TFS server at the configured URL and a running control-plane
and TFS migration agent to complete an actual export. In a unit-test context (CI or local
without TFS access), the CLI connects to the control plane, submits the job successfully,
but no TFS agent processes it. The SSE follow-log stream times out or returns a job-failed
state, causing the CLI to exit with code 1.

The test `TfsExport_ValidConfig_LiveProgressDisplayed` in
`tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Cli/TfsExport/TfsExportProgressVisibilityTests.cs`
carries `[Ignore]` until a system-test TFS environment is available.

To unblock: run this test in a dedicated integration/system-test gate where
`AZDEVOPS_SYSTEM_TEST_ORG` (TFS collection URL) and `AZDEVOPS_SYSTEM_TEST_PAT` point to a
real, accessible TFS collection, and the full control-plane + TFS migration agent stack is
running.

---

## GAP-017: tfs-export — TFS export output is streamed to the operator in real time

**File:** `features/cli/export/tfs-export.feature`
**Scenario:** `TFS export output is streamed to the operator in real time`
**Family:** `tfs-export`
**Wiring:** `unwired`
**Gap type:** `validity-gate`
**Detected:** `2026-06-09`
**Status:** OPEN

### Engineering detail

The test `TfsExport_OutputStreamed_StdoutAndStderrDistinguished` uses two assertions:
- `AssertOutputLinesProduced()`: passes trivially because the CLI emits "Exporting from..."
  before the job submission fails, regardless of whether streaming actually worked.
- `AssertErrorOutputOnStderr()`: passes trivially when stderr is empty (the assertion body
  short-circuits on `string.IsNullOrWhiteSpace(StandardError)`).

Neither assertion proves real-time streaming behaviour or stderr/stdout channel distinction.
The test is a false positive under the validity-gate rule.

The test `TfsExport_OutputStreamed_StdoutAndStderrDistinguished` in
`tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Cli/TfsExport/TfsExportProgressVisibilityTests.cs`
carries `[Ignore]` until this gap is resolved.

To unblock:
1. Implement a streaming assertion that captures output lines with timestamps and verifies
   inter-line delay (proving incremental emission rather than buffered-at-end).
2. OR inject a fake progress event source that emits a known sequence of events and assert
   the exact lines appear in order on stdout.
3. `AssertErrorOutputOnStderr` needs a scenario where an error actually IS emitted (e.g.
   route a validation error to stderr) so the assertion is not trivially skipped.

