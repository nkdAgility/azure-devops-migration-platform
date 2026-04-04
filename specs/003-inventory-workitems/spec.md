# Feature Specification: Work Items Inventory Command

**Feature Branch**: `003-inventory-workitems`  
**Created**: 2026-04-04  
**Status**: Draft  
**Input**: User description: "Flesh out the inventory command for Work Items discovery supporting both AzureDevOps and TfsObjectModel with progressive timespan reduction to stay under the 20k work item query limit"

## Architecture References

The following documents were read before drafting this specification.

| Document | Status |
|---|---|
| `agents.md` | Confirmed accurate — binding entry point consulted first |
| `docs/cli.md` | **Discrepancy** — no `inventory` command listed; needs addition |
| `docs/source-types.md` | **Discrepancy** — only describes export; inventory mode not mentioned for either source type |
| `docs/tfs-exporter.md` | **Discrepancy** — only describes export agent; inventory capability via subprocess not yet specified |
| `docs/configuration.md` | **Discrepancy** — `source` section omits authentication fields; needs update to include auth shape |
| `docs/architecture.md` | Confirmed accurate — inventory is a read-only pre-flight operation; does not violate job-engine separation |
| `docs/modules.md` | Confirmed accurate — no inventory module type yet; this feature introduces `IInventoryService` as a separate concern |
| `.agents/guardrails/system-architecture.md` | Confirmed accurate — rules 16, 19 consulted; inventory is not migration logic; TFS still requires subprocess isolation |
| `.agents/guardrails/coding-standards.md` | Confirmed — Spectre.Console.Cli for CLI; no bare credential args |
| `.agents/guardrails/migration-rules.md` | Confirmed — inventory is not migration; no package, no checkpoint |

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Work Item Inventory via Azure DevOps (Priority: P1)

An operator planning a migration wants to know how many work items and revisions exist across every project in an Azure DevOps organisation before deciding whether to proceed. They run a single CLI command that connects to the organisation using a PAT, counts all work items and revisions across every project, and renders a live-updating table in the terminal. When complete, the results are saved to a CSV file.

**Why this priority**: This is the core deliverable — it gives operators the critical pre-migration size estimate without requiring any job submission or package creation. It is the primary use case that motivated the feature.

**Independent Test**: Can be fully tested by pointing the CLI at a real or mocked Azure DevOps organisation with a known number of projects and work items and verifying the counts match, the table updates progressively, and a CSV is produced. Delivers standalone value as an operator planning tool.

**Acceptance Scenarios**:

1. **Given** an Azure DevOps organisation contains three projects, **When** the operator runs `migrate inventory work-items --config migration.json`, **Then** a live-updating table appears in the terminal with one row per project, and each row's "Work Items", "Revisions", and "Updated" cells update as counting progresses.
2. **Given** a project has 45,000 work items, **When** the inventory command counts it, **Then** the platform issues multiple bounded queries rather than a single query, and all 45,000 work items are counted without the platform receiving an error from the query API.
3. **Given** counting is complete for all projects, **When** the command exits successfully, **Then** a file named `discovery-summary.csv` is written to the configured output location and the terminal confirms the file path.
4. **Given** a project has zero work items, **When** the inventory command counts it, **Then** the project row shows 0 work items, 0 revisions, and the counting is marked complete without error.
5. **Given** an invalid PAT is supplied, **When** the inventory command attempts to connect, **Then** the command exits immediately with a non-zero exit code and a clear authentication failure message.

---

### User Story 2 — Work Item Inventory via TFS Object Model (Priority: P2)

An operator with an on-premises Team Foundation Server wants to scope their migration before committing. They configure the source as `TeamFoundationServer` and run the same `migrate inventory work-items` command. The CLI transparently delegates to the TFS subprocess and streams the results back.

**Why this priority**: TFS support is a stated requirement alongside Azure DevOps. It is slightly lower priority because it depends on the subprocess isolation infrastructure already in place, but it must operate identically from the operator's perspective.

**Independent Test**: Can be fully tested against a real or simulated TFS collection by verifying that the same live-updating table and CSV output are produced, that no .NET 4.8 assembly is loaded in-process, and that the subprocess communicates discovery results via the established NDJSON protocol.

**Acceptance Scenarios**:

