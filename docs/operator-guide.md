# Operator Guide

This guide is for migration operators — people who configure, run, monitor, and troubleshoot migrations using the Azure DevOps Migration Platform. It covers everything from first config to production troubleshooting.

For architecture internals, see [architecture.md](architecture.md). For module developer details, see [module-development-guide.md](module-development-guide.md).

---

## Quick Start

**Binary**: `devopsmigration`

```
1. Create config file (migration.json)
2. devopsmigration queue --config migration.json --follow   # Export + monitor
3. Review Identities/prepare-report.json, edit mapping.json
4. devopsmigration queue --config migration.json --follow   # Import
5. devopsmigration manage status --job <id>                 # Check final state
```

---

## Configuration Essentials

All configuration lives in a single JSON file, nested under the `MigrationPlatform` root key. Keys are PascalCase.

```json
{
  "MigrationPlatform": {
    "ConfigVersion": "1.0",
    "Mode": "Export | Prepare | Import | Migrate",
    "Package": {
      "WorkingDirectory": "D:\\exports\\run-001",
      "CreatePackage": false
    },
    "Source": { ... },
    "Target": { ... },
    "Tools": { ... },
    "Modules": { ... },
    "Policies": { ... },
    "Environment": { ... }
  }
}
```

### Modes

| Mode | What It Does |
|------|-------------|
| `Export` | Extract data from source into the package |
| `Prepare` | Validate target readiness, generate mapping reports |
| `Import` | Load a pre-exported package into the target |
| `Migrate` | Full Export → Prepare → Import in one job |

### Token Resolution

Credential fields (`AccessToken`) resolve in this order:

1. `$ENV:VARNAME` → reads environment variable (fails if unset or empty)
2. Non-empty literal → used as-is
3. Null or empty → Windows-integrated auth

**Always use `$ENV:` for secrets.** Never put PATs in config files.

### Package Path

The CLI auto-appends org and project folders:

```
WorkingDirectory: "D:\\exports"  →  D:\exports\<org>\<project>\
```

---

## Source Types

### Azure DevOps Services

```json
"Source": {
  "Type": "AzureDevOpsServices",
  "Url": "https://dev.azure.com/myorg",
  "Project": "MyProject",
  "ApiVersion": "7.1",
  "Authentication": {
    "Type": "AccessToken",
    "AccessToken": "$ENV:MIGRATION_PAT"
  }
}
```

### Team Foundation Server

```json
"Source": {
  "Type": "TeamFoundationServer",
  "Url": "http://tfs.internal:8080/tfs/DefaultCollection",
  "Project": "MyProject",
  "Authentication": {
    "Type": "Windows"
  }
}
```

- Runs in a dedicated net481 agent (Windows-only)
- Windows-integrated auth only
- Cannot use blob store packages — file paths only
- Package output is identical to ADO exports

### Simulated (Testing)

```json
"Source": {
  "Type": "Simulated",
  "Seed": 42,
  "Generator": {
    "Projects": [
      {
        "Name": "SimulatedProject1",
        "WorkItemTypes": [
          { "Type": "Bug", "Count": 5, "RevisionsPerItem": 3 },
          { "Type": "Task", "Count": 5, "RevisionsPerItem": 2 }
        ]
      }
    ]
  }
}
```

Same seed = identical data every run. Use simulated source/target to validate your config before connecting to live systems.

---

## Modules

Modules execute in a recommended order: **Identities → Nodes → Teams → WorkItems**. Each module handles both export and import for its domain.

### Identities Module

**What it does**: Exports user/group descriptors from the source; provides identity mapping to all other modules during import.

**Configuration**:

```json
"Modules": {
  "Identities": {
    "Enabled": true,
    "DefaultIdentity": "migration-service@contoso.com"
  }
}
```

**Package layout**:

```
Identities/
  descriptors.jsonl        ← One identity per line (export output)
  mapping.json             ← Operator-editable overrides (never auto-modified)
  prepare-report.json      ← Auto-matched & unresolved (Prepare output)
  unresolved.json          ← Identities that couldn't be resolved (import output)
```

**Operator workflow**:

1. **Export** → `descriptors.jsonl` written
2. **Prepare** → Target queried for matches. `prepare-report.json` generated showing auto-matched and unresolved identities
3. **Review** → Open `prepare-report.json`. For each unresolved identity, add an explicit mapping to `mapping.json`
4. **Import** → Resolution order: `mapping.json` → auto-match by UPN/display name → fallback to `DefaultIdentity`

