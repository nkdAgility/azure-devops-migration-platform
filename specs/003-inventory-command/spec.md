# Feature Specification: Inventory Command — Config-Driven, Multi-Source, Paginated

**Feature Branch**: `003-inventory-command`  
**Created**: 2025-07-14  
**Status**: Draft  

## Architecture References

The following canonical documents were read before drafting this spec. Discrepancies between this spec and those documents are recorded in `discrepancies.md`.

| Document | Status | Notes |
|---|---|---|
| [docs/cli.md](../../docs/cli.md) | ⚠️ Discrepancy logged | `inventory` command not listed in the command table; discrepancy logged |
| [docs/configuration.md](../../docs/configuration.md) | ⚠️ Discrepancy logged | No `inventory` section in the config schema; discrepancy logged |
| [docs/source-types.md](../../docs/source-types.md) | ✅ Confirmed accurate | `TeamFoundationServer` subprocess bridge applies to inventory; confirmed |
| [.agents/guardrails/system-architecture.md](../../.agents/guardrails/system-architecture.md) | ✅ Confirmed accurate | Rules 16 and 19 directly govern this feature |
| [.agents/guardrails/coding-standards.md](../../.agents/guardrails/coding-standards.md) | ✅ Confirmed accurate | `IOptions<T>` pattern, sealed options, Spectre.Console CLI rules confirmed |

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Config-Driven Single-Organisation Discovery (Priority: P1)

An operator wants to discover all projects and their work item counts in one Azure DevOps organisation. They create a `migration.json` with an `inventory` section that specifies the organisation URL and PAT, then run `migrate discovery inventory --config migration.json`. The command reads the config, connects to the organisation, discovers all projects, counts their work items, and displays a live-updating table. When complete, it optionally writes a CSV summary.

**Why this priority**: This is the core MVP — a single-org run with no project filter. It replaces the existing raw `--organisation`/`--token` CLI options with a maintainable, config-driven pattern. All subsequent stories build on this foundation.

**Independent Test**: Can be tested end-to-end by placing a valid `migration.json` with an `inventory.sources` array (single entry, no project filter) in the working directory and running the command. Output should list every project in the organisation with a work item count.

**Acceptance Scenarios**:

1. **Given** a config file with a single source specifying an org URL and a PAT with no project filter, **When** the operator runs the inventory command, **Then** the command discovers all projects in that organisation and prints a work item count for each project.
2. **Given** a config file with a single source and a specific project name, **When** the operator runs the inventory command, **Then** only that project is queried and counted; other projects in the organisation are not included in the output.
3. **Given** a valid config file, **When** the operator also passes `--out results.csv`, **Then** the summary is written to `results.csv` with one row per project containing the project name and work item count.
4. **Given** an organisation with no projects, **When** the inventory command runs, **Then** the command prints a clear "no projects found" message and exits with code 0.

---

### User Story 2 — Environment Variable Token Resolution (Priority: P1)

A CI/CD operator wants to avoid storing PATs in the config file. They write `"token": "$ENV:ADO_PAT"` in the inventory source entry. At runtime the tool resolves the actual PAT from the `ADO_PAT` environment variable before making any API calls. If the variable is not set, the command fails immediately with a specific error message identifying the missing variable name.

**Why this priority**: Required for safe use in automated pipelines and shared repositories. A PAT stored in plain text in a checked-in config file is a security risk. This must ship alongside P1 to make the feature safely usable from the first release.

**Independent Test**: Set `ADO_PAT=<valid-pat>` in the shell environment, run with a config containing `"token": "$ENV:ADO_PAT"` — inventory should succeed. Then unset `ADO_PAT` and re-run — the command should exit with a non-zero code and a message identifying the missing variable.

**Acceptance Scenarios**:

1. **Given** a config with `"token": "$ENV:ADO_PAT"` and the `ADO_PAT` environment variable is set to a valid PAT, **When** the inventory command runs, **Then** the PAT is resolved at runtime and the command succeeds as if the token were specified inline.
2. **Given** a config with `"token": "$ENV:MISSING_VAR"` and `MISSING_VAR` is not set in the environment, **When** the inventory command runs, **Then** the command fails before making any API calls and prints a message identifying the specific missing variable name.
3. **Given** a config with an inline PAT (no `$ENV:` prefix), **When** the inventory command runs, **Then** the token is used as-is without any environment variable lookup.
4. **Given** the token resolver is called with a non-`$ENV:` value, **When** it processes the value, **Then** it returns the value unchanged (idempotent for plain strings).

