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

## Clarifications

### Session 2026-04-04

- Q: Should `projects` in an `organisations` entry be optional, with absent or empty meaning all projects? → A: Yes — absent or empty enumerates all projects in that org; a non-empty list filters to only those named.
- Q: If a config has both `organisations` and `source`, which wins? → A: Validation error — command refuses and exits with a clear explanation of the conflict.
- Q: Should `enabled` in an `organisations` entry be required or optional with default `true`? → A: Optional, default `true` — only write `"enabled": false` to suppress an entry.
- Q: When using `source`-based config with `source.project` null, should inventory enumerate all projects or require a CLI flag? → A: Require an explicit `--all-projects` CLI flag; null project without the flag is a validation error.
- Q: How should `accessToken` reference an environment variable — two fields, a prefix convention, or env-var only? → A: Single `accessToken` field; if the value starts with `$ENV:VARNAME` the platform resolves that env var at the business logic layer. For `source`/`target` blocks the existing IConfiguration `__`-separator override (e.g. `MigrationTools__Source__Authentication__AccessToken`) also applies and takes highest precedence. `$ENV:` is the only mechanism available for `organisations` list entries.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Work Item Inventory via Azure DevOps (Priority: P1)

An operator planning a migration wants to know how many work items and revisions exist across every project in an Azure DevOps organisation before deciding whether to proceed. They run a single CLI command that connects to the organisation using a PAT, counts all work items and revisions across every project, and renders a live-updating table in the terminal. When complete, the results are saved to a CSV file.

**Why this priority**: This is the core deliverable — it gives operators the critical pre-migration size estimate without requiring any job submission or package creation. It is the primary use case that motivated the feature.

**Independent Test**: Can be fully tested by pointing the CLI at a real or mocked Azure DevOps organisation with a known number of projects and work items and verifying the counts match, the table updates progressively, and a CSV is produced. Delivers standalone value as an operator planning tool.

**Acceptance Scenarios**:

1. **Given** an `organisations`-mode config listing three projects across two orgs, **When** the operator runs `migrate inventory work-items --config tooling.json`, **Then** a live-updating table appears in the terminal with one row per project, and each row's "Work Items", "Revisions", and "Updated" cells update as counting progresses.
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

### User Story 4 — Multi-Org Tooling Roster Inventory (Priority: P3)

An operator responsible for dozens of Azure DevOps organisations in an enterprise wants a single inventory run that spans all of them. They maintain a dedicated tooling config file listing each org, with per-org PATs stored in environment variables. They run one command to inventory all enabled orgs and receive a single consolidated CSV.

**Why this priority**: This is the multi-org scenario from the `azure-devops-automation-tools` pattern. It builds directly on the `organisations` config mode. Lower priority because it composes the same per-org counting logic already delivered by P1/P2; the new work is only in the config loading and fan-out loop.

**Independent Test**: Can be fully tested with a fake that simulates two enabled orgs and one disabled org, verifying the table contains rows for both enabled orgs, the disabled org is skipped, and the CSV contains exactly the enabled orgs.

**Acceptance Scenarios**:

1. **Given** a config with three `organisations` entries — two enabled, one disabled — **When** the operator runs `migrate inventory work-items --config roster.json`, **Then** the table contains rows for the two enabled orgs' projects and the disabled org is silently skipped.
2. **Given** two organisations each with three projects, **When** the inventory command runs, **Then** all six projects appear in the output table and the CSV, grouped with their organisation name visible.
3. **Given** an `organisations` entry with an empty `projects` array, **When** the inventory command processes that entry, **Then** all projects in that org are enumerated and counted.
4. **Given** an `organisations` entry with `projects: ["Alpha", "Beta"]`, **When** the inventory command processes that entry, **Then** only "Alpha" and "Beta" are counted; other projects in that org are silently skipped.
5. **Given** a config containing both `organisations` and `source`, **When** the operator runs the inventory command, **Then** the command exits immediately with a non-zero code and an error message explaining that `organisations` and `source` are mutually exclusive.

---

### Edge Cases