> **Important**: `mapping.json` is never overwritten by the tool. It is your file to edit. Back it up.

**Resolution rules during import**:

| Priority | Source | Behavior |
|----------|--------|----------|
| 1 | `mapping.json` entry | Explicit operator override — always wins |
| 2 | UPN/display name match | Auto-resolved against target |
| 3 | No match found | Logged to `unresolved.json`, `DefaultIdentity` used |

### Nodes Module

**What it does**: Exports and imports area/iteration classification trees.

**Configuration**:

```json
"Modules": {
  "Nodes": {
    "Enabled": true,
    "ReplicateSourceTree": true,
    "AutoCreateNodes": true
  }
}
```

| Option | Default | What It Does |
|--------|---------|-------------|
| `ReplicateSourceTree` | `true` | Copy the full source area/iteration tree to the target |
| `AutoCreateNodes` | `true` | Auto-create any referenced path that doesn't exist on the target |

**Package layout**:

```
Nodes/
  source-tree.json         ← Full source tree snapshot
  referenced-paths.json    ← Paths referenced by exported work items
  prepare-report.json      ← Path existence validation against target
```

> **Gotcha**: If both `ReplicateSourceTree` and `AutoCreateNodes` are `false`, import assumes the target tree already exists. Any missing path causes a failure.

### Teams Module

**What it does**: Exports and imports team membership, settings, sprint iterations, member capacity.

**Configuration**:

```json
"Modules": {
  "Teams": {
    "Enabled": true,
    "AlwaysExport": false,
    "Extensions": {
      "TeamSettings": true,
      "NodeTranslation": true,
      "TeamIterations": true,
      "TeamMembers": true,
      "IdentityLookup": true,
      "TeamCapacity": true
    }
  }
}
```

| Extension | What It Exports/Imports |
|-----------|----------------------|
| `TeamSettings` | Board config, backlog level, bugs behaviour, working days |
| `NodeTranslation` | Records team area/iteration paths for node reference tracking |
| `TeamIterations` | Sprint/iteration assignments (including default and backlog iterations) |
| `TeamMembers` | Team membership with admin flags |
| `IdentityLookup` | Resolves team member identities via the identity translation tool. Members whose identity resolves to the configured default are **skipped** (logged as unresolvable) rather than imported under the wrong identity. |
| `TeamCapacity` | Per-member, per-sprint capacity data |

