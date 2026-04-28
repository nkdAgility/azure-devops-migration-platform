# Proposed Features: Rationalised into Platform Terminology

> **Source**: `feature-gap.md` — gaps from `azure-devops-migration-tools` (v16+) and `azure-devops-automation-tools` (PowerShell scripts)
> **Target architecture**: `azure-devops-migration-platform` — Source → Package → Target, modules + extensions + tools
>
> Generated: 2026-04-22

---

## How to Read This Document

| Term | Meaning |
|------|---------|
| **Module** | A domain-scoped unit that implements `IModule`. Runs inside the Migration Agent. Handles one concern (e.g. `WorkItems`, `Teams`). Has both export and import paths. |
| **Extension** | A named sub-data collector declared inside a module config block (e.g. `Revisions`, `Links`, `Attachments`). Enabled/disabled independently per run. |
| **Tool** | A shared, cross-cutting service declared once at MigrationPlatform config root (e.g. `FieldTransformTool`, `NodeStructureTool`). Extensions load tools by reference and may override selected values. Not a CLI command. |
| **Scope** | A mandatory selection criterion on a module (e.g. `wiql` query, project name). |
| **Discovery command** | A `discovery *` CLI sub-command that runs locally without submitting a `MigrationJob`. Reads source systems directly via REST. |
| **CLI feature** | A `queue`, `config`, `manage`, or `admin` command addition. |
| **Config feature** | A new field or schema addition to the scenario JSON config. |

### Tool Resolution Model (Canonical for This Proposal)

- Tools are declared under `MigrationPlatform.Tools` as a keyed object (key = tool type name, e.g. `"NodeStructure"`). This is the single source of truth.
- Modules do not declare tool definitions.
- Extensions use the tool singleton declared at the `Tools` root; there is no per-extension tool reference array.
- Per-type strategy overrides (e.g. on `WorkItemResolution`) are properties of the tool config itself, not extension-level schema constructs.

Status legend:

| Icon | Meaning |
|------|---------|
| ✅ | Implemented |
| 🔶 | Partially implemented |
| ❌ | Not implemented |
| 🆕 | Net-new — did not exist in either old tool |

---

## Index — Ordered by Perceived Value

### Modules