1. **Given** a source configured as `TeamFoundationServer`, **When** the operator runs `migrate inventory work-items`, **Then** the platform spawns the TFS subprocess via the existing `ExternalToolRunner` bridge and streams inventory progress back to the terminal.
2. **Given** a TFS project contains 60,000 work items created over 15 years, **When** the inventory command counts them, **Then** the platform queries in date-bounded windows and progressively narrows each window until every window returns fewer than 20,000 results.
3. **Given** a date window would return ≥ 20,000 work items, **When** the platform detects this, **Then** it halves the window size and retries without emitting a partial count from that window.
4. **Given** the TFS subprocess emits inventory progress events, **When** the .NET 10 host receives them, **Then** they are converted to the same live-table updates as the Azure DevOps path, and the operator sees no difference in presentation.

---

### User Story 3 — Progressive Query Narrowing for Large Collections (Priority: P2)

A source collection may have projects containing hundreds of thousands of work items spanning many years. The inventory command must count all work items reliably, regardless of total size, by automatically dividing the query space into windows that stay below the 20,000-item per-query limit.

**Why this priority**: Without this behaviour, large enterprise collections return query errors and the inventory fails. This is an enabling requirement for both source types.

**Independent Test**: Can be fully tested using a service fake/stub that simulates query results returning exactly the limit and verifying that the platform bisects the window and retries before recording a count.

**Acceptance Scenarios**:

1. **Given** a query window would return exactly 20,000 items (at the limit), **When** the platform executes the query, **Then** it treats the result as potentially truncated, halves the time window, and retries.
2. **Given** a query returns fewer than 20,000 items, **When** the platform records the result, **Then** it moves the window backward in time by the current window size and proceeds with the next query.
3. **Given** the window has been narrowed to less than 30 days, **When** a query returns under the limit, **Then** the platform gradually increases the window size again to minimise total query count.
4. **Given** a WIQL query fails with a server error during counting, **When** the error is received, **Then** the platform halves the current window size and retries that window rather than aborting.

---

### Edge Cases

- What happens when a project is deleted between the project list call and the count calls? The platform skips the missing project and records it as an error row in the table and the CSV.
- What happens when the connection is lost mid-count? The command exits with a non-zero code and reports the projects that were fully counted versus those that were not.
- What happens if the TFS subprocess terminates unexpectedly? `ExternalToolRunner` captures the non-zero exit code and the CLI reports the failure without crashing.
- What happens when the initial window size already yields zero results? The platform terminates the backward scan for that project and reports the total counted so far.

---

## Configuration Schema

The inventory command reads a single config file. The file uses a `Source` / `Target` shape — inspired by the established `azure-devops-migration-tools` convention but not schema-compatible with it. Only `Source` is read for inventory; `Target` is ignored if present.

### Azure DevOps Services example

```json
{
  "configVersion": "1.0",
  "source": {
    "type": "AzureDevOpsServices",
    "orgOrCollection": "https://dev.azure.com/myorg",
    "project": null,
    "apiVersion": "7.1",
    "authentication": {
      "type": "Pat",
      "accessToken": "",
      "accessTokenVariable": "MIGRATION_SOURCE_PAT"
    }
  }
}
```

`project` is optional. When omitted or `null`, inventory counts all projects in the organisation. When provided, only that single project is inventoried.

`accessToken` is used directly if non-empty. If empty, the value is read from the environment variable named in `accessTokenVariable`. This avoids secrets in checked-in files.

### TFS / on-premises example

```json
{
  "configVersion": "1.0",
  "source": {
    "type": "TeamFoundationServer",
    "orgOrCollection": "http://tfs.internal:8080/tfs/DefaultCollection",
    "project": null,
    "apiVersion": "15.0",
    "authentication": {
      "type": "Windows"
    }
  }
}
```

For `Windows` authentication, no token is required; the current Windows identity is used automatically.

### Source authentication types