---

### User Story 3 — Accurate Work Item Counts for Large Projects (Priority: P1)

A platform team inventorying a large enterprise organisation knows some projects have well over 20,000 work items — the maximum returned by a single Azure DevOps WIQL query. They need the inventory command to report accurate totals, not capped counts. The command paginates internally across as many query pages as needed to accumulate the real count.

**Why this priority**: Without pagination, work item counts are silently wrong for large projects. This is a data accuracy requirement — an incorrect count misleads capacity planning decisions. It must be correct by default with no operator opt-in required.

**Independent Test**: Can be verified with a mock or stub that returns a full page (20,000 items) on the first call and a partial page on the second; the reported count must equal the sum across all pages.

**Acceptance Scenarios**:

1. **Given** a project with exactly 20,000 work items, **When** the command counts its work items, **Then** the reported count is 20,000 and pagination stops after one full page (no further query is issued once the continuation is empty).
2. **Given** a project with 25,000 work items, **When** the command counts its work items, **Then** the reported count is 25,000, achieved by summing two paginated query results (20,000 + 5,000).
3. **Given** a project with 500 work items, **When** the command counts its work items, **Then** the reported count is 500 and only a single query page is issued.
4. **Given** any project, **When** a WIQL page returns fewer results than the maximum page size, **Then** pagination stops and no further query is issued for that project.

---

### User Story 4 — Multi-Source Inventory (Priority: P2)

A migration architect managing several Azure DevOps organisations runs inventory across all of them in a single command. The `migration.json` config contains an `inventory` section with a `sources` array listing multiple organisations (each with its own URL and token). The command processes each source sequentially, displaying labelled progress per source, and produces a unified summary.

**Why this priority**: Multi-org inventory eliminates the need to run the command multiple times and merge results manually. It is a significant usability improvement for large-scale migrations, but single-org (P1) must work first.

**Independent Test**: A config with two entries in `inventory.sources` (pointing to different org URLs) should produce labelled output rows for both organisations. Both sources must appear in any CSV output.

**Acceptance Scenarios**:

1. **Given** a config with two sources specifying two distinct org URLs, **When** the inventory command runs, **Then** the command processes both organisations sequentially, displaying labelled output for each, and exits with code 0 if both succeed.
2. **Given** a config with two sources where the first succeeds and the second fails authentication, **When** the inventory command runs, **Then** the first source's results are reported, the second source's failure is reported with a clear error, and the command exits with a non-zero code.
3. **Given** a config with multiple sources each using a distinct `$ENV:` token reference, **When** the inventory command runs, **Then** each source's token is resolved independently from its named environment variable.

---

### User Story 5 — CLI Project Override (Priority: P2)

A developer wants to do a quick spot-check on one project without editing the config file. They pass `--project MyProject` on the command line. This overrides or filters to that specific project regardless of what the config specifies, enabling ad-hoc single-project runs from an otherwise-unchanged config file.

**Why this priority**: Reduces friction for common day-to-day usage. Editing a config file for a single-project check is error-prone (risk of committing the edited file). A CLI override is safer and faster.

**Independent Test**: A config with no project filter plus `--project MyProject` on the CLI should inventory only `MyProject`. Without `--project`, all projects are inventoried.

**Acceptance Scenarios**:

1. **Given** a config with no project filter and `--project MyProject` on the CLI, **When** the inventory command runs, **Then** only `MyProject` is inventoried; no other projects are queried.
2. **Given** a config with `"project": "OtherProject"` in the source and `--project MyProject` on the CLI, **When** the inventory command runs, **Then** the CLI value (`MyProject`) takes precedence and only `MyProject` is inventoried.
3. **Given** a multi-source config and `--project MyProject` on the CLI, **When** the inventory command runs, **Then** the `--project` override is applied to all sources.
4. **Given** `--project` is not passed on the CLI, **When** the inventory command runs, **Then** the project setting from the config (or absence thereof) is used unchanged.

---

### User Story 6 — TFS On-Premises Source Type (Priority: P3)

An enterprise operator managing a legacy Team Foundation Server instance needs to inventory its projects before planning a migration to Azure DevOps Services. They set `"type": "TeamFoundationServer"` in the inventory source config. The command delegates the inventory operation to the TFS subprocess via `ExternalToolRunner`, following the same process bridge protocol used for TFS export.

**Why this priority**: Extends inventory to on-premises TFS, matching the source-type parity described in `docs/source-types.md`. P3 because TFS sources are less common than Azure DevOps Services and can ship after the cloud path is proven.

