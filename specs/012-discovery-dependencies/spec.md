# Feature Specification: Discovery Dependency Analysis

**Feature Branch**: `012-discovery-dependencies`  
**Created**: 2026-04-14  
**Status**: Draft  
**Input**: User description: "I'd like to create a new Discovery dependency query that analyses work items for external links. Cross-project and cross-organisation links."

## Clarifications

### Session 2026-04-14

- Q: Should same-project links be included in the dependency report? → A: No. Same-project links are not external dependencies and must be silently ignored. Only `CrossProject` and `CrossOrganisation` links are recorded and reported.
- Q: Does this command use the `source` configuration from a migration config file? → A: No. Like all `discovery *` commands, it uses `DiscoveryOptions` (organisations, authentication, projects) bound directly from the config file root — the same configuration model used by `discovery inventory`. There is no `source`/`target` section.
- Q: How should the project-level dependency summary be delivered? → A: Both — a second CSV file (`discovery-project-dependencies.csv`) written alongside the work-item CSV, plus a compact console table showing directed project pairs after the existing work-item summary.
- Q: What grouping logic should identify which projects must migrate together? → A: Directed pairs only — one row per source→target project pair with a total link count. No connected-component computation required.
- Q: How should cross-organisation links appear in the project dependency grouping? → A: Included as external boundary nodes — the remote org hostname is shown as a leaf node in the group, so the diagram and CSV reflect the full dependency picture including irreversible cross-org links.
- Q: When should the project-level summary be produced? → A: Always, as long as at least one external dependency is found. No flag required.
- Q: What columns should each row in the project dependency CSV contain? → A: `SourceProject`, `TargetProject`, `TargetOrganisation`, `LinkCount`, `LinkScope`, `GroupId` — one row per source+target project pair.
- Q: Should a Mermaid dependency diagram be included in the output? → A: Yes — a Mermaid flowchart written to `discovery-project-dependencies.md` in the same directory as the CSV. It must show directed edges between in-scope projects (labelled with link count), and cross-org targets as distinct boundary nodes labelled with the remote org hostname. Cross-org is part of the core intent, not optional.

## Architecture References

The following architecture and guardrail documents were read before drafting this spec:

| Document | Status |
|----------|--------|
| `agents.md` | Confirmed accurate — discovery commands are local-only, no job submission |
| `docs/architecture.md` | Confirmed accurate — no changes required |
| `docs/cli-guide.md` | **Discrepancy** — does not yet list `discovery dependencies` command |
| `docs/module-development-guide.md` | Confirmed accurate — this feature is not a module |
| `docs/capabilities-guide.md` | **Discrepancy** — inventory section per source type needs a corresponding dependencies section |
| `docs/validation.md` | Confirmed accurate — does not apply to discovery commands |
| `.agents/20-guardrails/core/architecture-boundaries.md` | Confirmed accurate — Rules 16 (no migration logic in CLI) applies |
| `.agents/30-context/domains/cli-commands.md` | **Discrepancy** — `discovery dependencies` command is not yet registered here |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Identify Cross-Project Work Item Links (Priority: P1)

A migration engineer is preparing to migrate one or more projects from Azure DevOps. Before starting the migration they need to know which work items have links pointing to work items in **other projects within the same organisation**. If those linked items are not included in the migration scope, those links will break after migration and the engineer needs to decide whether to expand scope, document breakage, or accept the loss.

**Why this priority**: This is the most common migration planning concern. Cross-project links within the same organisation are very frequent (parent-child relationships across project boundaries, related items, test cases linked to requirements in different projects). Identifying them before migration is essential for scope planning. Without it, migration engineers discover broken links only after the fact.

**Independent Test**: Can be fully tested by running `devopsmigration discovery dependencies --config discovery.json` (where `discovery.json` contains a `DiscoveryOptions`-format config with at least one organisation and project) against a source that has at least one cross-project linked work item, and verifying that the output report lists that work item, its ID, the link type, the target project, and the target work item ID.

**Acceptance Scenarios**:

1. **Given** an organisation and project configured in a `DiscoveryOptions`-format config file, **When** the operator runs `devopsmigration discovery dependencies --config discovery.json`, **Then** the command produces a report listing every work item in the configured project that has at least one link whose target is in a different project within the same organisation.
2. **Given** a project with no cross-project or cross-organisation links, **When** the operator runs the command, **Then** the report is produced with zero rows and the terminal output clearly states "No external dependencies found."
3. **Given** a configured project, **When** the command runs, **Then** each row in the report includes: source work item ID, source work item type, source project, link type (e.g. "Parent", "Related", "Tests", "Tested By", "Duplicate"), target work item ID, target project, and target organisation.

---