| `type` | Description | Active for source type |
|---|---|---|
| `Pat` | Personal Access Token. Uses `accessToken` or falls back to `accessTokenVariable`. | `AzureDevOpsServices` |
| `Windows` | Windows Integrated Auth (Kerberos / NTLM). No token needed. | `TeamFoundationServer` |

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The CLI MUST expose a `migrate inventory work-items` command that accepts a `--config <path>` option pointing to an existing config file. This is the only required argument.
- **FR-002**: ALL connection and authentication configuration MUST reside in the config file under the `source` section. The command MUST NOT accept organisation URL, collection URL, PAT, or any other connection detail as bare CLI arguments. The `source` section MUST contain `type`, `orgOrCollection`, optionally `project`, `apiVersion`, and an `authentication` block as defined in the Configuration Schema above.
- **FR-003**: When `source.type` is `AzureDevOpsServices`, the platform MUST count work items directly from the Azure DevOps REST API using the authenticated call without spawning a subprocess.
- **FR-004**: When `source.type` is `TeamFoundationServer`, the platform MUST delegate all counting to the `DevOpsMigrationPlatform.CLI.TfsMigration` subprocess via `ExternalToolRunner`. No TFS Object Model assembly MUST be loaded in the .NET 10 process.
- **FR-005**: The counting strategy MUST issue queries bounded by a time window (date range) and MUST detect when a query returns at or above the 20,000-item limit; in that case it MUST halve the window and retry before recording a count for that window.
- **FR-006**: When a window returns fewer results than the limit, the platform MUST advance the window backward in time and continue until a window returns zero results.
- **FR-007**: The platform MUST stream partial count totals progressively — each completed query window MUST yield an incremental count update without waiting for the full project to complete.
- **FR-008**: The CLI MUST render a live-updating terminal table showing, per project: project name, current work item count, current revision count, placeholder values for Repos and Pipelines (blank or zero), and the UTC time of the last update.
- **FR-009**: When all projects have been counted, the command MUST write a `discovery-summary.csv` file to the location specified by the config (defaulting to the current working directory) containing one row per project with: name, work item count, revision count, repo count, pipeline count.
- **FR-010**: The command MUST exit with code 0 on success and a non-zero code on any unrecoverable error (authentication failure, subprocess non-zero exit, unhandled exception).
- **FR-011**: The inventory command MUST NOT submit a job to the control plane and MUST NOT create a migration package or write any checkpoints. It is a read-only pre-flight operation.
- **FR-012**: The TFS subprocess MUST emit inventory progress as NDJSON lines on stdout using the same `IProgressSink`/`StdoutProgressSink` protocol already used for export progress; the .NET 10 host converts these lines into table updates.

### Key Entities

- **InventorySummary**: A per-project record of counted work items, revisions, repos, pipelines, completion flags, and the UTC timestamp of the last count update. Written to CSV on completion.
- **QueryWindow**: A bounded time range used for a single inventory query. Defined by a start date, end date, and window size. The window size is halved on overflow and gradually expanded on success.
- **InventoryProgressEvent**: A progress event emitted by `IProgressSink` during inventory. Contains project name, current totals, window metadata, and a flag indicating whether counting is complete for that project.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can complete a full work item inventory for an organisation with 10 projects and up to 500,000 total work items across all projects without any query returning an error due to the 20,000-item limit.
- **SC-002**: The live terminal table receives its first update within 5 seconds of the command starting.
- **SC-003**: On completion, the `discovery-summary.csv` file contains exactly one row per project with accurate totals matching the final table values.
- **SC-004**: A project with 100,000 work items is counted fully without the operator observing any failure or gap; the displayed total matches the actual count.
- **SC-005**: The TFS and Azure DevOps paths produce identical terminal output and CSV format, making the source type transparent to the operator.
- **SC-006**: The platform retries a failing query window at least 3 times with progressively smaller windows before reporting that project as failed.

---

## Assumptions

- Authentication for Azure DevOps uses a Personal Access Token with `vso.work_read` scope. The PAT is read from `source.authentication.accessToken` (if non-empty) or from the environment variable named in `source.authentication.accessTokenVariable`. Service principal authentication is out of scope for this feature.
- TFS on-premises uses Windows Integrated Auth; no token field is required in the config.
- Credentials MUST NOT be passed as CLI arguments (enforced by the config-only design).
- The initial query window size is 120 days (matching the POC default); this may be surfaced as an optional `source.inventory.initialWindowDays` config field in a future revision.
- The maximum items per query threshold is 20,000 (the Azure DevOps and TFS WIQL hard limit).
- Revision counting for Azure DevOps is performed by fetching the `System.Rev` field for each work item returned by the identity query; this is a separate API call and is counted after all work item IDs are enumerated.
- `project` in the config is optional for inventory. If omitted or null, all projects in the org/collection are inventoried. If present, only that project is counted.
- Repos and Pipelines columns are displayed but left at zero for this feature; they will be populated by a future inventory scope extension.
- The TFS inventory subprocess reuses the existing `DevOpsMigrationPlatform.CLI.TfsMigration` binary with a new `inventory` subcommand — no new binary is introduced.
- `Target` section within the config file is silently ignored by the inventory command.
- Architecture docs consulted: `docs/cli.md`, `docs/configuration.md`, `docs/source-types.md`, `docs/tfs-exporter.md`, `docs/architecture.md`. All gaps are filed in `discrepancies.md`.
