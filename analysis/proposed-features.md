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
| **Tool** | A shared, cross-cutting service declared once at MigrationPlatform config root (e.g. `FieldMappingTool`, `NodeStructureTool`). Extensions load tools by reference and may override selected values. Not a CLI command. |
| **Scope** | A mandatory selection criterion on a module (e.g. `wiql` query, project name). |
| **Discovery command** | A `discovery *` CLI sub-command that runs locally without submitting a `MigrationJob`. Reads source systems directly via REST. |
| **CLI feature** | A `queue`, `config`, `manage`, or `admin` command addition. |
| **Config feature** | A new field or schema addition to the scenario JSON config. |

### Tool Resolution Model (Canonical for This Proposal)

- Tools are declared at `MigrationPlatform.tools[]` (single source of truth for defaults).
- Modules do not declare tool definitions.
- Extensions load tools via `extensions[].tools[]` references.
- Each extension tool reference can override a subset of values for that extension only.
- Effective tool settings = migration-level tool defaults + extension-level overrides.

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
| **P3** | **[Process-per-Component Standalone Mode](#p3-process-per-component-standalone-mode)** | **🆕 ❌ 🔴 CRITICAL** | **Eliminate OTel instrumentation bleed in standalone mode by running ControlPlane and Agent as separate processes** |

---

## Modules — Detail

### M1: WorkItemsModule — FieldMapping

**Current state**: The module copies fields as opaque values. Identity fields are mapped by `IIdentityMappingService`. No general field transformation exists.

**Why it matters**: Any migration between organisations with different process templates (e.g. Agile source → Scrum target) requires value translation on State, Priority, and custom fields before import. Without this, work items import with invalid or missing field values.

**Proposed additions**:

#### New Tool: `FieldMappingTool` (see [T1](#t1-fieldmappingtool))

Declared once in `MigrationPlatform.tools[]`. The `WorkItems/Revisions` extension loads it by reference and may override selected values before writing `revision.json`.

```json
{
  "MigrationPlatform": {
    "tools": [
      {
        "id": "fieldmap-default",
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
    ],
    "Modules": {
      "WorkItems": {
        "Enabled": true,
        "Extensions": {
          "Revisions": {
            "Enabled": true,
            "tools": [
              {
                "ref": "fieldmap-default",
                "overrides": {
                  "applyTo": ["User Story", "Bug", "Task"]
                }
              }
            ]
          }
        }
      }
    }
  }
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
  "MigrationPlatform": {
    "tools": [
      {
        "id": "nodes-default",
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
    ],
    "Modules": {
      "WorkItems": {
        "Enabled": true,
        "Extensions": {
          "Revisions": {
            "Enabled": true,
            "tools": [
              {
                "ref": "nodes-default",
                "overrides": {
                  "createMissingNodes": true
                }
              }
            ]
          }
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
    "tools": [
      {
        "id": "wit-map-default",
        "type": "WorkItemTypeMapping",
        "map": {
          "User Story":    "Product Backlog Item",
          "Issue":         "Impediment"
        }
      }
    ],
    "Modules": {
      "WorkItems": {
        "Enabled": true,
        "Extensions": {
          "Revisions": {
            "Enabled": true,
            "tools": [
              { "ref": "wit-map-default" }
            ]
          }
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

### T1: FieldMappingTool

**Used by**: `WorkItemsModule`  
**Purpose**: Apply a declared set of field transformation rules to each work item revision before it is written to the package (export) or applied to the target (import).

**Invocation**: Declared in `MigrationPlatform.tools[]`, then loaded by extension references (for example `WorkItems/Revisions`) with optional `overrides`. See [M1](#m1-workitemsmodule--fieldmapping) for the full map type list and JSON schema.

**Key design rules**:
- Maps are applied in declaration order.
- `FieldSkip` maps remove the field from the revision before any write — they are not import-only.
- All maps respect the `applyTo` work item type filter.
- Map processing is a pure transformation (no I/O). The tool is injected as `IFieldMappingTool` and receives a `WorkItemRevision` value object; it returns a transformed copy.

---

### T2: NodeStructureTool

**Used by**: `WorkItemsModule`, `TeamsModule`  
**Purpose**: Remap `System.AreaPath` and `System.IterationPath` values before write/import; optionally create missing nodes in the target via the ADO Classification Nodes API.

**Invocation**: Declared in `MigrationPlatform.tools[]`, then loaded by relevant extension references with optional overrides. See [M2](#m2-workitemsmodule--nodestructure-tool) for the JSON schema.

**Key design rules**:
- Node creation calls are idempotent (check before POST).
- All path lookups are normalised (case-insensitive, trimmed).
- Language map overrides apply before regex mapping (so localised node names resolve correctly).
- The tool is injected as `INodeStructureTool`.

---

### T3: WorkItemTypeMappingTool

**Used by**: `WorkItemsModule`  
**Purpose**: Translate source work item type names to target names at both export time (stored in package) and import time (used for item creation).

**Invocation**: Declared in `MigrationPlatform.tools[]`, then loaded by extension references (typically `WorkItems/Revisions`). See [M3](#m3-workitemsmodule--workitemtypemapping-tool) for the JSON schema.

**Features**:
- Bidirectional: a map entry translates both the type name in the revision and any type-name references in link targets.
- Unmapped types pass through unchanged.
- Export-time validation: warn if a mapped target type does not exist in the target process.

---

### T4: StringManipulatorTool

**Used by**: `WorkItemsModule`  
**Purpose**: Apply regex-based find-and-replace rules to string field values (e.g. stripping legacy prefixes, sanitising invalid characters).

**Invocation**: Declared in `MigrationPlatform.tools[]`, then loaded by extension references with optional overrides.

```json
  "MigrationPlatform": {
    "tools": [
      {
        "id": "workitem-resolution-default",
        "type": "WorkItemResolution",
        "default": {
          "strategy": "ReflectedWorkItemIdField",
          "field": "Custom.ReflectedWorkItemId"
- Invalid character cleanup as a named built-in rule: `{ "type": "StripControlCharacters" }`.
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
    ],
    "Modules": {
      "WorkItems": {
        "Enabled": true,
        "Extensions": {
          "Revisions": {
            "Enabled": true,
            "tools": [
              {
                "ref": "workitem-resolution-default"
              }
            ]
```
---
      }
### T6: ChangesetMappingTool
  }
**Used by**: `WorkItemsModule` (link rewriting for TFVC changeset links)  
**Purpose**: Map TFS changeset IDs to Git commit SHAs so that `Fixed In Changeset` links survive migration.

**Invocation**: Declared in `MigrationPlatform.tools[]`, then loaded by `WorkItems` extension references.

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
  "tools": [
    {
      "id": "workitem-resolution-default",
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
  ],
  "modules": [
    {
      "name": "WorkItems",
      "extensions": [
        {
          "type": "Import",
          "enabled": true,
          "tools": [
            {
              "ref": "workitem-resolution-default"
            }
          ]
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

### P2: Three-Channel Telemetry Model — Remaining Items

> The core Three-Channel Telemetry Model is **implemented**. The types (`ProgressEvent`, `JobMetrics`, `JobSnapshot`, `JobBootstrap`), stores (`IJobMetricsStore`, `IJobSnapshotStore`, `JobTelemetryStore`, `JobSnapshotStore`), endpoints (POST/GET metrics, POST/GET snapshot, GET bootstrap), SSE `id:` field + server-side `Last-Event-ID` replay, `ControlPlaneTelemetryTimer` (dual push), `SnapshotMetricExporter` → `JobMetrics`, TFS subprocess adapter, TUI bootstrap fetch, and TUI Channel 2-only polling are all in place. See `.agents/context/telemetry-architecture.md` for the canonical description.
>
> The items below are the remaining gaps.

#### P2-A: Client-Side `Last-Event-ID` on SSE Reconnect

**Status**: Server-side implemented; client-side missing.

`ProgressController` parses the `Last-Event-ID` header and replays missed events. However, `ControlPlaneClient.FollowLogsAsync()` does not send the header when reconnecting to the SSE stream. When the TUI reconnects after a network blip, it currently replays all per-project state instead of only missed events.

**Fix**: `FollowLogsAsync` should accept an optional `lastEventSequence` parameter. On reconnect, set the `Last-Event-ID` request header to the last received sequence. The bootstrap endpoint already returns `LastEventSequence` for the initial connection.

#### P2-B: Project-Boundary Snapshot Push

**Status**: Timer-based push implemented; boundary-based push missing.

`ControlPlaneTelemetryTimer` pushes `JobSnapshot` on a configurable timer interval. The spec calls for also pushing at **project boundaries** (when a project completes or fails) so that late-joining clients see per-project state changes sooner than the timer interval.

**Fix**: Discovery modules already call `PushSnapshot` at project boundaries via the `IJobSnapshotStore`, and the timer reads `Latest`. To get boundary-triggered HTTP pushes to the Control Plane, either: (a) make the timer wake on a `ManualResetEventSlim` signal set by the store's `Update()` method, or (b) have discovery modules directly trigger an immediate push via a new `ISnapshotPushTrigger` interface.

#### P2-C: Rename `JobTelemetryStore` → `JobMetricsStore`

**Status**: Cosmetic naming inconsistency.

The control-plane store class is still named `JobTelemetryStore` (file: `ControlPlane/Services/JobTelemetryStore.cs`). For consistency with the `JobMetrics` type used everywhere else, it should be renamed to `JobMetricsStore`. This is a non-functional change.

#### P2-D: `POST /metrics` Payload Validation

**Status**: Not implemented.

The spec's cardinality guardrails call for the Control Plane to reject `JobMetrics` payloads containing fields not defined in the schema. Currently `TelemetryController.PushTelemetry()` accepts and stores any payload that deserialises successfully. This prevents agents from silently adding high-cardinality dimensions.

**Fix**: Configure `System.Text.Json` with `JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow` for the metrics deserialization path, or add explicit model validation.

#### P2-E: Remove `LatestByProject` from `JobProgressStore`

**Status**: Still in use.

The spec states that `JobSnapshot` should replace `JobProgressStore.LatestByProject` as the authoritative source for per-project state. Currently both mechanisms coexist: `LatestByProject` feeds the SSE replay in `ProgressController`, and `JobSnapshot` feeds the bootstrap endpoint. Once the TUI uses bootstrap + `Last-Event-ID` for reconnect (P2-A), `LatestByProject` can be removed.

**Dependency**: Requires P2-A (client-side `Last-Event-ID`).

---

### P3: Process-per-Component Standalone Mode

> **🔴 CRITICAL — Next Priority**
>
> This is the highest-priority platform feature. The current quick fix (exporter-only registration in `LocalStackHost`) is a temporary workaround with known limitations. This proposal is the proper architectural fix.

**Current state**: In standalone mode (`devopsmigration export --config ...`), the CLI hosts the ControlPlane API and MigrationAgent worker **in-process** via `LocalStackHost`. Each component creates its own `IHostApplicationBuilder` and `IServiceProvider` (separate DI containers), but they all run in the **same OS process**.

**The problem**: OpenTelemetry's `AddHttpClientInstrumentation()` hooks `System.Net.Http` via `System.Diagnostics.DiagnosticListener`, which is **process-global** (static singleton). When any `HttpClient` in the process makes a call, **all** registered OTel pipelines capture the span and attribute it to their own `service.name`. This causes phantom connections on the Application Insights Application Map:

- **CLI → dev.azure.com** (phantom) — the CLI's OTel pipeline captures the Agent's ADO HTTP calls
- **ControlPlane → dev.azure.com** (phantom) — the ControlPlane's OTel pipeline captures the Agent's ADO HTTP calls
- **CLI → Agent** (phantom) — actually CLI → ControlPlane, but bleed causes mis-attribution

The correct topology is: `CLI ↔ ControlPlane ↔ Agent ↔ dev.azure.com`

**Quick fix in place** (temporary):
- `LocalStackHost` does NOT call `AddServiceDefaults()` for the in-process ControlPlane (no OTel at all — it only serves HTTP).
- `LocalStackHost` registers the Agent with exporter-only telemetry (`ConfigureAgentExporterOnly`) — custom meters and trace sources exported to Azure Monitor, but **no** `AddHttpClientInstrumentation()`.
- The CLI's own pipeline still has `AddHttpClientInstrumentation()`, so CLI → ControlPlane dependency traces are captured — but Agent → ADO calls also appear as CLI dependencies. This is an accepted trade-off.

**Why the quick fix is insufficient**:
1. Agent → ADO calls appear as CLI → ADO on the Application Map (misleading topology).
2. The Agent's custom `ActivitySource` traces (e.g. `WorkItemExport`, `AttachmentDownload`) are exported by the Agent's pipeline but HTTP dependency spans are captured by the CLI pipeline — split attribution.
3. No Live Metrics for the in-process ControlPlane or Agent (they use standalone exporters, not `UseAzureMonitor()` distro).
4. Any future component that registers `AddHttpClientInstrumentation()` will reintroduce the bleed.

**Proposed solution**: Launch ControlPlane and Agent as **separate child processes** instead of in-process hosts.

#### Design

```
CLI process (parent)
├── Starts ControlPlane process (child)
│   └── Runs ControlPlaneHost (full AddServiceDefaults, Live Metrics, correct service.name)
├── Waits for health check
├── Starts Agent process (child)
│   └── Runs MigrationAgent (full AddServiceDefaults, Live Metrics, correct service.name)
└── CLI's own OTel pipeline (standalone exporters, CLI service.name)
    └── Only captures CLI → ControlPlane HTTP calls (correct topology)
```

Each process has its own `DiagnosticListener` instance — no bleed possible. All three components appear correctly on the Application Map with proper service names, dependency arrows, and Live Metrics.

#### Implementation approach

1. **Reuse existing entry points**: `ControlPlaneHost/Program.cs` and `MigrationAgent/Program.cs` already exist as standalone binaries. `LocalStackHost` launches them as child processes via `System.Diagnostics.Process`.
2. **Port negotiation**: CLI picks a free port, passes it to ControlPlane via command-line arg or env var. Agent receives the ControlPlane URL the same way.
3. **Lifecycle management**: CLI manages child process lifecycle — starts both, monitors health, forwards cancellation (Ctrl+C), and ensures cleanup on exit.
4. **Config forwarding**: Both child processes need `appsettings.json` and the scenario config path. Pass via env vars or temp config file.
5. **Log routing**: Child process stdout/stderr is captured by the CLI for diagnostic display. The ControlPlane's SSE stream remains the primary progress channel.
6. **Single-binary distribution**: The CLI package must include `ControlPlaneHost` and `MigrationAgent` binaries. Build scripts (`build.ps1`) must produce all three.
7. **Fallback**: If child process launch fails (e.g. missing binaries), fall back to current in-process mode with a warning about Application Map accuracy.

#### Affected files

| File | Change |
|------|--------|
| `CLI.Migration/LocalStackHost.cs` | Replace in-process `WebApplication.CreateBuilder()` / `Host.CreateApplicationBuilder()` with `System.Diagnostics.Process.Start()` for ControlPlane and Agent binaries |
| `CLI.Migration/LocalStackHost.cs` | Remove `ConfigureAgentExporterOnly()` helper — no longer needed |
| `CLI.Migration/MigrationPlatformHost.cs` | Remove `AddHttpClientInstrumentation()` workaround comments — HTTP instrumentation is now safe |
| `CLI.Migration/DevOpsMigrationPlatform.CLI.Migration.csproj` | Remove ServiceDefaults project reference (no longer hosts in-process) |
| `ControlPlaneHost/Program.cs` | Accept `--port` argument for dynamic port binding |
| `MigrationAgent/Program.cs` | Accept `--control-plane-url` argument for dynamic endpoint |
| `build.ps1` | Ensure CLI publish includes ControlPlaneHost and MigrationAgent binaries |
| `.vscode/launch.json` | Update standalone debug profile to launch all three processes |

#### Benefits

- **Correct Application Map** — each component is a separate node with accurate dependency arrows
- **Live Metrics for all components** — each process uses full `UseAzureMonitor()` distro
- **No bleed possible** — `DiagnosticListener` is process-scoped
- **Simpler code** — no need for exporter-only workarounds or instrumentation suppression
- **Future-proof** — adding new components (e.g. a Git sync worker) follows the same pattern

---

The following additions to the scenario JSON schema are implied by the proposals above:

| Field | Location | Purpose |
|---|---|---|
| `MigrationPlatform.tools[]` | MigrationPlatform config root | Array of reusable tool declarations and defaults |
| `MigrationPlatform.Modules.<Module>.Extensions.<Extension>.tools[]` | Extension config | Tool references loaded by an extension; may include `overrides` |
| `organisations[].processes[]` | Config root | Process metadata discovered by `discovery org-sync` |
| `organisations[].projects[].enabled` | Config root | Already in schema; documented as relevant to `config generate` |
| `scopes[].type: "ids"` | Module scopes | New scope type for explicit work item ID list |
| `modules[].collapseRevisions` | WorkItems module | Boolean option |
| `modules[].maxRevisions` | WorkItems module | Integer option |
| `modules[].maxGracefulFailures` | WorkItems module | Integer option |
| `modules[].generateMigrationComment` | WorkItems module | Boolean option |
| `extensions[Attachments].maxSizeBytes` | WorkItems/Attachments extension | Integer option |
