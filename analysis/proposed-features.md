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
| **Tool** | A shared, cross-cutting service injected into one or more modules (e.g. `FieldMappingTool`, `NodeStructureTool`). Declared at the module or config level. Not a CLI command. |
| **Scope** | A mandatory selection criterion on a module (e.g. `wiql` query, project name). |
| **Discovery command** | A `discovery *` CLI sub-command that runs locally without submitting a `MigrationJob`. Reads source systems directly via REST. |
| **CLI feature** | A `queue`, `config`, `manage`, or `admin` command addition. |
| **Config feature** | A new field or schema addition to the scenario JSON config. |

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
| M1 | [WorkItemsModule — FieldMapping extensions](#m1-workitemsmodule--fieldmapping) | 🔶 Missing field-map engine | 14 field-map types required for cross-process-template migrations |
| M2 | [WorkItemsModule — NodeStructure tool](#m2-workitemsmodule--nodestructure-tool) | ❌ | Area/iteration path mapping, creation, and language override |
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
| T1 | [FieldMappingTool](#t1-fieldmappingtool) | ❌ | 14 field-map types injected into WorkItemsModule |
| T2 | [NodeStructureTool](#t2-nodestructuretool) | ❌ | Area/iteration path regex mapping + auto-creation |
| T3 | [WorkItemTypeMappingTool](#t3-workitemtypemappingtool) | ❌ | Work item type name remapping table |
| T4 | [StringManipulatorTool](#t4-stringmanipulatortool) | ❌ | Regex-based field string cleanup |
| T5 | [GitRepositoryMappingTool](#t5-gitrepositorymappingtool) | ❌ | Source→target repo name mapping for GitModule and link rewriting |
| T6 | [ChangesetMappingTool](#t6-changesetmappingtool) | ❌ | TFS changeset → Git commit SHA mapping |
| T7 | [WorkItemResolutionTool](#t7-workitemresolutiontool) | 🆕 ❌ | Per-work-item-type strategy for finding existing items in the target (field, hyperlink, or title match); handles types that cannot have custom fields (e.g. Shared Steps) |

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
| P1 | [Checkpoint Reconciliation](#p1-checkpoint-reconciliation) | 🆕 ❌ | Rebuild missing/corrupted checkpoint state from existing package data across all modules |
| P2 | [Three-Channel Telemetry Model](#p2-three-channel-telemetry-model) | 🆕 ❌ | Rationalise Events, Metrics, and Snapshot into distinct channels with correct layering |

---

## Modules — Detail

### M1: WorkItemsModule — FieldMapping

**Current state**: The module copies fields as opaque values. Identity fields are mapped by `IIdentityMappingService`. No general field transformation exists.

**Why it matters**: Any migration between organisations with different process templates (e.g. Agile source → Scrum target) requires value translation on State, Priority, and custom fields before import. Without this, work items import with invalid or missing field values.

**Proposed additions**:

#### New Tool: `FieldMappingTool` (see [T1](#t1-fieldmappingtool))

Declared in the module config's `tools` array. The module passes each revision's fields through the tool before writing `revision.json`.

```json
{
  "name": "WorkItems",
  "tools": [
    {
      "type": "FieldMapping",
      "applyTo": ["User Story", "Bug"],
      "maps": [
        { "type": "FieldToField",    "sourceField": "Custom.OldField", "targetField": "Custom.NewField" },
        { "type": "FieldValue",      "field": "System.State", "valueMap": { "Active": "In Progress", "Resolved": "Done" } },
        { "type": "FieldLiteral",    "field": "Custom.MigratedBy", "value": "migration-platform" },
        { "type": "FieldToTag",      "field": "System.AreaPath" },
        { "type": "FieldValueToTag", "field": "System.State", "pattern": "^Resolved$" },
        { "type": "RegexField",      "field": "System.Title", "pattern": "^\\[OLD\\]\\s*", "replacement": "" },
        { "type": "FieldClear",      "field": "Custom.LegacyId" },
        { "type": "FieldSkip",       "field": "Custom.InternalOnly" },
        { "type": "FieldMerge",      "sourceFields": ["System.Title", "Custom.Subtitle"], "targetField": "System.Title", "format": "{0} — {1}" },
        { "type": "FieldCalculation","targetField": "Custom.Score", "expression": "..." },
        { "type": "FieldToTagField", "sourceFields": ["System.Tags", "Custom.Labels"], "targetField": "System.Tags" },
        { "type": "TreeToTagField",  "field": "System.AreaPath", "targetField": "System.Tags" },
        { "type": "MultiValueConditional", "conditions": [...], "targetField": "...", "value": "..." },
        { "type": "FieldToFieldMulti", "maps": [ { "sourceField": "...", "targetField": "..." } ] }
      ]
    }
  ]
}
```

**Map types to implement** (14 total):

| Map Type | Purpose |
|---|---|
| `FieldToField` | Copy field A → field B with optional default |
| `FieldToFieldMulti` | Multiple source→target field copy pairs |
| `FieldLiteral` | Set field to a literal value |
| `FieldValue` | Dictionary-based value remapping (e.g. State) |
| `FieldMerge` | Merge multiple source fields into one with format string |
| `FieldCalculation` | Compute field from an expression |
| `FieldClear` | Null-out a field |
| `FieldSkip` | Exclude field from the written revision (not imported) |
| `FieldValueToTag` | Append to `System.Tags` when field value matches a pattern |
| `FieldToTag` | Append field value to `System.Tags` |
| `FieldToTagField` | Merge multiple field values into a tag-style target field |
| `MultiValueConditional` | Conditional multi-field → single field mapping |
| `RegexField` | Regex find-and-replace within a field value |
| `TreeToTagField` | Convert area/iteration tree path into a tag |

**Per-map `applyTo` filter** — each map entry (or the tool itself) accepts an optional `applyTo` array of work item type names to restrict application.

---

### M2: WorkItemsModule — NodeStructure Tool

**Current state**: `System.AreaPath` and `System.IterationPath` are written verbatim into `revision.json`. On import, if those paths don't exist in the target, the import fails or the revision lands in the root.

**Why it matters**: Almost every migration involves renaming or restructuring area/iteration trees. Without this, imports to a different project hierarchy fail silently or require manual remediation.

**Proposed additions**:

#### New Tool: `NodeStructureTool` (see [T2](#t2-nodestructuretool))

```json
{
  "name": "WorkItems",
  "tools": [
    {
      "type": "NodeStructure",
      "areaMap":      { "OldOrg\\OldProject\\Team A": "NewOrg\\NewProject\\Team A - Migrated" },
      "iterationMap": { "OldOrg\\OldProject\\Sprint 1": "NewOrg\\NewProject\\Sprint 1" },
      "areaLanguageOverride":      "Area",
      "iterationLanguageOverride": "Iteration",
      "createMissingNodes": true,
      "skipRevisionWithInvalidAreaPath": false,
      "skipRevisionWithInvalidIterationPath": false,
      "replicateAllExistingNodes": false
    }
  ]
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
  "name": "WorkItems",
  "tools": [
    {
      "type": "WorkItemTypeMapping",
      "map": {
        "User Story":    "Product Backlog Item",
        "Issue":         "Impediment"
      }
    }
  ]
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

### T1: FieldMappingTool

**Used by**: `WorkItemsModule`  
**Purpose**: Apply a declared set of field transformation rules to each work item revision before it is written to the package (export) or applied to the target (import).

**Invocation**: Declared in the `tools` array of the `WorkItems` module config. See [M1](#m1-workitemsmodule--fieldmapping) for the full map type list and JSON schema.

**Key design rules**:
- Maps are applied in declaration order.
- `FieldSkip` maps remove the field from the revision before any write — they are not import-only.
- All maps respect the `applyTo` work item type filter.
- Map processing is a pure transformation (no I/O). The tool is injected as `IFieldMappingTool` and receives a `WorkItemRevision` value object; it returns a transformed copy.

---

### T2: NodeStructureTool

**Used by**: `WorkItemsModule`, `TeamsModule`  
**Purpose**: Remap `System.AreaPath` and `System.IterationPath` values before write/import; optionally create missing nodes in the target via the ADO Classification Nodes API.

**Invocation**: Declared in the `tools` array of the relevant module config. See [M2](#m2-workitemsmodule--nodestructure-tool) for the JSON schema.

**Key design rules**:
- Node creation calls are idempotent (check before POST).
- All path lookups are normalised (case-insensitive, trimmed).
- Language map overrides apply before regex mapping (so localised node names resolve correctly).
- The tool is injected as `INodeStructureTool`.

---

### T3: WorkItemTypeMappingTool

**Used by**: `WorkItemsModule`  
**Purpose**: Translate source work item type names to target names at both export time (stored in package) and import time (used for item creation).

**Invocation**: Declared in the `tools` array of the `WorkItems` module config. See [M3](#m3-workitemsmodule--workitemtypemapping-tool) for the JSON schema.

**Features**:
- Bidirectional: a map entry translates both the type name in the revision and any type-name references in link targets.
- Unmapped types pass through unchanged.
- Export-time validation: warn if a mapped target type does not exist in the target process.

---

### T4: StringManipulatorTool

**Used by**: `WorkItemsModule`  
**Purpose**: Apply regex-based find-and-replace rules to string field values (e.g. stripping legacy prefixes, sanitising invalid characters).

**Invocation**: Declared in the `tools` array of the `WorkItems` module config.

```json
{
  "type": "StringManipulator",
  "applyTo": ["System.Title", "System.Description"],
  "rules": [
    { "pattern": "^\\[LEGACY\\]\\s*",    "replacement": "" },
    { "pattern": "[\\x00-\\x08\\x0B\\x0C\\x0E-\\x1F]", "replacement": "" }
  ]
}
```

**Features**:
- `applyTo` field list (applies to all string fields if omitted).
- Multiple ordered rules per invocation.
- Invalid character cleanup as a named built-in rule: `{ "type": "StripControlCharacters" }`.
- The tool is injected as `IStringManipulatorTool`.

---

### T5: GitRepositoryMappingTool

**Used by**: `GitModule`, `WorkItemsModule` (link rewriting in `EmbeddedImages` and `Links` extensions)  
**Purpose**: Map source repository names to target repository names; rewrite embedded git:// or PR URLs in work item HTML fields.

**Invocation**: Declared in the `tools` array of `GitModule` and/or `WorkItems` module config.

```json
{
  "type": "GitRepositoryMapping",
  "map": {
    "OldRepoName":    "NewRepoName",
    "LegacyService":  "platform-service"
  }
}
```

---

### T6: ChangesetMappingTool

**Used by**: `WorkItemsModule` (link rewriting for TFVC changeset links)  
**Purpose**: Map TFS changeset IDs to Git commit SHAs so that `Fixed In Changeset` links survive migration.

**Invocation**: Declared in the `tools` array of `WorkItems` module config.

```json
{
  "type": "ChangesetMapping",
  "mappingFile": "changeset-to-commit.json"
}
```

The mapping file is a flat `{ "12345": "abc123def456..." }` JSON dictionary produced either manually or by a future `discovery changeset-map` command.

---

### T7: WorkItemResolutionTool

**Used by**: `WorkItemsModule` (import path)  
**Status**: 🆕 ❌ Not implemented  
**Priority**: High

**Purpose**: Determine how the import path decides whether a work item already exists in the target before creating or updating it. Different work item types may require different resolution strategies — for example, standard types can use a `ReflectedWorkItemId` custom field, while types that cannot carry custom fields (e.g. `Shared Steps`, `Shared Parameter`) need a fallback such as a hyperlink, a title+type match, or a dedicated field on their inherited type.

**The problem in detail**:

The ADO process model allows customisation only of *inherited* work item types. System-locked types like `Shared Steps` and `Shared Parameter` cannot have custom fields added without first creating a custom WIT that inherits from them. In many target organisations this step has not been done, leaving those types with no way to carry a `ReflectedWorkItemId` field. Without a fallback, the import either fails, skips, or duplicates those items on every run.

**Proposed config**:

```json
{
  "name": "WorkItems",
  "tools": [
    {
      "type": "WorkItemResolution",
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
  ]
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

### P2: Three-Channel Telemetry Model

**Current state**: The platform conflates three distinct concerns in two channels:
- `ProgressEvent` carries envelope fields (`Module`, `Stage`, `Message`, `Timestamp`) **and** per-event counter fields (`TotalWorkItems`, `WorkItemsProcessed`, `ExternalLinksFound`, etc.). Counter fields are accumulated by the TUI from raw events — wrong layer.
- `MetricSnapshot` and `DiscoveryMetricSnapshot` are parallel flat types with no shared structure. `DiscoveryMetricSnapshot` is assembled in `TuiMainView` from accumulated event fields — it belongs in the agent.
- Late-joining clients (connecting after a job has been running) have no way to catch up: the SSE stream does not replay history. The workaround is embedding a `MetricSnapshot` inside `ProgressEvent.Metrics` as a transport hack.

**Why it matters**: The TUI accumulates state it shouldn't own, making it fragile and incorrect on reconnect. The agent is not the authoritative source for its own counters. Adding new counter types requires changes in both the agent and the TUI. Late-joining clients get a stale or empty display until the next event arrives.

**Proposed solution**: Three explicitly separate types — **`JobEvent`**, **`JobMetrics`**, **`JobSnapshot`** — each with a single, non-overlapping responsibility:

| Type | OTel analogy | Frequency | Size | Direction |
|------|-------------|-----------|------|-----------|
| `JobEvent` | OTel Event | Every state change | Tiny | Agent → SSE fan-out → client |
| `JobMetrics` | OTel Metrics | Every few seconds (timer) | Small | Agent push → Control Plane stores latest → client polls |
| `JobSnapshot` | — | Every 5 min or at project boundary | Large | Agent push → Control Plane stores latest → client fetches on connect |

#### Why both JobMetrics and JobSnapshot?

`JobMetrics` provides **high-frequency aggregate totals** (every few seconds). `JobSnapshot` provides **per-project detail** (every 5 minutes or at project boundaries).

**Concrete scenario**: During a 2-hour dependency analysis of a single 50,000-item project, `JobSnapshot` does not update until the project completes — there is only one project, so there is no project boundary to trigger a push. `JobMetrics` updates every 5 seconds with the in-progress `WorkItemsAnalysed` counter. Without `JobMetrics`, the TUI would show a frozen progress bar for 2 hours.

A client cannot derive one from the other: `JobMetrics` has no per-project detail, and `JobSnapshot` updates too infrequently for live progress.

---

#### Channel 1: JobEvent

`POST /lease/{id}/progress` → fan-out via `GET /jobs/{id}/events` (SSE)

**Purpose**: real-time notification of state changes. Maps to an OTel Event — something happened at a point in time.

- `JobEvent` (`ProgressEvent`) becomes a **pure envelope**: `Module`, `Stage`, `Message`, `Timestamp`, `EventSequence` only.
- No counter fields. No `LastProcessed`. No embedded data payloads (except the TFS subprocess constraint — see below).
- The TUI uses events to update the live log panel. It never accumulates counters from events.

**Event sequence number**: Every `JobEvent` carries a monotonic `EventSequence` (`long`), assigned by the agent and scoped per job. The SSE stream includes the sequence as the `id:` field, enabling standard `Last-Event-ID` reconnect semantics — a client that reconnects sends the last sequence it received and the server replays missed events from the ring buffer. Client-side reducers use `EventSequence` for idempotency: if a received sequence is ≤ the last applied sequence, the event is discarded.

**Late-joining clients**: A client connecting mid-job calls `GET /jobs/{id}/bootstrap` (see [Unified Bootstrap Endpoint](#unified-bootstrap-endpoint) below) to obtain the current `JobSnapshot`, `JobMetrics`, and `LastEventSequence` in a single atomic response. It then subscribes to the SSE stream with `Last-Event-ID: {LastEventSequence}` to receive only events it missed. The ring buffer remains for recent log replay but no longer carries per-project state.

---

#### Channel 2: JobMetrics

`POST /lease/{id}/metrics` → stored by `JobMetricsStore`; polled via `GET /jobs/{id}/metrics`

**Purpose**: aggregate counters for dashboards and OTel metrics export. Maps to OTel Metrics — what is the current count.

- Agent pushes a `JobMetrics` record on a background timer (`ControlPlaneTelemetryTimer`). Frequency: every few seconds.
- Aggregates across all orgs and projects — totals only, no per-project breakdown.
- Raw counters are the **agent's responsibility**. The CLI/TUI only derives display values (percentages, rates) — it never accumulates from events.

```
JobMetrics
├── Scope: JobScopeCounters              // shared by all job types
│   ├── OrganisationsTotal/Completed/Failed
│   ├── ProjectsTotal/Completed/Failed
│   └── WorkItemsTotal
│
├── Migration: MigrationCounters?        // null for discovery jobs
│   ├── WorkItems: WorkItemCounters
│   │   ├── Attempted/Completed/Failed/Skipped
│   │   ├── RevisionsProcessed
│   │   └── Attachments: AttachmentCounters?
│   │       └── Processed/Failed/TotalBytes
│   └── Diagnostics: MigrationDiagnostics?
│       ├── WorkItemDurationMeanMs
│       ├── FieldCountMean
│       ├── AttachmentCountMean
│       ├── LinkCountMean
│       ├── RevisionCountMean
│       ├── PayloadBytesMean
│       ├── RevisionsMissing
│       ├── RevisionOrderErrors
│       ├── BrokenLinks
│       ├── MissingWorkItems
│       ├── WorkItemsInFlight
│       └── QueueDepth
│
└── Discovery: DiscoveryCounters?        // null for migration jobs
    ├── Inventory: InventoryCounters?
    │   ├── RevisionsTotal
    │   ├── RepositoriesTotal
    │   └── CheckpointsSaved
    └── Dependencies: DependencyCounters?
        ├── WorkItemsAnalysed
        ├── ExternalLinksFound
        ├── CrossProjectLinks
        ├── CrossOrgLinks
        └── CheckpointsSaved
```

`MigrationDiagnostics` captures the OTel-derived mean values and correctness counters from the current `MetricSnapshot` (duration means, revision deltas, in-flight gauges). These are aggregate-only — they do not appear per-project. Future modules (Teams, Git, TestManagement) add their own nullable counter types to `MigrationCounters` without breaking existing clients.

---

#### Channel 3: JobSnapshot

`POST /lease/{id}/snapshot` → stored by `JobSnapshotStore`; retrieved via `GET /jobs/{id}/snapshot`

**Purpose**: full per-org, per-project state for late-joining clients.

- Agent pushes a `JobSnapshot` every **5 minutes** (configurable timer) **or** at project boundaries (whichever comes first).
- The Control Plane stores only the **latest** `JobSnapshot` per job — overwrite, no history.
- A client connecting **mid-job** calls `GET /jobs/{id}/snapshot` once on connect to populate the full org/project table, then uses `JobEvent` for log updates and polls `JobMetrics` for live aggregate counters.
- This replaces the `JobProgressStore.LatestByProject` mechanism — the snapshot is the authoritative source for per-project state.

`JobSnapshot` is structured as orgs → projects. The counter types (`MigrationCounters`, `DiscoveryCounters`) are **shared with `JobMetrics`** — same record types, different scope (per-project here vs. aggregate in `JobMetrics`).

```
JobSnapshot
└── Organisations: OrgSnapshot[]
    ├── Url: string
    ├── Name: string
    └── Projects: ProjectSnapshot[]
        ├── Name: string
        ├── Status: ProjectStatus        // Pending | InProgress | Completed | Failed
        ├── Migration: MigrationCounters?  // same type as JobMetrics.Migration (excl. Diagnostics), scoped to this project
        └── Discovery: DiscoveryCounters?  // same type as JobMetrics.Discovery, scoped to this project
```

**Job type examples**:
- **Migration job**: one `OrgSnapshot`, one `ProjectSnapshot`. `Migration` populated, `Discovery` null. The per-project `MigrationCounters` are always equal to `JobMetrics.Migration` (there is only one project).
- **Discovery job**: one or more `OrgSnapshot`s, many `ProjectSnapshot`s. `Discovery` populated, `Migration` null. A project may have `Inventory` populated with final values while `Dependencies` is still null or mid-flight — this is correct, as inventory runs before dependency analysis.

**Note on `MigrationDiagnostics`**: The `Diagnostics` sub-record is **not included in `ProjectSnapshot.Migration`** — it is aggregate-only (OTel means and in-flight gauges are not meaningful at per-project scope). `ProjectSnapshot` uses `MigrationCounters` but its `Diagnostics` property is always null at the per-project level.

The `allProjectStats` dictionary already maintained by `DependencyDiscoveryModule` (and equivalent per-project state in inventory/migration modules) feeds directly into the `ProjectSnapshot` list.

---

#### Unified Bootstrap Endpoint

`GET /jobs/{id}/bootstrap`

**Purpose**: Eliminate the race condition inherent in making three separate calls (`GET /snapshot`, `GET /metrics`, `GET /events`) on client connect. A single atomic response provides everything a late-joining client needs to render the full UI.

**Response**:
```json
{
  "snapshot": { /* JobSnapshot */ },
  "metrics":  { /* JobMetrics  */ },
  "lastEventSequence": 14207
}
```

**Behaviour**:
- The Control Plane reads the latest `JobSnapshot`, the latest `JobMetrics`, and the current maximum `EventSequence` under a single lock/snapshot.
- The client uses `snapshot` to populate the org/project table, `metrics` to populate aggregate counters, and `lastEventSequence` as the `Last-Event-ID` when subscribing to the SSE stream.
- If the job has not yet emitted a snapshot (e.g. it just started), `snapshot` is `null` and `lastEventSequence` is `0`.
- Individual `GET /jobs/{id}/snapshot` and `GET /jobs/{id}/metrics` endpoints remain available for polling refreshes after the initial bootstrap.

---

#### Cardinality Guardrails

**Design rules** to prevent metric cardinality explosion:

1. **`JobMetrics` is aggregate-only** — it carries no per-entity, per-project, or per-work-item dimensions. All high-cardinality breakdowns belong exclusively in `JobSnapshot`.
2. **No high-cardinality labels on OTel instruments** — counters and histograms exported via `SnapshotMetricExporter` must use only low-cardinality dimensions (`job_id`, `module`, `stage`). Per-project or per-work-item dimensions are forbidden on OTel metrics.
3. **Validation at the Control Plane** — `POST /lease/{id}/metrics` rejects any `JobMetrics` payload that contains fields not defined in the schema. This prevents agents from silently adding high-cardinality dimensions.
4. **Counter types are additive** — new counter records (e.g. a future `TeamsCounters`) are added as nullable properties on `MigrationCounters` or `DiscoveryCounters`. They must not introduce per-entity arrays or dictionaries inside `JobMetrics`.

Violating these rules produces the same OTel cardinality problems the three-channel model was designed to eliminate.

---

#### TFS Subprocess Constraint

The TFS Export Agent targets .NET Framework 4.8 and **cannot make HTTP calls**. It communicates via NDJSON written to stdout, parsed by `TfsExporterProcessAdapter`. The `ProgressEvent.Metrics` field changes type from `MetricSnapshot?` to `JobMetrics?` — the subprocess emits `JobMetrics` as a payload embedded in the event. `TfsExporterProcessAdapter` extracts and forwards it to `POST /lease/{id}/metrics`. Both sides share the `Abstractions` assembly (targets `netstandard2.0`), so the type is available in .NET Framework 4.8.

---

#### Affected Files

| File | Change |
|------|--------|
| `Abstractions/Models/ProgressEvent.cs` | Remove all counter fields and `LastProcessed`; add `EventSequence` (`long`); keep `Module`, `Stage`, `Message`, `Timestamp`, `LastCheckpointAt`, `NextCheckpointDueAt`; change `Metrics` from `MetricSnapshot?` to `JobMetrics?` |
| `Abstractions/Models/MetricSnapshot.cs` | Delete — replaced by `JobMetrics` |
| `Abstractions/Models/DiscoveryMetricSnapshot.cs` | Delete — replaced by `JobMetrics` |
| `Abstractions/Models/JobMetrics.cs` | New — aggregate counters with `Scope`, `Migration?`, `Discovery?` sections |
| `Abstractions/Models/JobScopeCounters.cs` | New — shared org/project totals |
| `Abstractions/Models/MigrationCounters.cs` | New — shared by `JobMetrics.Migration` and `ProjectSnapshot.Migration` |
| `Abstractions/Models/MigrationDiagnostics.cs` | New — OTel-derived means and correctness counters (aggregate-only) |
| `Abstractions/Models/WorkItemCounters.cs` | New — attempted/completed/failed/skipped + revisions |
| `Abstractions/Models/AttachmentCounters.cs` | New — processed/failed/totalBytes |
| `Abstractions/Models/DiscoveryCounters.cs` | New — shared by `JobMetrics.Discovery` and `ProjectSnapshot.Discovery` |
| `Abstractions/Models/InventoryCounters.cs` | New — revisions/repos/checkpoints |
| `Abstractions/Models/DependencyCounters.cs` | New — analysed/links/cross-project/cross-org/checkpoints |
| `Abstractions/Models/JobSnapshot.cs` | New — `OrgSnapshot[]` hierarchy |
| `Abstractions/Models/OrgSnapshot.cs` | New — org entry with `ProjectSnapshot[]` |
| `Abstractions/Models/ProjectSnapshot.cs` | New — project entry; reuses `MigrationCounters`/`DiscoveryCounters` |
| `Abstractions/Telemetry/IMetricSnapshotStore.cs` | Rename to `IJobMetricsStore`; type changes to `JobMetrics` |
| `MigrationAgent/ControlPlaneTelemetryTimer.cs` | Push `JobMetrics` on fast timer; push `JobSnapshot` every 5 min or at project boundary |
| `ControlPlane/Controllers/TelemetryController.cs` | Split: `POST/GET /metrics` for `JobMetrics`; `POST/GET /snapshot` for `JobSnapshot`; add `GET /jobs/{id}/bootstrap` returning `{ Snapshot, Metrics, LastEventSequence }` |
| `ControlPlane/Services/JobTelemetryStore.cs` | Rename to `JobMetricsStore`; type changes to `JobMetrics` |
| `ControlPlane/Services/JobSnapshotStore.cs` | New — stores latest `JobSnapshot` per job |
| `ControlPlane/Services/JobProgressStore.cs` | Remove `LatestByProject` dictionary — snapshot replaces this mechanism; use `EventSequence` as SSE `id:` field for `Last-Event-ID` replay |
| `Abstractions/Models/JobBootstrap.cs` | New — response record for the bootstrap endpoint |
| `Infrastructure/Telemetry/SnapshotMetricExporter.cs` | Output `JobMetrics` instead of `MetricSnapshot` |
| `CLI.Migration/Views/TuiMainView.cs` | Delete counter accumulation logic; call `GET /snapshot` on connect; poll `GET /metrics` on refresh cycle |
| `CLI.Migration/TfsExporterProcessAdapter.cs` | Extract `JobMetrics` from `ProgressEvent.Metrics` instead of `MetricSnapshot` |

---

## Config Schema Additions Required

The following additions to the scenario JSON schema are implied by the proposals above:

| Field | Location | Purpose |
|---|---|---|
| `modules[].tools[]` | Module config | Array of tool declarations injected into the module |
| `organisations[].processes[]` | Config root | Process metadata discovered by `discovery org-sync` |
| `organisations[].projects[].enabled` | Config root | Already in schema; documented as relevant to `config generate` |
| `scopes[].type: "ids"` | Module scopes | New scope type for explicit work item ID list |
| `modules[].collapseRevisions` | WorkItems module | Boolean option |
| `modules[].maxRevisions` | WorkItems module | Integer option |
| `modules[].maxGracefulFailures` | WorkItems module | Integer option |
| `modules[].generateMigrationComment` | WorkItems module | Boolean option |
| `extensions[Attachments].maxSizeBytes` | WorkItems/Attachments extension | Integer option |