- What happens when a project is deleted between the project list call and the count calls? The platform skips the missing project and records it as an error row in the table and the CSV.
- What happens when the connection is lost mid-count? The command exits with a non-zero code and reports the projects that were fully counted versus those that were not.
- What happens if the TFS subprocess terminates unexpectedly? `ExternalToolRunner` captures the non-zero exit code and the CLI reports the failure without crashing.
- What happens when the initial window size already yields zero results? The platform terminates the backward scan for that project and reports the total counted so far.

---

## Configuration Schema

All connection and authentication configuration lives in the config file. The inventory command supports two **mutually exclusive** config modes. Having both in the same file is a validation error.

---

### Mode 1 — Migration Config (`source`-based)

Reuses the existing migration config shape. Only `source` is read; `target` is silently ignored.

The `--all-projects` CLI flag controls scope:

| `source.project` | `--all-projects` flag | Result |
|---|---|---|
| Set to a project name | Absent | Inventory that one project only |
| Set to a project name | Present | Ignored — project name takes precedence |
| Null or absent | Present | Enumerate all projects in the org |
| Null or absent | Absent | **Validation error** — must specify a project or pass `--all-projects` |

#### Azure DevOps Services — single project

```json
{
  "configVersion": "1.0",
  "source": {
    "type": "AzureDevOpsServices",
    "orgOrCollection": "https://dev.azure.com/myorg",
    "project": "Alpha",
    "apiVersion": "7.1",
    "authentication": {
      "type": "Pat",
      "accessToken": "$ENV:MIGRATION_SOURCE_PAT"
    }
  }
}
```

```
migrate inventory work-items --config migration.json
```

#### Azure DevOps Services — all projects in the org

Same config file with `project` null, plus the `--all-projects` flag:

```
migrate inventory work-items --config migration.json --all-projects
```

#### TFS on-premises — single project (Windows auth)

```json
{
  "configVersion": "1.0",
  "source": {
    "type": "TeamFoundationServer",
    "orgOrCollection": "http://tfs.internal:8080/tfs/DefaultCollection",
    "project": "MyProject",
    "apiVersion": "15.0",
    "authentication": {
      "type": "Windows"
    }
  }
}
```

---

### Mode 2 — Tooling Roster Config (`organisations`-based)

Used for multi-org, multi-project inventory. No `source` or `target` block. Modelled on the `azure-devops-automation-tools` `organisations.json` pattern but embedded directly in the migration platform config file.

The `--all-projects` flag has no effect in this mode; scope is controlled per entry via the `projects` array.

```json
{
  "configVersion": "1.0",
  "organisations": [
    {
      "type": "AzureDevOpsServices",
      "orgOrCollection": "https://dev.azure.com/myorg",
      "projects": ["Alpha", "Beta"],
      "apiVersion": "7.1",
      "authentication": {
        "type": "Pat",
        "accessToken": "$ENV:ORG1_PAT"
      }
    },
    {
      "type": "AzureDevOpsServices",
      "orgOrCollection": "https://dev.azure.com/anotherorg",
      "projects": [],
      "apiVersion": "7.1",
      "authentication": {
        "type": "Pat",
        "accessToken": "$ENV:SHARED_PAT"
      }
    },
    {
      "enabled": false,
      "type": "TeamFoundationServer",
      "orgOrCollection": "http://tfs.internal:8080/tfs/DefaultCollection",
      "projects": [],
      "apiVersion": "15.0",
      "authentication": {
        "type": "Windows"
      }
    }
  ]
}
```

#### `organisations` entry fields

| Field | Required | Default | Description |
|---|---|---|---|
| `type` | Yes | — | `AzureDevOpsServices` or `TeamFoundationServer` |
| `orgOrCollection` | Yes | — | Organisation URL or TFS collection URL |
| `projects` | No | (all) | List of project names to inventory. Absent or empty = enumerate all projects in the org |
| `apiVersion` | Yes | — | API version to pin |
| `authentication` | Yes | — | Auth block (see below) |
| `enabled` | No | `true` | Set to `false` to skip this entry without deleting it |

---

### Authentication types

| `type` | Description | Applies to |
|---|---|---|
| `Pat` | Personal Access Token | `AzureDevOpsServices` |
| `Windows` | Windows Integrated Auth (Kerberos / NTLM). No token field needed. | `TeamFoundationServer` |

### Token resolution order for `Pat` auth

The `accessToken` field value is resolved in this order (highest precedence first):