### User Story 2 - Identify Cross-Organisation Work Item Links (Priority: P2)

A migration engineer needs to know whether any work items in the source project have links pointing to work items in a **completely different Azure DevOps organisation or TFS collection**. These links cannot be re-created during migration and will always be permanently broken. The engineer needs to document, communicate, and plan for this data loss explicitly.

**Why this priority**: Cross-organisation links are the hardest migration risk to recover from — they represent irreversible data loss. Knowing the count and identity of affected items before migration allows the team to communicate risk clearly to stakeholders and make informed go/no-go decisions.

**Independent Test**: Can be fully tested by running the command against a known source project and verifying that cross-org links appear in the report as a distinct category from cross-project links. A report entry identifying the external organisation hostname distinguishes cross-org from cross-project rows.

**Acceptance Scenarios**:

1. **Given** a source project that contains work items with links to items in a different organisation, **When** the discovery runs, **Then** those links appear in the report and are clearly distinguished from cross-project (same-org) links by a `LinkScope` column with value `CrossOrganisation`.
2. **Given** the report contains cross-organisation links, **Then** the terminal summary prints a warning count for cross-organisation links separately from cross-project links.
3. **Given** a cross-organisation link, **Then** the target URL (or organisation hostname) is recorded in the report even if the target is unreachable, so the engineer can identify which external system is involved.

---

### User Story 3 - Scoped Discovery with WIQL Filter (Priority: P3)

A migration engineer working with a very large project wants to run dependency analysis only against a filtered subset of work items (e.g. only items in a specific area path, or only items in a specific iteration, or only Bugs and User Stories). Running against the full project backlog would be too slow and they only need to understand the dependencies for the items they intend to migrate.

**Why this priority**: Large organisations commonly have projects with hundreds of thousands of work items. Running a full dependency analysis is operationally feasible but time-consuming. Scoping via a WIQL command-line option gives engineers the flexibility to analyse exactly the set of items relevant to their migration plan.

**Independent Test**: Can be fully tested by providing a `--wiql` expression on the command line and verifying that only work items matching the WIQL filter appear in the source column of the report.

**Acceptance Scenarios**:

1. **Given** a `--wiql` option provided on the command line, **When** the operator runs `devopsmigration discovery dependencies --config discovery.json --wiql "SELECT [System.Id] FROM WorkItems WHERE ..."`, **Then** only work items matching the WIQL expression are analysed for outbound links.
2. **Given** an invalid or unparseable WIQL expression passed via `--wiql`, **When** the operator runs the command, **Then** the command exits with error code 1 and a human-readable message identifying the WIQL syntax error before making any network calls.
3. **Given** no `--wiql` option is provided, **When** the command runs, **Then** all work items in every configured project are analysed (equivalent to `SELECT [System.Id] FROM WorkItems`).

---

### User Story 4 - Project-Level Dependency Summary for Consolidation Planning (Priority: P2)

A migration planner needs to understand not just which individual work items have external links, but which **projects depend on which other projects** — including dependencies that cross into different organisations. This higher-level view is essential for consolidation planning: it tells the planner which projects form a dependency group and must all be included in the same migration scope, and which external organisations are entangled.

**Why this priority**: Work-item rows are too granular for scope decisions. A planner needs the project graph to answer: "If I migrate Project A, which other projects must come with it?" The Mermaid diagram makes this visually instant and shareable in GitHub or ADO wiki.

**Independent Test**: After running `devopsmigration discovery dependencies`, verify that `discovery-project-dependencies.csv` exists with one row per directed project pair and that `discovery-project-dependencies.md` contains valid Mermaid syntax rendering a directed graph including any cross-org nodes.

**Acceptance Scenarios**:

1. **Given** the work-item dependency run finds cross-project links between Project A and Project B (42 links) and Project A and an external org (3 links), **When** the command completes, **Then** `discovery-project-dependencies.csv` contains two rows: `A → B, 42, CrossProject, GroupId=1` and `A → external-org-hostname, 3, CrossOrganisation, GroupId=1`.
2. **Given** the project dependency CSV has been written, **Then** `discovery-project-dependencies.md` contains a Mermaid `flowchart LR` block with a directed edge `A -->|42| B` and `A -->|3| external-org-hostname`, and the cross-org node is visually distinguishable (e.g., using a `:::external` class or `[hostname]` label style).
3. **Given** the run completes with at least one external dependency, **Then** the terminal prints a compact directed-pair table (SourceProject → TargetProject/Org, LinkCount, Scope) sorted by LinkCount descending, after the existing work-item summary table.
4. **Given** no external dependencies are found, **Then** neither `discovery-project-dependencies.csv` nor `discovery-project-dependencies.md` is written, and the console prints only the "No external dependencies found." message.