| # | Module | Status | Summary |
|---|--------|--------|---------|
| M2 | [WorkItemsModule — NodeStructure tool](#m2-workitemsmodule--nodestructure-tool) | ✅ | Area/iteration path mapping, creation, and language override |
| M3 | [WorkItemsModule — WorkItemTypeMapping tool](#m3-workitemsmodule--workitemtypemapping-tool) | ❌ | Agile↔Scrum type remapping |
| M4 | [WorkItemsModule — missing options](#m4-workitemsmodule--missing-options) | ❌ | CollapseRevisions, MaxRevisions, GracefulFailures, etc. |
| M5 | [TeamsModule](#m5-teamsmodule) | ❌ Placeholder | Team settings, members, capacity, iteration paths |
| M6 | [TestManagementModule](#m6-testmanagementmodule) | ❌ Not started | Test plans, suites, cases, shared steps, configurations |
| M7 | [GitModule](#m7-gitmodule) | ❌ Placeholder | Full git repository mirror migration |
| M8 | [SharedQueriesModule](#m8-sharedqueriesmodule) | ❌ Not started | Shared query folders with project-name remapping |
| M9 | [ProcessDefinitionsModule](#m9-processdefinitionsmodule) | ❌ Not started | Process templates, WIT definitions, fields, layouts |
| M10 | [ProfilePicturesModule](#m10-profilepicturesmodule) | ❌ Not started | AD profile picture export/import |

### Tools (shared cross-cutting services)

| # | Tool | Status | Summary |
|---|------|--------|---------|
| T2 | [NodeStructureTool](#t2-nodestructuretool) | ✅ | Area/iteration path regex mapping + auto-creation |
| T3 | [WorkItemTypeMappingTool](#t3-workitemtypemappingtool) | ❌ | Work item type name remapping table |
| T4 | [StringManipulatorTool](#t4-stringmanipulatortool) | ✅ Covered by `FieldTransformTool` | Regex-based field string cleanup (implemented via `RegexFieldTransform` and 14+ transform types in `FieldTransformTool`) |
| T5 | [GitRepositoryMappingTool](#t5-gitrepositorymappingtool) | ❌ | Source→target repo name mapping for GitModule and link rewriting |
| T6 | [ChangesetMappingTool](#t6-changesetmappingtool) | ❌ | TFS changeset → Git commit SHA mapping |
| T7 | [WorkItemResolutionTool](#t7-workitemresolutiontool) | 🆕 🔶 | Per-work-item-type strategy for finding existing items in the target (field, hyperlink, or title match); handles types that cannot have custom fields (e.g. Shared Steps). Strategy pattern (`IWorkItemResolutionStrategy`) exists; full tool config not yet implemented |

### Discovery Commands

| # | Command | Status | Summary |
|---|---------|--------|---------|
| D1 | [`discovery org-sync`](#d1-discovery-org-sync) | ❌ | Enumerate all projects from configured orgs and upsert into the `organisations` roster |
| D2 | [`discovery inventory` — missing artefact types](#d2-discovery-inventory--missing-artefact-types) | 🔶 | Add pipelines, repos, test plans, suites, process name, shared steps counts to existing command |
| D3 | [`discovery process`](#d3-discovery-process) | 🆕 ❌ | Export process/WIT/field/layout metadata and produce a source↔target diff report |

### CLI Features

| # | Feature | Status | Summary |
|---|---------|--------|---------|
| C1 | [`config generate`](#c1-config-generate) | ❌ | Bulk stamp scenario-config templates per org+project from template library |
| C2 | [`queue --batch`](#c2-queue---batch) | ❌ | Queue multiple jobs from a roster of generated configs |
| C3 | [`admin field install`](#c3-admin-field-install) | ❌ | Idempotent custom field + picklist + control installation across orgs |
| C4 | [`admin field install-reflected-id`](#c4-admin-field-install-reflected-id) | ❌ | Install `Custom.ReflectedWorkItemId` across all orgs/processes/WITs |
| C5 | [`admin field delete`](#c5-admin-field-delete) | ❌ | Delete a field by reference name from all enabled orgs |
| C6 | [`admin page install`](#c6-admin-page-install) | ❌ | Idempotent page/group installation on WIT form layouts across orgs |

### Platform Features

| # | Feature | Status | Summary |
|---|---------|--------|---------|
| P1 | [Checkpoint Reconciliation](#p1-checkpoint-reconciliation) | 🆕 🔶 | Rebuild missing/corrupted checkpoint state from existing package data across all modules. Implemented for discovery modules (`DependencyDiscoveryModule`); not yet generalised across all modules |
| P4 | [Operator Interaction / Pending Questions](#p4-operator-interaction--pending-questions) | 🆕 ❌ | Allow a running MigrationJob to pause and request operator input via TUI or CLI follow-mode; Agent enters "Pending" state on Control Plane until the answer is provided |
| P5 | [Cloud Deployment — Ring-Based ControlPlane + Agents](#p5-cloud-deployment--ring-based-controlplane--agents) | 🆕 ❌ | Deploy ControlPlaneHost and MigrationAgent(s) to Azure Container Apps via `azd up` with three deployment rings (canary, preview, release) on `devopsmigration.io` |

---

## Modules — Detail

### M2: WorkItemsModule — NodeStructure Tool

**Current state**: ✅ **Implemented.** `NodeStructureTool` (`src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/NodeStructureTool.cs`) provides area/iteration path regex mapping, language override (`AreaLanguageOverride`, `IterationLanguageOverride`), auto-create missing nodes (`AutoCreateNodes`), skip on unresolvable paths, and replicate source tree. Options in `NodeStructureOptions.cs`.

**Why it matters**: Almost every migration involves renaming or restructuring area/iteration trees. Without this, imports to a different project hierarchy fail silently or require manual remediation.

**Proposed additions**:

#### New Tool: `NodeStructureTool` (see [T2](#t2-nodestructuretool))

```json
{
  "MigrationPlatform": {
    "Tools": {
      "NodeStructure": {
        "areaMap":      { "OldOrg\\OldProject\\Team A": "NewOrg\\NewProject\\Team A - Migrated" },
        "iterationMap": { "OldOrg\\OldProject\\Sprint 1": "NewOrg\\NewProject\\Sprint 1" },
        "areaLanguageOverride":      "Area",
        "iterationLanguageOverride": "Iteration",
        "createMissingNodes": true,
        "skipRevisionWithInvalidAreaPath": false,
        "skipRevisionWithInvalidIterationPath": false,
        "replicateAllExistingNodes": false
      }
    },
    "Modules": {
      "WorkItems": {
        "Enabled": true,
        "Extensions": {
          "Revisions": { "Enabled": true }
        }
      }
    }
  }
}
```

**Features within the tool**:

| Feature | Description |
|---|---|
| Regex-based area path mapping | Source path → target path via regex replace |
| Regex-based iteration path mapping | Source path → target path via regex replace |
| Auto-create missing area/iteration nodes | Call ADO API to create missing paths before import |
| Language override | Map localised node names (e.g. Spanish "Área" → "Area") |
| Skip revision on invalid area path | Drop revision if path cannot be resolved/created |
| Skip revision on invalid iteration path | Drop revision if path cannot be resolved/created |
| Replicate all nodes from source | Enumerate and create all area/iteration nodes regardless of whether any work item uses them |
| Convert area paths to tags | Optionally flatten the tree into `System.Tags` instead of mapping (see also `TreeToTagField` in FieldMappingTool) |

---

### M3: WorkItemsModule — WorkItemTypeMapping Tool

**Current state**: Work item types are written verbatim from source into `revision.json`. On import, if the type name differs, the import fails.

**Proposed additions**:

#### New Tool: `WorkItemTypeMappingTool` (see [T3](#t3-workitemtypemappingtool))

```json
{
  "MigrationPlatform": {
    "Tools": {
      "WorkItemTypeMapping": {
        "map": {
          "User Story":    "Product Backlog Item",
          "Issue":         "Impediment"
        }
      }
    },
    "Modules": {
      "WorkItems": {
        "Enabled": true,
        "Extensions": {
          "Revisions": { "Enabled": true }
        }
      }
    }
  }
}
```

Applied during both export (stored in `revision.json` as `_mappedType`) and import (used as the target `workItemType` on creation).

---

### M4: WorkItemsModule — Missing Options

Options that belong directly on the `WorkItems` module config rather than as separate tools.

| Option | Config Key | Default | Description |
|---|---|---|---|
| Explicit work item ID list | `scopes[].type: "ids"` | — | New scope type: select work items by explicit ID list instead of WIQL |
| Collapse revisions to single item | `collapseRevisions` | `false` | Write only the latest revision rather than the full history |
| Maximum revision limit | `maxRevisions` | `0` (unlimited) | Cap the number of revisions written per work item |
| Graceful failure tolerance | `maxGracefulFailures` | `0` (strict) | Continue past N item-level failures before aborting the module |
| Generate migration comment | `generateMigrationComment` | `false` | Append a comment to each imported work item recording source ID, org, and timestamp |
| Maximum attachment size | `extensions[Attachments].maxSizeBytes` | unlimited | Skip attachment download if file exceeds this size |
| Link count filter | `extensions[Links].filterIfCountMatches` | — | Skip import of links if the work item already has a matching link count |
| Outbound link checking | `extensions[Links].checkOutboundLinks` | `false` | Validate that outbound link targets exist before import |

---

### M5: TeamsModule

**Current state**: Placeholder feature files exist. No implementation.

**Why it matters**: Team settings, capacity, and iteration assignments are required to restore the full team operating model after migration.

**Proposed Module**: `TeamsModule`

**Scopes**:
- `type: "teams"` with optional `filter` (team name pattern)
- `type: "all"` — all teams in the project

**Extensions**:

| Extension | Description |
|---|---|
| `TeamIterations` | Export/import iteration path assignments per team |
| `TeamMembers` | Export/import team member assignments |
| `TeamCapacity` | Export/import sprint capacity planning data |
| `TeamSettings` | Export/import team backlog, working days, and area config |

**Dependency**: `TeamsModule` depends on `IdentitiesModule` (team members are identities).

**Additional CLI feature**: Export the list of all teams to `teams.csv` as part of `discovery inventory`.

---

### M6: TestManagementModule

**Current state**: No implementation. Not started.

**Why it matters**: Test plans, suites, and cases represent significant engineering investment and are often a hard requirement for compliance-regulated migrations.

**Proposed Module**: `TestManagementModule`

**Scopes**:
- `type: "plans"` with optional plan name filter
- `type: "all"` — all test plans in the project

**Extensions**:

| Extension | Description |
|---|---|
| `TestPlans` | Export/import test plan definitions |
| `TestSuites` | Export/import static and dynamic suite hierarchy |
| `TestCases` | Export/import test case work items (delegates to `WorkItemsModule` for revision history) |
| `SharedSteps` | Export/import shared step work items |
| `SharedParameters` | Export/import shared parameter data sets |
| `TestConfigurations` | Export/import test configurations |
| `TestVariables` | Export/import test variables |

**Dependency**: `TestManagementModule` depends on `WorkItemsModule` (test cases are work items).

---

### M7: GitModule

**Current state**: Placeholder feature files exist. No implementation beyond name-mapping tool reference.

**Proposed Module**: `GitModule`

**Scopes**:
- `type: "repositories"` with optional repo name filter
- `type: "all"` — all repositories in the project

**Extensions**:

| Extension | Description |
|---|---|
| `Mirror` | Full `git --mirror` push: all branches, tags, and ref deletions |
| `Additive` | Push branches/tags without deleting refs removed from source |
| `RepositoryMapping` | Apply source→target repository name mapping (see `GitRepositoryMappingTool`) |

**Behaviour**:
- Enumerate repos from source via REST API.
- Create the target repository if it does not exist (same name after mapping).
- Perform a temporary bare clone, then push to target.
- Safety: target projects must exist — they are not created automatically.
- PAT values are never written to the package or logged.

**Tool dependency**: `GitRepositoryMappingTool` (see [T5](#t5-gitrepositorymappingtool)).

---

### M8: SharedQueriesModule

**Current state**: No implementation. Not started.

**Proposed Module**: `SharedQueriesModule`

**Scopes**:
- `type: "folder"` with a root folder path
- `type: "all"` — entire shared query tree

**Extensions**:

| Extension | Description |
|---|---|
| `FolderHierarchy` | Preserve the full folder structure on import |
| `ProjectNameRewrite` | Rewrite project names embedded in WIQL query strings to match the target project |

---

### M9: ProcessDefinitionsModule

**Current state**: No implementation. Not started.

**Why this is high-effort**: The ADO Process API is complex; many operations (e.g. updating picklist IDs) are unsupported. This module is better served by the `admin field install` CLI commands for field-level work, and by the Microsoft `process-migrator` tool for full template migration.

**Proposed Module**: `ProcessDefinitionsModule`

**Scopes**:
- `type: "processes"` with optional name filter

**Extensions**:

| Extension | Description |
|---|---|
| `WorkItemTypes` | Export/import custom work item type definitions |
| `Fields` | Export/import field definitions and picklists |
| `Layouts` | Export/import page/group/control layout definitions |
| `States` | Export/import state workflow definitions |
| `Rules` | Export/import field rules and behaviours |

**Note**: Full process template migration (XML → Inherited process) may delegate to `microsoft/process-migrator` as an external tool invocation, similar to how TFS export delegates to a .NET 4.8 subprocess.

---

### M10: ProfilePicturesModule

**Current state**: No implementation. Not started. Low priority.

**Proposed Module**: `ProfilePicturesModule`

**Extensions**:

| Extension | Description |
|---|---|
| `ExportFromAD` | Export profile pictures from Azure Active Directory / Entra ID |
| `Import` | Import profile pictures to target ADO organisation |

---

## Tools — Detail

### T2: NodeStructureTool

**Used by**: `WorkItemsModule`, `TeamsModule`  
**Status**: ✅ **Implemented** — `NodeStructureTool.cs`, `INodeStructureTool.cs`, `NodeStructureOptions.cs`  
**Purpose**: Remap `System.AreaPath` and `System.IterationPath` values before write/import; optionally create missing nodes in the target via the ADO Classification Nodes API.

**Invocation**: Declared under `MigrationPlatform.Tools` as a keyed entry (`"NodeStructure"`). See [M2](#m2-workitemsmodule--nodestructure-tool) for the JSON schema.

**Key design rules**:
- Node creation calls are idempotent (check before POST).
- All path lookups are normalised (case-insensitive, trimmed).
- Language map overrides apply before regex mapping (so localised node names resolve correctly).
- The tool is injected as `INodeStructureTool`.

---

### T3: WorkItemTypeMappingTool

**Used by**: `WorkItemsModule`  
**Purpose**: Translate source work item type names to target names at both export time (stored in package) and import time (used for item creation).

**Invocation**: Declared under `MigrationPlatform.Tools` as a keyed entry (`"WorkItemTypeMapping"`). See [M3](#m3-workitemsmodule--workitemtypemapping-tool) for the JSON schema.

**Features**:
- Bidirectional: a map entry translates both the type name in the revision and any type-name references in link targets.
- Unmapped types pass through unchanged.
- Export-time validation: warn if a mapped target type does not exist in the target process.

---

### T4: StringManipulatorTool

**Used by**: `WorkItemsModule`  
**Status**: ✅ **Covered by `FieldTransformTool`** — `RegexFieldTransform` and 14+ other transform types in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/FieldTransform/` provide equivalent and broader functionality.  
**Purpose**: Apply regex-based find-and-replace rules to string field values (e.g. stripping legacy prefixes, sanitising invalid characters).

**Invocation**: Declared under `MigrationPlatform.Tools` as a keyed entry.

```json
{
  "MigrationPlatform": {
    "Tools": {
      "StringManipulator": {
        "rules": [
          { "type": "RegexReplace", "field": "System.Title", "pattern": "^\\[LEGACY\\]\\s*", "replacement": "" },
          { "type": "StripControlCharacters", "field": "System.Description" }
        ]
      }
    }
  }
}
```

---

### T6: ChangesetMappingTool

**Used by**: `WorkItemsModule` (link rewriting for TFVC changeset links)  
**Purpose**: Map TFS changeset IDs to Git commit SHAs so that `Fixed In Changeset` links survive migration.

**Invocation**: Declared under `MigrationPlatform.Tools` as a keyed entry.

```json
{
  "MigrationPlatform": {
    "Tools": {
      "ChangesetMapping": {
        "mappingFile": "changeset-to-commit.json"
      }
    }
  }
}
```

The mapping file is a flat `{ "12345": "abc123def456..." }` JSON dictionary produced either manually or by a future `discovery changeset-map` command.

---

### T7: WorkItemResolutionTool

**Used by**: `WorkItemsModule` (import path)  
**Status**: 🆕 🔶 Partially implemented — `IWorkItemResolutionStrategy` and `IWorkItemResolutionStrategyFactory` exist in `Abstractions.Agent/Import/`; full configurable tool with per-type overrides not yet implemented  
**Priority**: High

**Purpose**: Determine how the import path decides whether a work item already exists in the target before creating or updating it. Different work item types may require different resolution strategies — for example, standard types can use a `ReflectedWorkItemId` custom field, while types that cannot carry custom fields (e.g. `Shared Steps`, `Shared Parameter`) need a fallback such as a hyperlink, a title+type match, or a dedicated field on their inherited type.

**The problem in detail**:

The ADO process model allows customisation only of *inherited* work item types. System-locked types like `Shared Steps` and `Shared Parameter` cannot have custom fields added without first creating a custom WIT that inherits from them. In many target organisations this step has not been done, leaving those types with no way to carry a `ReflectedWorkItemId` field. Without a fallback, the import either fails, skips, or duplicates those items on every run.

**Proposed config**:

```json
{
  "MigrationPlatform": {
    "Tools": {
      "WorkItemResolution": {
        "default": {
          "strategy": "ReflectedWorkItemIdField",
          "field": "Custom.ReflectedWorkItemId"
        },
        "overrides": [
          {
            "applyTo": ["Shared Steps", "Shared Parameter"],
            "strategy": "ReflectedWorkItemIdHyperlink",
            "hyperlinkComment": "ReflectedWorkItemId"
          },
          {
            "applyTo": ["Test Case"],
            "strategy": "ReflectedWorkItemIdField",
            "field": "Custom.AltReflectedId"
          }
        ]
      }
    },
    "Modules": {
      "WorkItems": {
        "Enabled": true,
        "Extensions": {
          "Revisions": { "Enabled": true }
        }
      }
    }
  }
}
```

**Resolution strategies**:

| Strategy | How it finds an existing item | Notes |
|---|---|---|
| `ReflectedWorkItemIdField` | Query target for `{field} = {sourceId}` | Default. Requires a custom field on the WIT. |
| `ReflectedWorkItemIdHyperlink` | Query target for a hyperlink whose `comment` matches a configured key and whose URL contains the source work item ID | Works on any WIT including system-locked types. Hyperlinks are stored as links, not fields. |
| `TitleAndTypeMatch` | Query target for work items with matching `System.Title` + `System.WorkItemType` | Fuzzy fallback only — may produce false positives if titles are not unique. |
| `AlwaysCreate` | Never searches for an existing item — always creates a new one | Use when the target is known to be empty and deduplication is not required. |
| `AlwaysSkip` | Never creates or updates — logs a warning and moves on | Use to suppress a specific type entirely during import. |

**Key design rules**:
- The `default` strategy applies to all work item types not matched by any `overrides` entry.
- `overrides` entries are matched in declaration order; the first match wins.
- The resolution result (`Found`, `NotFound`, `Skipped`) is recorded in the package checkpoint so reruns do not re-query items that were already resolved.
- The tool is injected as `IWorkItemResolutionTool` and called by the import orchestrator **before** any write attempt.
- Export is unaffected — this tool is import-only.

**Interaction with `admin field install-reflected-id`** ([C4](#c4-admin-field-install-reflected-id)):  
When using `ReflectedWorkItemIdHyperlink` as the fallback, no field installation is required for the affected types. The `admin field install-reflected-id` command should log a note when it detects that a WIT is system-locked and advise the operator to configure `WorkItemResolutionTool` with a `ReflectedWorkItemIdHyperlink` override for those types.

---

## Discovery Commands — Detail


### D1: `discovery org-sync`

**Status**: ❌ Not implemented  
**Priority**: High

**Purpose**: Enumerate all projects from every org in the `organisations` roster and upsert them (with IDs) back into the config. Equivalent to `Add-ProjectsToConfig.ps1` + `Search-ProcessesWeCareAbout.ps1`.

**Behaviour**:
1. Read `organisations` from the config file.
2. For each enabled org, call `GET /_apis/projects` and `GET /_apis/work/processes`.
3. Upsert any new projects (with `id`, `name`, `enabled: true`) into the org's `projects` array.
4. Upsert any non-system processes (with `typeId`, `name`) into the org's `processes` array.
5. Write the updated config back to the same file.

**CLI syntax**:
```
devopsmigration discovery org-sync --config organisations.json
devopsmigration discovery org-sync --config organisations.json --dry-run
```

**Config additions required**: `organisations[].projects[]` already exists. Add `organisations[].processes[]` with `typeId`, `name`, `enabled`.

---

### D2: `discovery inventory` — missing artefact types

**Status**: 🔶 Partially implemented — work items and revisions are counted; all other artefact types are missing  
**Priority**: Medium

**Purpose**: Extend the existing `discovery inventory` command with additional per-project artefact counts. These are additions to the existing command, not a new command. Equivalent to the artefact coverage in `Generate-ProjectStats.ps1`.

**Additional counts to add to `discovery inventory`**:

| Artefact | API |
|---|---|
| Build/release pipelines | `GET /_apis/pipelines` |
| Test plans | `GET /_apis/testplan/plans` |
| Test suites (tree, per plan) | `GET /_apis/testplan/plans/{id}/suites?asTreeView=True` |
| Git repositories | `GET /_apis/git/repositories` |
| Shared steps | WIQL `WorkItemType = 'Shared Steps'` |
| Process base template name | `GET /_apis/projects/{id}/properties` |
| Process template name (custom) | `GET /_apis/work/processes/{typeId}` |

**Output additions**:
- New columns appended to the existing `inventory.csv` and `inventory.json` output.
- Optional Excel export: `--format csv|json|xlsx` (default `csv`).

**CLI syntax** (additions to existing flags, not a new command):
```
devopsmigration discovery inventory --config migration.json
devopsmigration discovery inventory --config migration.json --output ./reports
devopsmigration discovery inventory --config migration.json --output ./reports --format xlsx
```

---

### D3: `discovery process`

**Status**: 🆕 ❌ Not implemented  
**Priority**: Medium

**Purpose**: A new command that does two things in one pass:

1. **Export** — dumps the full process metadata (fields, picklists, WIT definitions, layouts, states) for every configured org to an output folder. Equivalent to `Generate-ProcessOutput.ps1`.
2. **Diff** — when both `source` and `target` are configured, compares the two process snapshots and produces a gap report: fields present in source but absent from target, value-map mismatches, layout differences, and WITs that cannot accept custom fields.

This is specifically useful as a **pre-migration validation step** — run it before configuring `WorkItemsModule` field maps to understand what transformations are actually needed.

**Behaviour**:
1. For each enabled org (from `organisations`), enumerate non-system processes and their WITs.
2. Fetch fields, picklists, layouts, and state definitions via the Process REST API.
3. Write raw snapshots to `--output`.
4. If both source and target are resolvable from config, produce a `process-diff.json` and `process-diff.md` report comparing:
   - Fields in source WITs that are missing from the corresponding target WITs.
   - Fields present in both but with mismatched types or picklist values.
   - States present in source that have no counterpart in target (affects `FieldValue` maps).
   - WITs that exist in source but are absent from target process (affects `WorkItemTypeMappingTool`).
   - WITs that are system-locked in target (affects `WorkItemResolutionTool` strategy choice).

**Output structure**:
```
output/
  <org-name>/
    fields.json                     ← all org-level WIT fields
    lists.json                      ← picklist summary
    lists/<id>.json                 ← picklist detail per list
    processes.json                  ← all processes
    processes/<process-name>/
      <wit.referenceName>-LAYOUT.json
      <wit.referenceName>-fields.json
      <wit.referenceName>-states.json
  process-diff.md                   ← human-readable gap report (source vs target)
  process-diff.json                 ← machine-readable gap report
```

**CLI syntax**:
```
devopsmigration discovery process --config migration.json --output ./process-audit
devopsmigration discovery process --config migration.json --output ./process-audit --diff-only
devopsmigration discovery process --config migration.json --output ./process-audit --export-only
```

**Flags**:

| Flag | Description |
|---|---|
| `--diff-only` | Skip raw export; only produce the diff report (requires both source and target in config) |
| `--export-only` | Skip diff; only export raw process metadata |
| `--output <dir>` | Override `Artefacts.WorkingDirectory` for output |

**Relationship to other features**:
- The diff report directly informs `WorkItemsModule` `FieldMappingTool` configuration ([T1](#t1-fieldmappingtool)) — it tells the operator exactly which `FieldValue` maps and `FieldToField` maps are needed.
- WIT presence gaps inform `WorkItemTypeMappingTool` configuration ([T3](#t3-workitemtypemappingtool)).
- System-locked WIT detection informs `WorkItemResolutionTool` override configuration ([T7](#t7-workitemresolutiontool)).
- The raw export can be used as input to `admin field install` ([C3](#c3-admin-field-install)) to understand which fields need installing on the target before migration begins.

---

### P5: Cloud Deployment — Ring-Based ControlPlane + Agents

**Status**: 🆕 ❌ Not implemented
**Priority**: High

**Purpose**: Deploy the ControlPlaneHost and MigrationAgent(s) to Azure Container Apps (ACA) via the Aspire AppHost and `azd up`, with three independent deployment rings mapped to subdomains of `devopsmigration.io`. Each ring is a fully isolated environment (separate ACA environment, PostgreSQL, Blob Storage). The CLI connects to a ring via `Environment.ControlPlane.BaseUrl` — a preview CLI talks to `app-preview.devopsmigration.io`, a release CLI talks to `app.devopsmigration.io`.

**Deployment rings**:

| Ring | Domain | Trigger | `azd` Environment |
|---|---|---|---|
| Canary | `app-canary.devopsmigration.io` | `build.ps1 -Mode Deploy` (developer local) | `canary` |
| Preview | `app-preview.devopsmigration.io` | CI on main merge | `preview` |
| Release | `app.devopsmigration.io` | CI on release tag | `release` |

**Key design decisions**:

- **Code-to-ring only** — every ring is built from source and deployed directly. There is no image promotion between rings; the version is baked into the assemblies at build time.
- **Full isolation per ring** — each ring has its own Azure resource group, ACA environment, PostgreSQL Flexible Server, and Blob Storage account. A preview bug cannot corrupt release data.
- **CLI is the ring selector** — the customer's config `ControlPlane.BaseUrl` determines which ring they connect to. Preview customers get a preview CLI binary (or override the URL in config). The CLI version and ControlPlane version must match.
- **MigrationAgent runs in ACA** — stateless worker containers, scaled by the ControlPlaneHost via `IAgentLauncher` (future `ContainerAgentLauncher`) or by KEDA based on job queue depth. A single container image supports all modes (Export, Prepare, Import, Migrate).
- **TfsMigrationAgent is NOT cloud-deployed** — it requires .NET Framework 4.8.1 and the TFS Object Model (SOAP/COM), which cannot run in Linux containers. The `Package` mode already produces a zip with `TfsMigrationAgent/` inside for manual deployment on a Windows machine near the TFS server. Customers may choose to deploy it on their own Windows VM or Azure VM with VPN/ExpressRoute to their TFS instance.
- **Aspire replaces Docker Compose** — the AppHost `Program.cs` defines the full service topology. `azd up` generates ACA Bicep automatically. No `docker-compose.yml` is needed. Customers who want to self-host on a plain Docker host can use `azd config set platform.type compose` to generate one from the Aspire manifest.

**Infrastructure per ring** (provisioned by `azd up`):

```
rg-devopsmigration-{ring}/
  ├── Azure Container Apps Environment
  │   ├── controlplane (container app, external ingress)
  │   └── migration-agent (container app, N replicas, internal only)
  ├── Azure PostgreSQL Flexible Server
  │   └── controlplane-db
  ├── Azure Storage Account
  │   └── packages (blob container)
  └── Azure Container Registry
```

**build.ps1 additions**:

- New mode: `Deploy` — Build + Test + `azd up` to the target ring.
- New parameter: `-Ring canary|preview|release` (defaults to `canary` for Deploy mode).
- `azd up` handles container building, registry push, and ACA deployment from the Aspire manifest — no manual `docker build`/`docker push` required.

**Ring configuration** (in build.ps1):

| Ring | Agent Replicas | Notes |
|---|---|---|
| Canary | 1 | Developer testing; minimal resources |
| Preview | 1 | Preview customers; early access to main builds |
| Release | 2+ | Production customers; higher availability |

**CI/CD pipeline flow**:

| Trigger | Command |
|---|---|
| Developer local | `pwsh ./build.ps1 -Mode Deploy` (defaults to canary) |
| Main merge | `pwsh ./build.ps1 -Mode Deploy -Ring preview` |
| Release tag | `pwsh ./build.ps1 -Mode Deploy -Ring release` |

**Prerequisites**:

1. `azure.yaml` at repo root (Aspire `azd` project definition)
2. Dockerfiles for ControlPlaneHost and MigrationAgent
3. One-time `azd env new` for each ring with `AZURE_LOCATION` and `AZURE_RESOURCE_GROUP`
4. DNS records: `app-canary.`, `app-preview.`, `app.` → ACA ingress IPs
5. Wildcard TLS certificate for `*.devopsmigration.io` (or per-subdomain certs via ACA managed certificates)

**Files to create**:

| File | Purpose |
|---|---|
| `azure.yaml` | `azd` project definition mapping services to AppHost projects |
| `src/DevOpsMigrationPlatform.ControlPlaneHost/Dockerfile` | Multi-stage build for ControlPlaneHost container |
| `src/DevOpsMigrationPlatform.MigrationAgent/Dockerfile` | Multi-stage build for MigrationAgent container |

**Relationship to existing architecture**:

- The AppHost `Program.cs` already has `portable` and `docker` subprofiles. `azd up` uses the Aspire manifest generated from the `docker` subprofile's Azure resource declarations (`AddAzurePostgresFlexibleServer`, `AddAzureStorage`).
- `ControlPlaneHost` already detects cloud mode (ACA/KEDA manages agents) and idles `AgentLifecycleService`.
- The `IAgentLauncher` abstraction (documented in `docs/control-plane.md`) anticipates `ContainerAgentLauncher` for deploying and scaling agent containers to a configurable ACA environment.
- Package storage uses `IArtefactStore` with `AzureBlobArtefactStore` in cloud mode — same `BlobContainerClient` code validated locally by the `dev-docker` subprofile via Azurite.

---

## CLI Features — Detail

### C1: `config generate`

**Status**: ❌ Not implemented  
**Priority**: High

**Purpose**: Stamp a library of scenario-config templates with per-org/per-project source values and write one populated config file per project. Equivalent to `Generate-ConfigurationsFromTemplates.ps1`.

**Behaviour**:
1. Read `organisations` from a roster config (multi-org, using the `organisations` array).
2. For each enabled org, iterate each enabled project.
3. For each template file in `--templates <dir>`, produce a populated copy under `--output <dir>/<org>/<project>/<template-name>.json`, substituting `source.orgOrCollection`, `source.project`, and `source.authentication.accessToken`.
4. Target values (org, project, PAT) are left as template placeholders or taken from a separate `--target` config flag.

**CLI syntax**:
```
devopsmigration config generate --config organisations.json --templates ./templates --output ./generated-configs
devopsmigration config generate --config organisations.json --templates ./templates --output ./generated-configs --dry-run
```

**Config additions required**: None — uses existing `organisations[]` schema. Template files are standard scenario JSON configs with `{{source.orgOrCollection}}` / `{{source.project}}` substitution tokens.

---

### C2: `queue --batch`

**Status**: ❌ Not implemented  
**Priority**: Medium

**Purpose**: Queue multiple migration jobs from a folder of generated scenario configs, submitting each to the control plane in sequence (or in parallel up to `--max-concurrent`). Equivalent to iterating over the output of `config generate` and calling `devopsmigration queue` for each.

**CLI syntax**:
```
devopsmigration queue --batch ./generated-configs/myorg --follow
devopsmigration queue --batch ./generated-configs/myorg --max-concurrent 3
devopsmigration queue --batch ./generated-configs/myorg --filter "*.workItems.json"
```

**Notes**:
- Each config in the batch is submitted as an independent `MigrationJob`.
- `--follow` streams all job logs to a single multiplexed output.
- `--max-concurrent` controls how many jobs run simultaneously (default: 1 — sequential).
- The batch command itself has no migration logic; it is a loop over `QueueCommand`.

---

### C3: `admin field install`

**Status**: ❌ Not implemented  
**Priority**: High

**Purpose**: Idempotently install custom fields (with picklists and form controls) across all configured orgs and processes. Equivalent to `Install-CustomFields.ps1`.

**CLI syntax**:
```
devopsmigration admin field install --config migration.json --fields ./fields
devopsmigration admin field install --config migration.json --fields ./fields --dry-run
devopsmigration admin field install --config migration.json --fields ./fields --validate-only
```

**Field definition file** (`fields/<refname>.json`):
```json
{
  "referenceName": "Custom.MyField",
  "createFieldPOST":   { ... },
  "addFieldPOST":      { ... },
  "createPicklistPOST": { ... },
  "createControlPOST": { ... },
  "defaultGroupLabel": "Details"
}
```

**Fields manifest** (`fields.json`):
```json
{
  "fields": [
    { "refname": "Custom.MyField", "enabled": true },
    { "refname": "Custom.OtherField", "enabled": false }
  ]
}
```

**Idempotency rules**:
1. Check if the field exists at org level — create if absent.
2. Check if the picklist exists — create or update items if needed.
3. Check if the field is on the WIT in the process — add if absent.
4. Check if the control is in the target group on the layout — add if absent.
5. Validate `referenceName` consistency across all POST bodies before any API call.

---

### C4: `admin field install-reflected-id`

**Status**: ❌ Not implemented  
**Priority**: High

**Purpose**: Install `Custom.ReflectedWorkItemId` across all configured orgs, all non-system processes, and all WITs (creating custom WIT inheritors for system WITs as needed). Equivalent to `Install-ReflectedWorkItemID.ps1`.

**CLI syntax**:
```
devopsmigration admin field install-reflected-id --config migration.json
devopsmigration admin field install-reflected-id --config migration.json --dry-run
```

**Field definition** read from a standard field definition file (same format as `C3`), defaulting to the bundled `ReflectedWorkItemId.json` definition. Accepts `--field-file <path>` to override.

---

### C5: `admin field delete`

**Status**: ❌ Not implemented  
**Priority**: Low

**Purpose**: Delete a field by reference name from all enabled orgs. Equivalent to `Delete-CustomField.ps1`.

**CLI syntax**:
```
devopsmigration admin field delete --config migration.json --field Custom.MyField
devopsmigration admin field delete --config migration.json --field Custom.MyField --dry-run
```

---

### C6: `admin page install`

**Status**: ❌ Not implemented  
**Priority**: Medium

**Purpose**: Idempotently add pages and groups to WIT form layouts across all configured orgs and processes. Equivalent to `Install-CustomPages.ps1`.

**CLI syntax**:
```
devopsmigration admin page install --config migration.json --pages ./pages
devopsmigration admin page install --config migration.json --pages ./pages --dry-run
```

**Page definition file** (`pages/<name>.json`):
```json
{
  "name": "Portfolio",
  "enabled": true,
  "workItemTypes": ["Epic", "Feature"],
  "PagePOST": {
    "label": "Portfolio",
    "sections": [
      {
        "id": "Section1",
        "groups": [ { "label": "Strategic Context", "controls": [...] } ]
      }
    ]
  }
}
```

**Idempotency rules**:
1. Enumerate all non-system processes and WITs.
2. Auto-create a custom WIT inheritor if the system WIT cannot be modified directly.
3. Add the page if it doesn't exist; otherwise proceed to group check.
4. Add missing groups to the correct section; skip groups already present.

---

## Platform Features — Detail

### P1: Checkpoint Reconciliation

**Current state**: If a checkpoint cursor file (`.migration/Checkpoints/*.cursor.json`) or continuation token (`.continuation.json`) is lost, corrupted, or never written (e.g. crash before first checkpoint), the system restarts all work from scratch — even when the package already contains complete data from prior runs.

**Why it matters**: Multi-hour discovery and export runs (e.g. 15k dependency records, 50k+ work items) become unbearably expensive when a missing cursor forces a full re-run. The data proves what was completed; the cursor is just a summary of that data.

**Proposed solution**: A `Reconcile` job type executed by the Migration Agent. The CLI sends `devopsmigration reconcile --config migration.json` → Control Plane creates a `MigrationJob { Type = "Reconcile" }` → Agent scans package data vs checkpoint state → rebuilds missing checkpoints.

#### Reconciliation Logic Per Module

| Module | Data Signal | Reconciled Checkpoint |
|--------|------------|----------------------|
| **Dependencies** | Parse `dependencies.csv` → extract unique `SourceOrganisationUrl\|SourceProject` pairs | `Dependencies.cursor.json` with `completedProjects[]` and `recordCount` |
| **Inventory** | Presence of `inventory.json` / `inventory.csv` | `inventory.cursor.json` |
| **WorkItems (import)** | Enumerate `WorkItems/` → find lexicographically last revision folder | `workitems.cursor.json` with `lastProcessed` = last folder, `stage` = `Completed` |
| **WorkItems (export/discovery)** | Scan latest revision folder → extract ChangedDate and WorkItemId from `revision.json` | `*.continuation.json` (best-effort — fingerprint set to empty, caller must accept or reject) |
| **Teams, Permissions, Builds, Git, Identities** | Last folder under each module prefix | Per-module `*.cursor.json` |

#### Architectural Constraints

- **Data residency**: Reconciliation runs on the Agent (not CLI) because only the Agent has write access to the package.
- **Conservative by default**: For continuation tokens where the query fingerprint cannot be reconstructed, the reconciled token uses an empty fingerprint. On next resume, `EvaluateResumeDecisionAsync` will return `RejectedQueryMismatch` and the caller can choose fresh-start or accept.
- **Idempotent**: Running reconcile when checkpoints already exist and are valid is a no-op.
- **Observable**: Each reconciliation emits a warning log with the reconstructed state so operators can audit what was inferred.

#### Standalone Automatic Reconciliation (Implemented)

As an interim measure before the full `Reconcile` job is built, individual modules can perform **automatic reconciliation on startup**: if the cursor is missing but data exists, reconstruct the cursor from the data and log a warning. This is implemented for:
- `DependencyDiscoveryModule` — parses existing `dependencies.csv` to rebuild `completedProjects` set

---

### P4: Operator Interaction / Pending Questions

**Current state**: If a migration job encounters an ambiguous or unrecoverable situation at runtime (e.g., a field type mismatch that prepare-time validation did not catch, an unexpected value not in the mapping table, a permissions error on a subset of items), the only options are fail-fast or silently skip. There is no mechanism for the job to ask the operator a question and wait for a response.

**Why it matters**: Fail-fast is too aggressive for long-running migrations — the operator may lose hours of work. Silent skip hides problems. The operator should be able to make an informed decision at runtime without aborting the entire job.

**Proposed solution**:

1. **Agent pauses**: When a module or tool encounters a situation requiring operator input, it raises a `PendingQuestion` through a platform service (e.g., `IOperatorInteractionService`). The Agent pauses the current job and transitions to `Pending` state on the Control Plane.
2. **Control Plane queues the question**: The question (with context, options, and a timeout) is stored as part of the job state. The job remains in `Pending` until answered or timed out.
3. **Operator connects**: The operator can connect via TUI or re-attach the CLI in follow mode (`devopsmigration follow --job <id>`) to the same job. The UI presents the pending question with available choices.
4. **Answer flows back**: The operator's answer is recorded on the Control Plane and delivered to the Agent, which resumes processing with the chosen action.
5. **Timeout behaviour**: If no answer is provided within a configurable timeout (default: 1 hour), the Agent applies a default action (e.g., skip the item and continue) or fails the job, depending on the question's severity.

**Consumers in this proposal**:
- **FieldTransformTool (this spec)**: Runtime transform errors could present "Skip this item / Abort / Retry" choices.
- **General use**: Any module or tool can raise a `PendingQuestion` — this is a platform-level capability, not specific to field transforms.

**Architectural constraints**:
- The Agent MUST NOT block indefinitely — timeout with configurable default is mandatory.
- Questions and answers MUST be recorded in the job log for auditability.
- The Control Plane is the mediator — Agent and CLI/TUI never communicate directly about questions.

> **Note**: This is a separate platform feature (P4). The FieldTransformTool spec references it as a future integration point but does not depend on it for v1. V1 uses fail-fast on transform errors.

| Field | Location | Purpose |
|---|---|---|
| `MigrationPlatform.Tools.<ToolTypeName>` | MigrationPlatform config root | Keyed-object tool declarations (key = tool type name, e.g. `NodeStructure`) |
| `MigrationPlatform.Modules.<Module>.Extensions.<Extension>` | Extension config | Extension-level config; no per-extension tool reference array |
| `organisations[].processes[]` | Config root | Process metadata discovered by `discovery org-sync` |
| `organisations[].projects[].enabled` | Config root | Already in schema; documented as relevant to `config generate` |
| `scopes[].type: "ids"` | Module scopes | New scope type for explicit work item ID list |
| `modules[].collapseRevisions` | WorkItems module | Boolean option |
| `modules[].maxRevisions` | WorkItems module | Integer option |
| `modules[].maxGracefulFailures` | WorkItems module | Integer option |
| `modules[].generateMigrationComment` | WorkItems module | Boolean option |
| `extensions[Attachments].maxSizeBytes` | WorkItems/Attachments extension | Integer option |
