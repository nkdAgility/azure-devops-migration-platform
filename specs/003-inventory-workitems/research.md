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

**Decision:** Replace the current `CatalogService.CountAllWorkItemsAsync` inner loop (which batches by ID cursor) with a **date-window strategy** matching the POC pattern:
- Start window: `initialWindowDays` (default 120) ending at `DateTime.UtcNow`.
- Issue `SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = '{project}' AND [System.CreatedDate] >= '{start:yyyy-MM-dd}' AND [System.CreatedDate] < '{end:yyyy-MM-dd}'` via WIQL.
- If result count ≥ 20,000: halve the window, retry the same end date.
- If result count == 0: stop scanning (no further work items before this date).
- If result count < 20,000: record the count, optionally fetch `System.Rev` for each ID in batches of 200, advance the window end to the current window start, repeat.
- After a successful window, if window was < 30 days: grow by 1 day (up to max) to reduce total query count.

**Rationale:** The ID-cursor approach (current `CatalogService`) cannot handle the 20k limit because a single project could have >20k work items per ID range with no way to sub-divide. The date-window approach is the established pattern from the POC (`WorkItemStoreExtensions`) and the automation-tools PowerShell. It is also what the TFS subprocess already uses, making the two paths behaviorally consistent.

**Alternatives considered:**
- ID-cursor paging (current): rejected — a batch of 20k IDs is the *entire query result*, not a page; you cannot ask for the next page.
- Tag-based or area-path-based sub-division: rejected — fragile, requires knowledge of the project's taxonomy.

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

**Decision:** Default CSV path is the current working directory (`./discovery-summary.csv`). No `--out` flag is added to this implementation; the path is not configurable in this feature iteration.

**Rationale:** The existing `InventoryCommand` already has an `--out` flag that violates the all-config-in-file rule. That flag will be removed as part of this rework. A configurable output path is a future enhancement.

**Alternatives considered:**
- Config-driven output path: deferred to a future feature; not needed for MVP.
- Keeping `--out`: rejected — violates the no-connection-details-as-CLI-args principle.

---

## 7. Revision counting for Azure DevOps

**Decision:** After each successful query window yields a set of work item IDs, fetch `System.Rev` for each ID in batches of 200 using `WorkItemTrackingHttpClient.GetWorkItemsAsync`. Sum all revision values to get the total revision count for that window. This is additive — each window's revision total is accumulated into `InventorySummary.RevisionsCount`.

**Rationale:** There is no direct WIQL aggregate for revision count. The field fetch is the cheapest available mechanism. Batch size 200 matches the existing `CatalogService` pattern.

**Alternatives considered:**
- Separate revision query per work item: rejected — O(n) round trips.
- Skip revision counting: rejected — revision count is a required output column and a key migration scoping metric.
