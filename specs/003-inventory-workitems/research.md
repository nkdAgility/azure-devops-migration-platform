# Research: Work Items Inventory Command

**Phase 0 output for feature 003-inventory-workitems**

All NEEDS CLARIFICATION items from the Technical Context are resolved here.

---

## 1. Token resolution for `$ENV:VARNAME`

**Decision:** Implement a `TokenResolver` utility class (in `DevOpsMigrationPlatform.Abstractions`, compiled for both `net481` and `net10.0`) that takes a raw string and:
1. Returns `null`/empty unchanged.
2. If the string starts with `$ENV:`, reads the named environment variable via `Environment.GetEnvironmentVariable`. Throws `InvalidOperationException` if the variable is unset or empty.
3. Otherwise returns the literal value.

**Rationale:** The `$ENV:` prefix is explicit and unambiguous. It does not require IConfiguration and works at any layer — including inside `organisations` list entries where IConfiguration `__`-key paths are structurally impossible. The TFS subprocess can use the same `TokenResolver` because it lives in multi-targeted `Abstractions`.

**Alternatives considered:**
- Two fields (`accessToken` + `accessTokenVariable`): rejected — increases config surface area for marginal readability gain.
- IConfiguration-only: rejected — cannot reach indexed list items.

---

## 2. `organisations` config loading strategy

**Decision:** Define a new `InventoryOptions` sealed options class in `DevOpsMigrationPlatform.Abstractions` bound to the config root. It has two nullable properties: `Source` (reusing `MigrationEndpointOptions` extended with an `Authentication` block) and `Organisations` (`List<OrganisationEntry>`). Validation runs at startup before any API call.

**Rationale:** Reusing `MigrationEndpointOptions` for the `source` path avoids a new model. The `Organisations` list is a separate property rather than a derived view to make the two-mode distinction explicit and enforceable by the validator.

**Alternatives considered:**
- Deriving from `MigrationOptions`: rejected — `MigrationOptions` carries `Mode`, `Artefacts`, `Modules`, and `Policies` that are meaningless for a read-only inventory command. Inheritance here would create confusion about which fields matter.
- A separate `inventory.json` file: rejected — operators asked for a single config file pattern.

---

## 3. Azure DevOps work item counting strategy

**Decision:** Extract the date-window algorithm into a shared `WorkItemQueryWindowStrategy` class in `DevOpsMigrationPlatform.Infrastructure.AzureDevOps`. Both `CatalogService` (export) and `AzureDevOpsInventoryService` (inventory) use it. The two strategies are **complementary, not alternatives**:

- **Date-window** (`WorkItemQueryWindowStrategy`): controls the time-bounded WIQL query that keeps each individual query under the 20,000-item limit. Algorithm:
  - Start window: `initialWindowDays` (default 120) ending at `DateTime.UtcNow`.
  - WIQL: `SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{project}' AND [System.CreatedDate] >= '{start}' AND [System.CreatedDate] < '{end}'`
  - If result count ≥ 20,000: halve the window and retry the same end date.
  - If result count == 0: stop scanning backward (no earlier work items).
  - If result count < 20,000: yield the IDs, advance the window end to the current window start, repeat.
  - After a successful window narrower than 30 days: grow by 1 day to reduce total query count over time.
- **ID-cursor paging** (`CatalogService`): pages through the IDs returned by a single window using `ORDER BY [System.Id]`. This remains unchanged — it operates *within* a window, not across windows.

`CatalogService` is not replaced; it gains a dependency on `WorkItemQueryWindowStrategy` to bound each page query. `AzureDevOpsInventoryService` uses `WorkItemQueryWindowStrategy` directly and does not page (it counts, not fetches).

TFS uses the parallel `WorkItemStoreExtensions.QueryCountAllByDateChunk` (already in the POC's net481 layer) — same algorithm, different runtime.

**Rationale:** Date-window and ID-cursor serve different concerns. Extracting `WorkItemQueryWindowStrategy` means ADO export, ADO inventory, and future ADO modules all share one implementation of the windowing algorithm instead of each re-implementing it. TFS mirrors this with its existing `WorkItemStoreExtensions`.

**Alternatives considered:**
- Replace ID-cursor with date-window entirely in `CatalogService`: rejected — they are complementary; replacing paging with windowing would require a full rewrite of the export fetch path, which is out of scope.
- Keep date-window logic inside `AzureDevOpsInventoryService` only: rejected — export and future modules also need windowed queries; a private implementation creates duplication.

---

## 4. TFS inventory subprocess approach

**Decision:** Add an `inventory` subcommand to the existing `DevOpsMigrationPlatform.CLI.TfsMigration` Spectre.Console app that:
- Accepts `--collection`, `--project` (optional), `--all-projects` (flag), and reads auth from stdin JSON (same `TfsExportRequest`-style pattern).
- Uses `WorkItemStore.QueryCount(wiql)` per window (already available in `WorkItemStoreExtensions.QueryCountAllByDateChunk`).
- Emits `InventoryProgressEvent` records as NDJSON via `StdoutProgressSink`.

The .NET 10 `ExternalToolRunner` is called with the `inventory` subcommand. No new binary. No new subprocess adapter interface.

**Rationale:** The entire process isolation infrastructure already exists. The subprocess already emits NDJSON and the host already converts it. Adding an `inventory` subcommand is the smallest consistent extension.

**Alternatives considered:**
- New binary: rejected — unnecessary, violates the coding standard that says no new subprocess binary.
- Calling TFS OM from .NET 10 directly: rejected — categorically forbidden by guardrail rule 19.

---

## 5. `--all-projects` CLI flag placement

**Decision:** Declare `--all-projects` as a `[CommandOption]` on `InventoryCommand.Settings` (type `bool`). It is only consulted for Mode 1 (`source`-based) configs. In Mode 2 it is silently ignored. Spectre.Console renders it in help text.

**Rationale:** It is a per-invocation scope override, not connection config — the one legitimate use of a CLI flag per the spec clarification.

**Alternatives considered:**
- `source.project = null` implicitly enumerating all: rejected — too easy to accidentally inventory a large org.
- Separate `--project` offset flag: rejected — project is already in the config; a separate flag creates a second source of truth.

---

## 6. CSV output path

**Decision:** Add an `--output <path>` CLI flag to `InventoryCommand.Settings`. When absent, defaults to the current working directory (`./discovery-summary.csv`). The path is not stored in the config file.

**Rationale:** Output path is a per-invocation concern, not a connection or authentication detail, so it is a legitimate CLI flag under the all-config-in-file rule. The rule only prohibits bare org URLs, projects, and PATs as CLI args. Allowing `--output` mirrors the convention used by standard CLI tools and avoids polluting the config file with a machine-local path.

**Alternatives considered:**
- Config-driven output path: rejected — a local filesystem path is an operator invocation concern, not migration configuration.
- Always CWD with no flag: rejected — operators running multiple inventory passes simultaneously need to control output location.

---

## 7. Revision counting for Azure DevOps

**Decision:** After each successful query window yields a set of work item IDs, fetch `System.Rev` for each ID in batches of 200 using `WorkItemTrackingHttpClient.GetWorkItemsAsync`. Sum all revision values to get the total revision count for that window. This is additive — each window's revision total is accumulated into `InventorySummary.RevisionsCount`.

**Rationale:** There is no direct WIQL aggregate for revision count. The field fetch is the cheapest available mechanism. Batch size 200 matches the existing `CatalogService` pattern.

**Alternatives considered:**
- Separate revision query per work item: rejected — O(n) round trips.
- Skip revision counting: rejected — revision count is a required output column and a key migration scoping metric.