**Independent Test**: Can be tested with a mock subprocess that returns a valid NDJSON inventory result. The .NET 10 inventory command should read the subprocess output and incorporate it into the summary table without any TFS-specific code in the .NET 10 layer.

**Acceptance Scenarios**:

1. **Given** a config with `"type": "TeamFoundationServer"` in the inventory source, **When** the inventory command runs, **Then** the command delegates to the TFS subprocess via `ExternalToolRunner` and does not attempt a direct REST API call to the TFS server.
2. **Given** the TFS subprocess exits with code 0, **When** the inventory command processes its NDJSON output, **Then** the project summaries are incorporated into the terminal display and CSV output.
3. **Given** the TFS subprocess exits with a non-zero exit code, **When** the inventory command handles the failure, **Then** it reports a clear error using the subprocess stderr content and exits with a non-zero code.
4. **Given** a config with `"type": "AzureDevOpsServices"`, **When** the inventory command runs, **Then** it uses the REST API path directly with no subprocess involved.

---

### Edge Cases

- **Empty organisation**: Organisation URL is present but has zero projects → "No projects found" message, exit 0.
- **Unreachable endpoint**: Network error connecting to an org URL → command reports the specific source that failed with the connection error; continues to remaining sources if multi-source; exits with non-zero code.
- **Malformed `$ENV:` syntax**: Token field contains `$ENV:` with no variable name (e.g., `"$ENV:"`) → fails with a clear parse error before any API calls are made.
- **Missing `inventory` section**: Config file has no `inventory` key → command fails with a message explaining the missing section and pointing to the expected format.
- **Config version mismatch**: Config file `configVersion` is newer than the tool supports → fails fast with a clear version-mismatch error (consistent with existing config versioning rules).
- **Project filter matches no project**: `--project` or config project filter specifies a project that does not exist in the organisation → the project is reported with a "not found" warning and a count of zero; command exits with a non-zero code.
- **Pagination with API errors mid-sequence**: A WIQL page request fails part-way through paginating a large project → the partial count is reported with a warning marker rather than silently returning a wrong total; command exits with a non-zero code.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The inventory command MUST read its source configuration from the JSON config file loaded via `--config` (defaulting to `migration.json` in the working directory).
- **FR-002**: The config file MUST support an `inventory` section with a `sources` array; each source entry MUST include `type`, `orgOrCollection`, an optional `project`, and a `token` field.
- **FR-003**: The `token` field MUST support `$ENV:VARNAME` syntax; the tool MUST resolve the token value from the named environment variable at runtime before any API call is made.
- **FR-004**: When a `$ENV:VARNAME` token references an environment variable that is not set, the command MUST fail immediately with an error message that identifies the missing variable by name.
- **FR-005**: When no `project` is specified in a source entry, the command MUST discover and count all projects in that organisation or collection.
- **FR-006**: When a `project` is specified in a source entry, the command MUST count work items for that project only.
- **FR-007**: The command MUST accept a `--project` CLI option that overrides the project filter for all sources during that invocation.
- **FR-008**: Work item counting MUST paginate WIQL queries to accurately count projects with more than 20,000 work items; the reported total MUST equal the sum across all pages.
- **FR-009**: The `sources` array MUST support multiple entries (multiple organisations); the command MUST process each source sequentially and display labelled output per source.
- **FR-010**: The command MUST support `"type": "AzureDevOpsServices"` as a source type, using the REST API for discovery and counting.
- **FR-011**: The command MUST support `"type": "TeamFoundationServer"` as a source type; all inventory logic for TFS MUST be delegated to the TFS subprocess via `ExternalToolRunner` — no TFS Object Model code in the .NET 10 layer.
- **FR-012**: Token resolution (`$ENV:VARNAME` expansion) MUST be implemented as a shared utility accessible to other commands; it MUST NOT be inline ad-hoc logic inside `InventoryCommand`.
- **FR-013**: The existing `--organisation` and `--token` CLI options MUST be removed from the inventory command; all connection configuration MUST come from the config file.
- **FR-014**: Inventory options MUST be bound via `IOptions<T>` with a sealed options class containing a `SectionName` constant; the options MUST be registered in the DI container.
- **FR-015**: The command MUST display a live-updating terminal table showing per-project progress as inventory runs, using Spectre.Console live rendering.
- **FR-016**: The command MUST accept `--out <PATH>` to write the final summary to a CSV file; this option is optional and the command MUST complete successfully without it.
- **FR-017**: Adding the `inventory` section to the config schema MUST be treated as a breaking config schema change and MUST result in a `configVersion` increment with a corresponding upgrader.
- **FR-018**: Any source processing failure MUST be reported with a clear, source-specific error message; the command MUST exit with a non-zero code if any source fails.

