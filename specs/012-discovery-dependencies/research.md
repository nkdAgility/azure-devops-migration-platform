# Research: Discovery Dependency Analysis

**Feature**: `012-discovery-dependencies`  
**Phase**: 0 — Research  
**Status**: Complete — all NEEDS CLARIFICATION resolved

---

## 1. ADO REST API: Work Item Link Enumeration & Classification

**Decision**: Use `WorkItemTrackingHttpClient.GetWorkItemsBatchAsync` with `WorkItemExpand.Relations` to retrieve all links per work item. Batch size: 200 (ADO batch API ceiling).

**Resolved Questions**:
- **Link URL format**: For work-item-to-work-item relations the ADO REST API returns `"url": "https://dev.azure.com/{org}/_apis/wit/workItems/{id}"`. The project name is **not** in the URL. To determine the target project, a secondary batch-GET is required requesting only the `System.TeamProject` field.
- **CrossOrganisation detection**: Compare the URL host of each relation's `url` against the source organisation's configured host (case-insensitive). If they differ → `CrossOrganisation`. If same host → candidate for `CrossProject` or `SameProject` resolution via secondary GET.
- **LinkType display name**: Use `WorkItemRelation.Attributes["name"]` (e.g. `"Child"`, `"Parent"`, `"Related"`) rather than the raw `rel` value (e.g. `"System.LinkTypes.Hierarchy-Forward"`).
- **Same-project filtering**: After resolving target project via secondary batch-GET, if `System.TeamProject` matches the source project → silently discard. Never emit `SameProject` scope.
- **TargetStatus resolution**: For both `CrossProject` and `CrossOrganisation` links, `TargetStatus` is set during the secondary GET: HTTP 200 → `Reachable`, HTTP 404 → `Deleted`, HTTP 403/401 → `AccessDenied`, any network/unexpected error → `Unknown`. For `CrossOrganisation` links the secondary GET is attempted against the remote URL but without the source PAT (unauthenticated). Success still yields `Reachable`; failure yields the appropriate `TargetStatus` without failing the command.

**Rationale**: Batch-GET is the only approach that scales. Individual GETs for each link would hit ADO rate limits in large projects. The 200-item batch keeps individual HTTP payloads small and minimises concurrent requests.

**Alternatives considered**:
- WIQL join query to retrieve linked IDs with project names in one query: rejected — WIQL join is limited to a fixed depth and does not return the cross-org case.
- TeamProject lookup via `WorkItemTrackingHttpClient.GetWorkItemAsync` (single-item GET): rejected — serial I/O; O(links) round trips vs O(links/200).

---

## 2. WIQL Filter Validation

**Decision**: Pass the `--wiql` expression directly to `WorkItemTrackingHttpClient.QueryByWiqlAsync`. Rely on ADO server-side validation. If the server returns a non-success HTTP status (typically 400), catch `VssServiceResponseException` and surface the API's inner `Message` as CLI error code 1. No client-side parsing.

**Rationale**: Client-side WIQL parsing would require embedding a WIQL grammar. ADO's server error message is accurate, stable, and human-readable. Spec requirement (FR-011) says invalid WIQL must exit with code 1 and a human-readable message — server-side errors satisfy this.

**Alternatives considered**:
- Regex pre-validation before sending: rejected — subset of WIQL only, false positives/negatives.
- WIQL grammar library: rejected — no maintained .NET library; over-engineering for a discovery command.

---

## 3. TFS Subprocess Delegation

**Decision**: Add a new `TfsDependencyProcessAdapter` in `DevOpsMigrationPlatform.CLI.Migration`. It follows the exact same pattern as `TfsExporterProcessAdapter`: uses `IExternalToolRunner` to spawn `tfsmigration.exe` with a `dependencies` subcommand. Credentials are passed via stdin JSON; the subprocess emits `DependencyProgressEvent` NDJSON on stdout.

The TFS subprocess (`DevOpsMigrationPlatform.CLI.TfsMigration`) receives a new `dependencies` subcommand that uses TFS Object Model `WorkItemStore` to query all (or WIQL-filtered) work items, inspect their links, and emit classification records.

**TFS link classification**: TFS OM `WorkItem.WorkItemLinks` has `WorkItemLink.LinkedWorkItemId` and `WorkItemLink.LinkTypeEnd.ImmutableName`. The target URL is `{collectionUrl}/_apis/wit/workItems/{id}` (same collection) or a remote link with full URL. Cross-collection is any link where the `WorkItemLink.BaseWorkItem.Store.TeamProjectCollection.Uri` prefix doesn't match the source collection URI.

**Rationale**: The `TfsExporterProcessAdapter` pattern is already established and tested. Re-using `IExternalToolRunner` and the NDJSON protocol avoids creating new IPC machinery.

**Alternatives considered**:
- Sharing TFS link analysis code via the multi-targeted `Abstractions` layer: rejected — TFS OM assemblies are net481-only and must stay confined to `CLI.TfsMigration`.
- Direct TCP pipe: rejected — process bridge protocol is already specified; unnecessary complexity.

---

## 4. Simulated Source Support

**Decision**: Implement `SimulatedDependencyAnalysisService` in `DevOpsMigrationPlatform.Infrastructure`. It uses the `Simulated` organisation entry's `seed` value as a `Random` seed to produce deterministic `DependencyProgressEvent` records. Generates synthetic `DependencyRecord` items targeting both fake `CrossProject` and `CrossOrganisation` combinations. Proportions controlled by entry fields (or hardcoded defaults: 70% CrossProject, 30% CrossOrganisation).

**Rationale**: The simulated source is essential for CI testing without live ADO credentials (SC-001 + spec assumption). Using a seeded `Random` guarantees determinism (Principle VII).