| Priority | Mechanism | Applies to |
|---|---|---|
| 1 | IConfiguration env var override via `__` separator — e.g. `MigrationTools__Source__Authentication__AccessToken` | `source` / `target` blocks only (standard .NET config layering) |
| 2 | `$ENV:VARNAME` prefix in the `accessToken` field — resolved at the business logic layer | All auth blocks, including `organisations` entries |
| 3 | Literal string value in `accessToken` | All auth blocks |

`$ENV:VARNAME` is the only env-var mechanism available for `organisations` list entries, since list items do not have stable IConfiguration key paths. Multiple entries may reference the same variable (e.g. `"$ENV:SHARED_PAT"`).

### Config validation rules

| Condition | Result |
|---|---|
| Both `organisations` and `source` present | **Validation error** — command exits and explains the conflict |
| Neither `organisations` nor `source` present | **Validation error** — command exits with config guidance |
| Mode 1: `source.project` null, no `--all-projects` flag | **Validation error** — must specify a project or pass `--all-projects` |
| Mode 2: `organisations` array is empty | **Validation error** |
| Mode 2: entry missing `type` or `orgOrCollection` | **Validation error** |

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The CLI MUST expose a `migrate inventory work-items` command accepting `--config <path>` (required) and `--all-projects` (optional flag). These are the only CLI arguments. All connection and authentication details MUST reside in the config file; no bare org URL, project, or PAT arguments are permitted.
- **FR-002**: The command MUST validate the config file on startup and exit with a non-zero code and a clear message if: both `organisations` and `source` are present; neither is present; the config is in Mode 1 with `source.project` null and `--all-projects` is absent; or any required field is missing.
- **FR-013**: When the config contains an `organisations` array (Mode 2), the command MUST iterate over all entries where `enabled` is `true` (or absent — default `true`) and inventory each one. Entries with `enabled: false` MUST be skipped silently.
- **FR-014**: For each `organisations` entry, `projects` is optional. If absent or empty the command MUST enumerate all projects in that org/collection. If non-empty the command MUST restrict inventory to only those named projects.
- **FR-015**: The `accessToken` field in any `authentication` block MUST be resolved as follows: (1) IConfiguration `__`-separator env var override takes highest precedence for `source`/`target` blocks; (2) if the field value starts with `$ENV:VARNAME`, the platform reads env var `VARNAME` at runtime; (3) otherwise the literal value is used. An empty literal with no env var result MUST cause a validation error for `Pat` auth.
- **FR-016**: The `--all-projects` flag MUST only affect Mode 1 (`source`-based) configs. In Mode 2 (`organisations`-based) it MUST be ignored.
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

- Authentication for Azure DevOps uses a Personal Access Token with `vso.work_read` scope. The PAT is resolved via the token resolution order defined in the Configuration Schema (IConfiguration env var override → `$ENV:VARNAME` prefix → literal value). Service principal authentication is out of scope for this feature.
- TFS on-premises uses Windows Integrated Auth; no token field is required in the config.
- Credentials MUST NOT be passed as CLI arguments. The only CLI arguments are `--config` and `--all-projects`.
- `$ENV:VARNAME` resolution applies at the business logic layer — it is not an IConfiguration feature. It allows a single env var to serve as the PAT for all entries in an `organisations` list.
- The initial query window size is 120 days (matching the POC default); may be surfaced as an optional config field in a future revision.
- The maximum items per query threshold is 20,000 (the Azure DevOps and TFS WIQL hard limit).
- Revision counting for Azure DevOps is performed by fetching the `System.Rev` field for each work item returned by the identity query; this is a separate API call counted after all work item IDs are enumerated.
- In Mode 1, `source.project` null without `--all-projects` is a validation error — this is intentional to prevent accidentally inventorying an entire organisation when only one project was intended.
- Repos and Pipelines columns are displayed but left at zero for this feature; they will be populated by a future inventory scope extension.
- The TFS inventory subprocess reuses the existing `DevOpsMigrationPlatform.CLI.TfsMigration` binary with a new `inventory` subcommand — no new binary is introduced.
- `target` in the config file is silently ignored by the inventory command.
- Architecture docs consulted: `docs/cli.md`, `docs/configuration.md`, `docs/source-types.md`, `docs/tfs-exporter.md`, `docs/architecture.md`. All gaps are filed in `discrepancies.md`.