### Key Entities

- **Inventory Source**: A single connection target. Attributes: `type` (`AzureDevOpsServices` | `TeamFoundationServer`), `orgOrCollection` (URL or collection path), optional `project` (single-project filter), `token` (plain PAT or `$ENV:VARNAME` reference).
- **Inventory Options**: The options object bound from the `inventory` config section via `IOptions<T>`. Contains the `sources` array. Sealed, with a `SectionName` constant. Registered in the CLI DI container.
- **Token Resolver**: A shared utility that accepts a raw token string and returns the resolved value. If the string begins with `$ENV:`, it reads the remainder as an environment variable name and returns that variable's value. Otherwise it returns the input unchanged. Fails with a descriptive error when the referenced variable is absent.
- **Project Summary**: The in-memory result for one project: project name, project ID, source organisation URL, and total work item count accumulated across all pagination pages.
- **Inventory Result**: The aggregate output of one source run: source label (org/collection URL), list of `ProjectSummary` records, and any error state for that source.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator with a valid config file can discover work item counts for all projects in an organisation without passing any credentials on the command line and without editing the config to insert raw PAT values.
- **SC-002**: Projects with more than 20,000 work items report the exact actual count; the difference between the reported count and the true count is zero for any project size.
- **SC-003**: When a `$ENV:` token reference is used, no PAT value appears in the config file, the shell command history, or any log output; the token is resolved only in memory at runtime.
- **SC-004**: A config file with multiple sources produces a complete inventory in a single command invocation; the operator does not need to run the command once per organisation.
- **SC-005**: Every failure mode (auth error, unreachable endpoint, missing env var, project not found) exits with a non-zero code and a human-readable message identifying the specific source and failure type — no silent failures, no generic error messages.
- **SC-006**: Adding an `inventory` section to an existing `migration.json` does not break any other command (`export`, `import`, `both`, `validate`, etc.).
- **SC-007**: A TFS on-premises source is inventoried using the same config structure as an Azure DevOps Services source; no TFS-specific CLI flags are required.

---

## Assumptions

*Architecture docs read:*
- `agents.md` — binding entry point confirmed
- `docs/cli.md` — CLI architecture and command table reviewed
- `docs/configuration.md` — config schema, versioning rules, and upgrader requirement confirmed
- `docs/source-types.md` — `AzureDevOpsServices` and `TeamFoundationServer` source type contracts confirmed
- `.agents/guardrails/system-architecture.md` — rules 16 (CLI must not contain migration logic) and 19 (TFS Object Model in subprocess only) are directly applicable
- `.agents/guardrails/coding-standards.md` — `IOptions<T>` pattern, sealed options, Spectre.Console CLI requirement, `AzureDevOps` naming convention confirmed

*Assumptions and scope decisions:*

- The inventory command is a **discovery tool only** — it reads from source systems and reports counts. No package is written. No `IArtefactStore` or `IStateStore` is used by this command.
- The `inventory` config section is additive to `migration.json`; it coexists with the existing `source`, `target`, and `modules` sections without conflict.
- Config version will be incremented (e.g., `1.0` → `2.0`) to reflect the addition of the `inventory` section as a breaking schema change, consistent with the versioning rules in `docs/configuration.md`.
- The WIQL pagination batch size is 20,000 items per page (the Azure DevOps REST API WIQL maximum). This is not configurable in v1.
- Sources are processed **sequentially** to avoid PAT rate-limiting on shared tokens. Parallel processing is out of scope for this feature.
- The `--out` (CSV output) option is preserved from the existing command and applies across all sources combined.
- The TFS inventory subprocess protocol follows the same stdin/stdout NDJSON process bridge defined in `docs/tfs-exporter.md`; a `TfsInventoryRequest` DTO is sent via stdin.
- The `--project` CLI override applies to **all sources** when multiple sources are configured. Per-source project overrides remain in the config file.
- `apiVersion` is optional in inventory source config; the command defaults to `7.1` for Azure DevOps Services when not specified.
- The inventory command does not submit a job to the control plane. It runs entirely within the CLI process as a local, synchronous operation.
- Credentials for TFS sources are passed via subprocess stdin JSON only — never via command-line arguments to the subprocess (consistent with system-architecture rule 19 and coding-standards credential rules).