**Alternatives considered**:
- Static fixture file: rejected — reduces flexibility; makes it hard to vary synthetic record counts.
- Generator based on work item count alone: accepted as default if no seed/count fields present on the org entry.

---

## 5. Concurrency Control

**Decision**: Add `MaxConcurrency` property (type `int`, default `4`) to `DiscoveryOptions`. `AzureDevOpsDependencyAnalysisService` uses `SemaphoreSlim(maxConcurrency)` to bound concurrent batch API calls. This matches FR-012.

**Rationale**: `DiscoveryOptions` already governs all discovery behaviour. Adding `MaxConcurrency` here keeps the concurrency control alongside the org/auth config rather than splitting it into a command-level flag.

**Alternatives considered**:
- Per-org `MaxConcurrency` on `OrganisationEntry`: deferred — cross-org concurrency is not in scope; a single global value keeps the model simple for v1.
- CLI flag `--max-concurrency`: rejected — connection-related settings belong in the config file per FR-002's principle (no bare credentials or rate-limit tuning as CLI args).

---

## 6. CSV Output Strategy

**Decision**: Write CSV rows incrementally using `StreamWriter` (auto-flush disabled, manual flush every 500 rows). The file is created before any API calls so a partial run always yields a partial but readable CSV. Header row is always written, even when zero records are found (FR-008).

**Rationale**: Streaming output is necessary for memory safety (SC-003). If the command is interrupted mid-run, the CSV contains all rows written up to that point, giving the operator partial data rather than nothing.

**Alternatives considered**:
- Collect all records in memory then write: rejected — violates SC-003 (50 k items × 10 links = 500 k rows in memory).
- Use a CSV serialisation library (CsvHelper): deferred — the existing `InventoryCommand.WriteCsv` uses a hand-rolled writer; follow the same pattern for consistency.

---

## 7. Default Output File Path

**Decision**: Default output path is `discovery-dependencies.csv` in the current working directory, overridable with `--output <path>`. If `--output` points to a file that already exists, overwrite it and print a warning (spec edge case).

**Rationale**: Parallel to `discovery inventory` which defaults to `discovery-summary.csv`. Storing in CWD is consistent and discoverable.

**Alternatives considered**:
- Output to `./output/` subdirectory: rejected — CWD default aligns with `InventoryCommand` pattern; the `InventoryCommand` writes to `./output/discovery-summary.csv` but spec FR-005 explicitly says CWD for dependencies.

---

## 8. Progress Events & Streaming Interface

**Decision**: `IDependencyDiscoveryService.DiscoverDependenciesAsync` returns `IAsyncEnumerable<DependencyProgressEvent>`. Each event carries either:
- A `DependencyRecord` (a single external link row ready for CSV), OR
- A progress heartbeat (work items analysed count, for console display).

The `DependencyCommand` iterates the async enumerable, writes each `DependencyRecord` to CSV immediately, and updates the live Spectre.Console progress panel from heartbeat events.

**Rationale**: `IAsyncEnumerable` is the established streaming pattern in this codebase (see `IInventoryService`, `IWorkItemDiscoveryService`). It enables back-pressure and lazy evaluation.

**Alternatives considered**:
- Separate channel for records vs heartbeats: rejected — single `IAsyncEnumerable` with a discriminated union (via a base `DependencyProgressEvent` with subtypes) is consistent with `InventoryProgressEvent`.
- Callback/delegate pattern: rejected — async enumerable is more composable and testable.

---

## 9. `DependencyProgressEvent` Discriminated Union Shape

**Decision**: `DependencyProgressEvent` is an abstract record in `Abstractions` with two concrete derived types:
- `DependencyFoundEvent`: carries a `DependencyRecord` to write to CSV.
- `DependencyHeartbeatEvent`: carries `WorkItemsAnalysed`, `ExternalLinksFound`, `CrossProjectCount`, `CrossOrgCount`.

The `DependencyCommand` uses pattern matching on the union.

**Rationale**: Mirrors how `InventoryProgressEvent` carries both partial and complete state in a single type. Using a discriminated union avoids having two separate async enumerables.

---

## 10. Service Layer Boundaries

| Layer | New Type | Responsibility |
|-------|----------|---------------|
| `Abstractions` | `IDependencyDiscoveryService` | Org-level orchestration interface |
| `Abstractions` | `IWorkItemLinkAnalysisService` | Per-org link analysis interface |
| `Infrastructure` | `DependencyDiscoveryService` | Iterates orgs, dispatches to per-org analyzers |
| `Infrastructure` | `SimulatedDependencyAnalysisService` | Fake records for Simulated org entries |
| `Infrastructure.AzureDevOps` | `AzureDevOpsDependencyAnalysisService` | ADO REST implementation |
| `CLI.Migration` | `TfsDependencyProcessAdapter` | TFS subprocess delegation |
| `CLI.Migration` | `DependencyCommand` | CLI entry point; renders table; writes CSV |

DI registration lives in a new `AddAzureDevOpsDependencyAnalysis(IConfiguration)` extension in `Infrastructure.AzureDevOps`.

---

## 11. Existing Types Modified

| Type | Project | Change |
|------|---------|--------|
| `DiscoveryOptions` | `Abstractions` | Add `public int MaxConcurrency { get; set; } = 4;` |
| `Program.cs` | `CLI.Migration` | Register `DependencyCommand` in `discovery` branch |
| `.vscode/launch.json` | — | Add `discovery dependencies` launch entries |
| `.agents/context/cli-commands.md` | — | Add `discovery dependencies` row and examples (doc task for implement) |
| `docs/cli.md` | — | Add `discovery dependencies` narrative (doc task for implement) |
| `docs/source-types.md` | — | Add Dependency Analysis section per source type (doc task for implement) |