---

### Edge Cases

- What happens when a source project has work items with hundreds of links each? The command must remain memory-safe and not load all work items at once.
- What happens when the target of a link has been deleted in the source organisation? The link should still be reported with a `TargetStatus` of `Deleted`.
- What happens when the authenticated user does not have read access to the linked target project? The link is reported with `TargetStatus` of `AccessDenied` — the command does not fail.
- What happens when the source is a TFS collection and the linked project is in a different TFS collection? The link is classified as `CrossOrganisation`.
- What happens when the same work item appears multiple times in the WIQL result (e.g. due to join artefacts)? It is de-duplicated before analysis.
- What if `--output` points to a file that already exists? The command overwrites it and prints a warning.
- What if connectivity is lost mid-run? The command reports how many items were processed before failure and exits with a non-zero code. Re-running from the beginning is required (no checkpoint support for discovery commands).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST provide a `discovery dependencies` command under the existing `discovery` branch in the CLI that runs locally without submitting a `MigrationJob` to the control plane.
- **FR-002**: The command MUST accept the standard `--config` option pointing to a `DiscoveryOptions`-format configuration file (the same format used by `discovery inventory`) to identify target organisations, projects, and authentication credentials. It MUST NOT depend on or read any `source` or `target` section from a migration config file.
- **FR-003**: The command MUST query all work items in every project listed under each enabled organisation in `DiscoveryOptions` (or only items matching the optional `--wiql` filter), inspect every link on each work item, and **silently discard any link whose target is in the same project as the source**. Only external links are recorded.
- **FR-004**: The command MUST classify each outbound external link into one of two scopes: `CrossProject` (target in a different project within the same organisation) or `CrossOrganisation` (target in a different organisation or collection). `SameProject` links are never written to the report.
- **FR-005**: The command MUST write a dependency report as a CSV file containing only external links (`CrossProject` and `CrossOrganisation`). Default output path is `discovery-dependencies.csv` in the current working directory, overridable with `--output <path>`.
- **FR-006**: Each row in the CSV report MUST include at minimum: `SourceWorkItemId`, `SourceWorkItemType`, `SourceProject`, `LinkType`, `LinkScope`, `TargetWorkItemId`, `TargetProject`, `TargetOrganisation`, `TargetStatus`. The `LinkScope` column MUST only contain values `CrossProject` or `CrossOrganisation`.
- **FR-007**: The command MUST print a console summary table after the report is written, showing: total work items analysed, total external links found, count of `CrossProject` links, count of `CrossOrganisation` links, and report file path.
- **FR-008**: If no cross-project or cross-organisation links are found, the command MUST print "No external dependencies found." and still write an empty CSV with the header row.
- **FR-009**: The command MUST support an optional `--output <path>` parameter allowing the operator to specify a custom file path for the CSV report.
- **FR-010**: For work items where the linked target is inaccessible (deleted, access denied, or in an unreachable organisation), the system MUST still record the link with an appropriate `TargetStatus` value rather than failing.
- **FR-011**: The command MUST support an optional `--wiql <expression>` parameter. When provided, only work items matching the WIQL expression are analysed. When omitted, all work items in the configured projects are analysed.
- **FR-012**: The command MUST respect the `maxConcurrency` setting from `DiscoveryOptions` (if present) when fetching link details to avoid triggering source rate limits.
- **FR-013**: For TFS organisation entries in `DiscoveryOptions`, the command MUST delegate to the `tfsmigration.exe` subprocess via the existing process adapter pattern, using the same credential passing mechanism as `discovery inventory` (credentials via stdin JSON, progress via NDJSON stdout).
- **FR-014**: The command MUST support all three organisation types declared in `DiscoveryOptions`: `AzureDevOpsServices`, `TeamFoundationServer`, and `Simulated`.
- **FR-015**: When at least one external dependency is found, the command MUST write a project-level dependency CSV to `discovery-project-dependencies.csv` (same directory as the work-item CSV, overridable with `--output-projects <path>`). Each row represents one directed source→target project pair and MUST contain: `SourceProject`, `TargetProject`, `TargetOrganisation`, `LinkCount`, `LinkScope`, `GroupId`. Cross-org targets use the remote org hostname as `TargetProject` and populate `TargetOrganisation`; cross-project rows leave `TargetOrganisation` empty. If no external dependencies are found, this file is NOT written.
- **FR-016**: Each row in the project dependency CSV MUST carry a `GroupId` — a stable integer (starting at 1) that groups all projects reachable from each other via directed edges (including cross-org boundary nodes) into the same component. Two projects with no path between them (direct or transitive) MUST have different `GroupId` values.
- **FR-017**: When at least one external dependency is found, the command MUST write a Mermaid flowchart diagram to `discovery-project-dependencies.md` (same directory as the CSVs, overridable with `--output-diagram <path>`). The diagram MUST be a `flowchart LR` block containing: a node per in-scope project, a directed edge per project pair labelled with link count (e.g. `A -->|42| B`), and cross-org targets as distinct leaf nodes labelled with the remote org hostname. Cross-org nodes MUST be visually distinguished using a Mermaid `:::external` CSS class applied via `classDef external fill:#f96`. The file MUST be valid Mermaid renderable in GitHub Markdown and Azure DevOps wiki without plugins.
- **FR-018**: After the existing work-item summary table, the command MUST print a compact project dependency table to the console with columns: `Source Project`, `→ Target`, `Links`, `Scope` — one row per directed pair, sorted by `Links` descending. Cross-org targets display the org hostname in the `→ Target` column prefixed with `🌐`.
- **FR-019**: All project-level outputs (CSV, Mermaid diagram, console table) MUST be computed entirely from the in-memory aggregation of already-collected `DependencyRecord` events. No additional API calls are permitted to produce them.