> **Default team is not assigned automatically.** The Azure DevOps API does not support
> explicitly setting a project's default team. When the import encounters the source default
> team it logs a structured warning ("target API does not support explicit default team
> assignment") and continues. After import, set the default team manually in the target
> project via **Project Settings → Teams**.
>
> **Untranslatable area/iteration paths are skipped.** If a team's area or iteration path
> cannot be mapped to the target classification tree, that path is excluded (with a warning)
> rather than written through unchanged — ensure the source classification tree is replicated
> (`Nodes.ReplicateSourceTree = true`) so paths resolve.

| Option | Default | What It Does |
|--------|---------|-------------|
| `AlwaysExport` | `false` | When `false`, skips teams whose `team.json` already exists (resumable). When `true`, re-fetches every team. |

**Package layout**:

```
Teams/
  {team-slug}/
    team.json              ← Team definition (settings, iterations, members, capacity)
  prepare-report.json      ← Team/group validation against target
```

### WorkItems Module

**What it does**: High-fidelity work item revision export and import — the core of the migration.

**Configuration**:

```json
"Modules": {
  "WorkItems": {
    "Enabled": true,
    "Scope": {
      "Query": "SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.ChangedDate] ASC",
      "Filters": [
        { "Mode": "Include", "Field": "System.AreaPath", "Pattern": "^TeamAlpha" },
        { "Mode": "Exclude", "Field": "System.State", "Pattern": "Removed" }
      ]
    },
    "Extensions": {
      "Revisions": { "Enabled": true },
      "Links": { "Enabled": true },
      "Attachments": { "Enabled": true },
      "Comments": { "Enabled": true },
      "EmbeddedImages": { "Enabled": true }
    }
  }
}
```

#### Scoping with WIQL and Filters

The `Query` selects which work items to process. `@project` is substituted with the configured project name.

**Filters** apply post-fetch, using AND logic:

| Filter Mode | Behavior |
|-------------|----------|
| `Include` | Retain only items where field matches the regex pattern |
| `Exclude` | Discard items where field matches the regex pattern |

- Multiple filters are AND-combined
- Missing field on an item → passes `Exclude`, fails `Include`
- Patterns are case-insensitive with a 2-second regex timeout
- **Tip**: Use indexed fields (`System.AreaPath`, `System.WorkItemType`) to minimize API pre-fetch time

**Example** — Export only Bugs and Tasks from a specific area:

```json
"Filters": [
  { "Mode": "Include", "Field": "System.WorkItemType", "Pattern": "^(Bug|Task)$" },
  { "Mode": "Include", "Field": "System.AreaPath", "Pattern": "^MyProject\\\\TeamAlpha" }
]
```

#### Extensions

| Extension | What It Does | When to Disable |
|-----------|-------------|----------------|
| `Revisions` | Export full revision history. `false` = latest state only | Speed over fidelity (e.g. one-time snapshot) |
| `Links` | Related links, external links, hyperlinks | If link relationships are not needed on target |
| `Attachments` | Download and store binary attachment files | Large packages with many attachments — consider a second pass |
| `Comments` | Fetch comment versions from the ADO Comments API | If comments are not needed |
| `EmbeddedImages` | Download inline images from HTML/Markdown fields, rewrite URLs | If HTML fields don't contain embedded images |

#### Import Stages

Streaming import processes each revision in four ordered stages. The cursor advances after each stage, enabling fine-grained resume:

```
CreatedOrUpdated → AppliedFields → AppliedLinks → UploadedAttachments → Completed
```

If import is interrupted mid-revision, it resumes from the last completed stage — not from the beginning.

#### ID Resolution Strategies (Partial Re-Import)

When importing into a target that already has some work items, configure an ID resolution strategy to seed the ID map:

```json
"Extensions": {
  "WorkItemResolutionStrategy": {
    "Enabled": true,
    "Parameters": {
      "Strategy": "TargetField",
      "FieldName": "Custom.SourceWorkItemId"
    }
  }
}
```

| Strategy | How It Finds Existing Target IDs |
|----------|--------------------------------|
| `TargetField` | Reads a custom field (e.g. `Custom.SourceWorkItemId`) on each target item |
| `TargetHyperlink` | Scans hyperlinks on target items for URLs matching a source pattern |

**Package layout**:

```
WorkItems/
  yyyy-MM-dd/                                           ← Chronological date folder
    <ticks>-<workItemId>-<revisionIndex>/
      revision.json                                     ← Revision state
      comment.json                                      ← Comment versions (if enabled)
      attachment-<filename>                              ← Binary attachments (if enabled)
```

---

## Tools

Tools are shared services configured under `MigrationPlatform.Tools`. They are pure transformations — no I/O, no mutable state. Tools are consumed by modules and extensions during import.

### FieldTransform Tool

Applies declarative field transformations to work item revisions. Transform groups execute in array order.

**Configuration**:

```json
"Tools": {
  "FieldTransform": {
    "Enabled": true,
    "TransformGroups": [
      {
        "Name": "MyTransformGroup",
        "Enabled": true,
        "ApplyTo": ["Bug", "UserStory"],
        "Transforms": [ ... ]
      }
    ]
  }
}
```

- `ApplyTo` is optional — omit to apply to all work item types
- Groups execute in array order; transforms within a group execute in array order
- Each transform must specify at minimum `Type` and `Field`

#### Available Transform Types

| Type | Description | Key Parameters |
|------|-------------|---------------|
| `MapValue` | Translate field values via a dictionary | `Field`, `ValueMap` (key→value pairs) |
| `CopyField` | Copy value from one field to another | `Field` (source), `TargetField` |
| `CopyFieldBatch` | Copy multiple fields in one declaration | Array of source→target pairs |
| `SetField` | Set a field to a literal constant | `Field`, `Value` |
| `ClearField` | Null out a field's value | `Field` |
| `ExcludeField` | Remove a field from the revision entirely | `Field` |
| `RegexField` | Regex find-and-replace on a field value | `Field`, `Pattern`, `Replacement` |
| `MergeFields` | Concatenate multiple fields into one | `SourceFields`, `TargetField`, `FormatTemplate` |
| `CalculateField` | Compute a value from an expression | `Field`, `Expression` |
| `ConditionalField` | Apply a transform only when a condition is met | `Field`, `Condition`, inner transform |
| `ConditionalTag` | Add/remove a tag based on a condition | `Condition`, `Tag` |
| `FieldToTag` | Promote a field's value to a tag | `Field` |
| `MergeToTag` | Merge multiple field values into a tag | `SourceFields`, `Separator` |
| `TreeToTag` | Flatten a hierarchical path to tag values | `Field` (area/iteration path) |

#### FieldTransform Recipes

**Recipe 1 — Remap State values** (e.g. "Active" → "In Progress"):

```json
{
  "Name": "StateRemapping",
  "ApplyTo": ["Bug", "UserStory"],
  "Transforms": [
    {
      "Type": "MapValue",
      "Field": "System.State",
      "ValueMap": {
        "Active": "In Progress",
        "Resolved": "Done",
        "New": "To Do"
      }
    }
  ]
}
```

**Recipe 2 — Copy a custom field to a standard field**:

```json
{
  "Name": "CopyPriority",
  "Transforms": [
    {
      "Type": "CopyField",
      "Field": "Custom.OldPriority",
      "TargetField": "Microsoft.VSTS.Common.Priority"
    }
  ]
}
```

**Recipe 3 — Strip a prefix from area paths using regex**:

```json
{
  "Name": "CleanAreaPaths",
  "Transforms": [
    {
      "Type": "RegexField",
      "Field": "System.AreaPath",
      "Pattern": "^OldProject\\\\Legacy\\\\",
      "Replacement": "NewProject\\"
    }
  ]
}
```

**Recipe 4 — Tag work items by their source area path**:

```json
{
  "Name": "TagByArea",
  "Transforms": [
    {
      "Type": "TreeToTag",
      "Field": "System.AreaPath"
    }
  ]
}
```

**Recipe 5 — Set a tracking field on all imported items**:

```json
{
  "Name": "MarkAsMigrated",
  "Transforms": [
    {
      "Type": "SetField",
      "Field": "Custom.MigratedFrom",
      "Value": "ADO-OldOrg"
    }
  ]
}
```

**Recipe 6 — Exclude a deprecated field**:

```json
{
  "Name": "RemoveLegacyField",
  "Transforms": [
    {
      "Type": "ExcludeField",
      "Field": "Custom.DeprecatedField"
    }
  ]
}
```

**Recipe 7 — Conditionally tag items based on field value**:

```json
{
  "Name": "TagHighPriority",
  "Transforms": [
    {
      "Type": "ConditionalTag",
      "Field": "Microsoft.VSTS.Common.Priority",
      "Condition": "== 1",
      "Tag": "P1-Migrated"
    }
  ]
}
```

### NodeTranslation Tool

Controls area/iteration path remapping, node auto-creation, and source tree replication during import.

```json
"Tools": {
  "NodeTranslation": {
    "Enabled": true,
    "ReplicateSourceTree": true,
    "AutoCreateNodes": true,
    "SkipOnUnresolvableArea": false,
    "SkipOnUnresolvableIteration": false,
    "AreaLanguageOverride": null,
    "IterationLanguageOverride": null,
    "AreaPathMappings": [
      { "Match": "^OldProject\\\\", "Replacement": "NewProject\\" }
    ],
    "IterationPathMappings": []
  }
}
```

| Option | Default | Purpose |
|--------|---------|---------|
| `ReplicateSourceTree` | `false` | Copy full source area/iteration tree to target before import |
| `AutoCreateNodes` | `true` | Auto-create any referenced path that doesn't exist on target |
| `SkipOnUnresolvableArea` | `false` | Skip a work item (instead of failing) when its area path can't be resolved |
| `SkipOnUnresolvableIteration` | `false` | Skip instead of fail on unresolvable iteration paths |
| `AreaLanguageOverride` | `null` | Override localised root node name (e.g. `"Area"` in English, `"Bereich"` in German) |
| `IterationLanguageOverride` | `null` | Same for iteration paths |
| `AreaPathMappings` | `[]` | Regex-based area path transformations. Each entry: `Match` (regex) + `Replacement`. Applied in order. |
| `IterationPathMappings` | `[]` | Same as `AreaPathMappings` but for iteration paths |

**Common pattern — Rename project prefix**:

```json
"AreaPathMappings": [
  { "Match": "^OldProject\\\\", "Replacement": "NewProject\\" }
]
```

**Localisation — German TFS instance**:

```json
"AreaLanguageOverride": "Bereich",
"IterationLanguageOverride": "Iteration"
```

### IdentityLookup Tool

Provides a default fallback identity for unmapped accounts.

```json
"Tools": {
  "IdentityLookup": {
    "Enabled": true,
    "DefaultIdentity": "migration-service@contoso.com"
  }
}
```

### Tool Interaction During Import

During work item import, tools are applied in this order:

1. **IdentityLookup** — resolves identity fields to target identities
2. **NodeTranslation** — remaps area/iteration paths, creates missing nodes
3. **FieldTransform** — applies all declared transform groups in array order

Each tool sees the output of the previous tool. Plan your transforms accordingly — a `FieldTransform` that references `System.AreaPath` will see the path *after* `NodeTranslation` has remapped it.

---

## Policies

Optional tuning knobs under `MigrationPlatform.Policies`:

```json
"Policies": {
  "Retries": { "Max": 8 },
  "Throttle": { "MaxConcurrency": 4 },
  "Checkpoints": { "Interval": 300 },
  "Validation": {
    "ContinueOnError": false,
    "WorkItemCountTolerance": 0,
    "FailOnUnresolvedIdentities": false,
    "SampleRate": 0.05
  }
}
```

| Policy | Default | What It Controls |
|--------|---------|-----------------|
| `Retries.Max` | `3` | Maximum retry attempts for transient API failures |
| `Throttle.MaxConcurrency` | `2` | Maximum parallel API requests |
| `Checkpoints.Interval` | `300` | Seconds between checkpoint flushes |
| `Validation.ContinueOnError` | `false` | If `true`, pre-flight failures are logged but don't halt import |
| `Validation.WorkItemCountTolerance` | `0` | Items that may be missing in post-flight without failing (0 = exact match) |
| `Validation.FailOnUnresolvedIdentities` | `false` | If `true`, any unresolved identity causes import failure |
| `Validation.SampleRate` | `0.05` | Fraction of items sampled in post-flight validation (0–1.0) |

---

## CLI Commands

### Migration Commands

| Command | Purpose |
|---------|---------|
| `queue --config <path> [--follow] [--level WARNING] [--force-fresh] [--port 5200]` | Submit a migration job |
| `prepare --config <path>` | Submit a Prepare job (validate target, generate reports) |

**`queue` options**:

| Option | Default | Purpose |
|--------|---------|---------|
| `--follow` | implicit in standalone | Stream diagnostic logs inline |
| `--level` | `Information` | Agent diagnostic minimum level (`WARNING`, `ERROR`, `Information`) |
| `--force-fresh` | `false` | Delete module cursors so enumeration restarts (identity map preserved) |
| `--port` | `5100` | Override control plane port in standalone mode |

### Job Management (`manage`)

| Command | Purpose |
|---------|---------|
| `manage list` | List all jobs, status & progress |
| `manage status --job <uuid>` | Job state and per-module progress |
| `manage progress --job <uuid>` | Fetch ProgressEvent records as NDJSON |
| `manage diagnostics --job <uuid> [--level WARNING]` | Download agent logs (optionally filtered) |
| `manage pause --job <uuid>` | Signal agent to checkpoint and pause |
| `manage resume --job <uuid>` | Resume a paused job |
| `manage cancel --job <uuid>` | Cancel a queued or running job |
| `manage login --url <endpoint>` | Authenticate with control plane |
| `manage logout --url <endpoint>` | Revoke session token |

### Discovery Modes (via `queue`)

| Command | Purpose |
|---------|---------|
| `queue --config <inventory-config.json>` | Submit an inventory job (`Mode: Inventory`) |
| `queue --config <dependencies-config.json>` | Submit a dependency analysis job (`Mode: Dependencies`) |

### Configuration Commands

| Command | Purpose |
|---------|---------|
| `config new [--output my-migration.json] [--force]` | Interactive wizard to create config file |
| `config set <key> <value>` | Set user-level preference |
| `config get <key>` | Read user-level preference |

### Other Commands

| Command | Purpose |
|---------|---------|
| `controlplane start [--port 5100]` | Start bundled Control Plane host in foreground |
| `tui` | Open interactive Terminal UI for live job monitoring |

### `--config` Resolution (When Omitted)

1. `--config <path>` supplied → use directly
2. `$Env:MigrationPlatform_Scenario_Folder` → scan & prompt
3. `preferences.json` → `scenario-folder` → scan & prompt
4. `./scenarios` subfolder of cwd → use if found
5. `*.json` in cwd → scan & prompt
6. Nothing found → warning with guidance

### Global Options

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `--config` | `-c` | (auto-resolution) | Path to migration config file |
| `--verbose` | `-v` | `false` | Verbose console output |
| `--disable-telemetry` | — | `false` | Suppress all telemetry export |
| `--port` | — | `5100` | Control plane port (standalone mode) |

---

## Validation Model

The platform uses four tiers of validation:

### Tier 0 — Structural (Local, No Network)

Runs before any job submission:
- Config parses as valid JSON
- `ConfigVersion` is supported
- Required fields present (`Mode`, `Package.WorkingDirectory`, `Modules`)
- Module names are registered
- Policy values are in valid ranges

**Failure**: Non-zero exit, no network call made.

### Tier 1 — Connectivity & Permissions (Network Required)

Runs before job submission:
- Source/target reachable
- Projects exist
- Credentials valid with required permissions
- Package path accessible

**Failure**: Non-zero exit with human-readable error.

### Tier 2 — Pre-Flight (Agent, Before Import)

Runs inside the agent before `ImportAsync`:
- `manifest.json` exists and is valid
- All `revision.json` files are valid
- Attachment files exist and hashes match
- Identity mapping integrity verified

**Failure**: Fail-fast by default. Set `Validation.ContinueOnError: true` to log and continue (not recommended for production).

### Tier 3 — Post-Flight (Agent, After Import)

Runs after `ImportAsync` completes:
- Work item count parity (within `WorkItemCountTolerance`)
- Link and attachment integrity (sample-based)
- Unresolved identities recorded
- All module cursors at `Completed`

**Output**: `validation-report.json` written regardless of pass/fail.

---

## Package Format

```
<WorkingDirectory>/
  .migration/
    migration-config.json            ← Config snapshot written by agent at startup
    plan.json                        ← Execution plan persisted after every task transition
    inventory.complete.json          ← Inventory completion marker
    prepare.complete.json            ← Prepare completion marker
  <org>/<project>/
    manifest.json                    ← Package metadata, source info, included types
    Identities/                      ← Identity descriptors and mappings
    Nodes/                           ← Classification tree snapshots
    Teams/                           ← Team definitions by slug
    WorkItems/                       ← Revision folders by date
    .migration/                      ← Project-scoped cursor files
```

### Checkpoint State

```
/.migration/
  inventory.complete.json            ← Marker: Inventory completed
  prepare.complete.json              ← Marker: Prepare completed

/{org}/{project}/.migration/
  inventory.workitems.cursor.json    ← Inventory resume position for WorkItems in this project
  export.workitems.cursor.json       ← Export resume position for WorkItems in this project
  import.workitems.cursor.json       ← Import resume position for WorkItems in this project
```

> `idmap.db` is locked exclusively during job execution. A second agent against the same package will fail fast.

### Zip Packaging

Set `Package.CreatePackage: true` to zip the package after export and unzip before import. Guarantees order preservation and streaming compatibility.

---

## Common Operator Patterns

### Resume After Interruption

```powershell
devopsmigration queue --config migration.json --follow
# (Interrupted)
# Re-run the same command:
devopsmigration queue --config migration.json --follow
```

Cursors resume from last checkpoint. Identity map preserved. Logs append.

### Fresh Restart (Re-Enumerate)

```powershell
devopsmigration queue --config migration.json --force-fresh
```

Deletes module cursor files so enumeration restarts. **Does NOT delete** `idmap.db` (prevents duplicates) or `mapping.json` (operator-edited).

### Test Config with Simulated Data

```powershell
# Use a simulated scenario to validate config
devopsmigration queue --config scenarios/roundtrip-simulated.json --follow
```

### Multi-Org Inventory with Selective Discovery

Set `Enabled: false` on orgs you don't want to iterate. They still participate in GUID→project name resolution:

```json
"Organisations": [
  { "Url": "https://dev.azure.com/org1", "Projects": ["MyProject"], "Enabled": true },
  { "Url": "https://dev.azure.com/org2", "Enabled": false },
  { "Url": "https://dev.azure.com/org3", "Enabled": false }
]
```

Discovery runs only against `org1/MyProject`. Cross-org link GUIDs pointing at `org2`/`org3` are still resolved to readable names.

---

## Gotchas

### 1. Unresolved Identities Don't Block Import by Default

`Validation.FailOnUnresolvedIdentities` defaults to `false`. Always review `Identities/prepare-report.json` after Prepare and populate `mapping.json`.

### 2. Filter Scopes Use AND Logic

Multiple filters are AND-combined. This catches operators who expect OR:

```json
"Filters": [
  { "Mode": "Include", "Field": "System.AreaPath", "Pattern": "^TeamA" },
  { "Mode": "Exclude", "Field": "System.State", "Pattern": "Removed" }
]
```

Result: Items in TeamA area AND NOT in Removed state.

### 3. `--force-fresh` Only Affects Cursors

It deletes `*.cursor.json` files — not `idmap.db` (prevents duplicates) and not `mapping.json` (operator-edited).

### 4. TFS Packages Cannot Use Blob Store

TFS agent is net481 and only supports `file://` paths.

### 5. Live Progress Uses Two Channels

`queue --follow` combines SSE (stage transitions, cursors) and polling (aggregate counters every ~5s). TFS jobs populate `ProgressEvent.Metrics`; .NET 10 jobs use the separate telemetry endpoint.

### 6. `Validation.ContinueOnError` Is Risky

Default `false` = fail-fast on pre-flight validation failure. Setting `true` allows partial/corrupted imports. Not recommended for production.

### 7. Node Auto-Creation Requires Both Settings

If `ReplicateSourceTree: false` AND `AutoCreateNodes: false`, any missing area/iteration path on the target causes failure.

---

## Troubleshooting

### Config Issues

| Error | Cause | Fix |
|-------|-------|-----|
| "Config parses as invalid JSON" | Syntax error | Validate with VS Code (schema auto-applied) or `jq` |
| "Unknown key at path ..." | Typo in config | Hover in VS Code for valid keys |
| "Module 'Foo' not registered" | Misspelled module name | Check `Modules` section keys |
| "`Source` and `Organisations` cannot both be set" | Mode 1/Mode 2 conflict | Use one, not both |

### Connectivity Issues

| Error | Cause | Fix |
|-------|-------|-----|
| "Source project 'X' not found" | Wrong project name or URL | Verify `Source.Project` matches exactly |
| "Target credentials insufficient" | Missing write permission | Ensure PAT has required scopes |
| "Package URI not accessible" | Path doesn't exist or blob creds invalid | Check path; verify SAS token |

### Resume Issues

| Error | Cause | Fix |
|-------|-------|-----|
| "agent.lock exists; package in use" | Another agent running | Wait for first job to complete or delete `agent.lock` |
| "Cursor file corrupted" | Interrupted write | Use `--force-fresh` to restart |

### Validation Issues

| Error | Cause | Fix |
|-------|-------|-----|
| "Attachment hash mismatch" | Corruption during download | Re-export from source |
| "Work item count delta exceeds tolerance" | Missing items in target | Check import logs; increase `WorkItemCountTolerance` or investigate |

---

## Scenario Configs

The `scenarios/` directory contains ready-to-use config templates:

| Category | Scenarios |
|----------|----------|
| **Discovery** | `discovery-dependency-ado-single-project.json`, `inventory-ado-single-project.json`, `inventory-ado-multi-project.json`, `inventory-multi-org.json`, `inventory-simulated.json`, `inventory-tfs-windows-auth.json` |
| **Export** | `queue-export-ado-workitems-single-project.json`, `queue-export-ado-workitems-inline-comments.json`, `queue-export-workitems-simulated-source.json` |
| **Import** | `queue-import-workitems-simulated-fixture.json`, `queue-import-workitems-simulated-target.json` |
| **Full Migration** | `roundtrip-simulated.json` |
| **Regression** | `regression-no-filter-scopes.json` |

Copy a scenario, customize for your environment, and go.

---

## Tips for Operators

1. **Start with defaults** — Only override policies if you need to.
2. **Always run Prepare first** — Catches 80% of issues before Import.
3. **Version-control `mapping.json`** — It's your operator-edited file. Back it up.
4. **Use `manage diagnostics`** — Download logs after job completion to diagnose issues.
5. **Copy from `scenarios/`** — Use existing configs as templates.
6. **`$ENV:` for all secrets** — Never commit PATs to config files.
7. **Test with Simulated first** — Validate your config against simulated data before live systems.
8. **Review identity mappings** — Always check `prepare-report.json` and populate `mapping.json` for unresolved identities.