### Key Entities

- **DependencyRecord**: A single external outbound link from a source work item to a target work item in a different project or organisation. Has source identity, link type, link scope, target identity, and target status. Same-project links are never represented as a `DependencyRecord`. This is the unit written as a CSV row.
- **DependencySummary**: Aggregated counts of external links by scope (`CrossProject` / `CrossOrganisation`) and by link type. Used for the terminal summary table.
- **LinkScope**: Enumeration of `CrossProject`, `CrossOrganisation` only. `SameProject` is not a valid value — those links are filtered before any record is created.
- **TargetStatus**: Enumeration of `Reachable`, `Deleted`, `AccessDenied`, `Unknown`.
- **ProjectDependencyRecord**: An aggregated directed project-pair row for the project-level CSV. Properties: `SourceProject` (string), `TargetProject` (string — remote org hostname for cross-org pairs), `TargetOrganisation` (string — empty for cross-project, org hostname for cross-org), `LinkCount` (int), `LinkScope` (LinkScope), `GroupId` (int). Computed by aggregating `DependencyRecord` events in memory after the streaming pass completes; never fetched from the API.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A migration engineer can run `devopsmigration discovery dependencies` against any supported source type and receive a complete dependency report within a time proportional to the number of work items and links (no artificial ceiling or timeout other than the configured network timeout).
- **SC-002**: The CSV report can be opened directly in Microsoft Excel or imported into any spreadsheet tool without additional transformation.
- **SC-003**: For a project with up to 50,000 work items and an average of 10 links per item, the command completes without running out of memory — work items are processed in a streaming fashion, not loaded all at once.
- **SC-004**: The terminal summary is legible and actionable within 5 seconds of the command completing — it must be brief enough to be read without scrolling and must distinguish cross-project from cross-organisation counts clearly.
- **SC-005**: A migration engineer using the report can identify all work items requiring scope expansion or stakeholder communication before running the migration, reducing post-migration link breakage discoveries to zero for the items that were in scope.
- **SC-006**: Cross-organisation links are always flagged with a distinct visual warning in the terminal summary so they are never missed or confused with less severe cross-project links.
- **SC-007**: The Mermaid diagram written to `discovery-project-dependencies.md` must render correctly in GitHub Markdown preview and Azure DevOps wiki without any additional plugins or extensions. Node and edge labels must not contain characters that break Mermaid syntax (quotes, parentheses, angle brackets in node IDs must be sanitised or quoted).

## Assumptions

- **Scope boundary**: This command analyses **outbound** links only (links where the source work item is in the configured project). Inbound links from other projects pointing into this project are out of scope for this feature.
- **SameProject links are always ignored**: Links where both the source and target belong to the same project are discarded during analysis and are never written to the report. The report and terminal output cover external dependencies exclusively.
- **Credentials**: Authentication is read from `DiscoveryOptions.Organisations[].Authentication` in the discovery config file — the same model used by `discovery inventory`. There is no `source` or `target` section involved.
- **TFS cross-collection detection**: Two work items are considered cross-organisation if their collection URLs differ (case-insensitive host comparison). The command does not attempt to authenticate against the remote collection to verify target existence.
- **Simulated source support**: When an organisation entry in `DiscoveryOptions` has `Type` = `Simulated`, the command generates synthetic dependency records so the feature can be tested without a live ADO organisation.
- **No checkpoint support**: Discovery commands are fast local queries. If the run is interrupted, the operator re-runs from scratch. This is consistent with `discovery inventory`.
- **Report format**: CSV is chosen as the primary output format for compatibility with common analysis tools. A machine-readable JSON summary format is out of scope for this initial version.

