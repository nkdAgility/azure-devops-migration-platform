# CLI Contract: `discovery dependencies`

**Feature**: `012-discovery-dependencies`  
**Command path**: `devopsmigration discovery dependencies`  
**Settings key**: `DependencyCommandSettings`

---

## Synopsis

```
devopsmigration discovery dependencies [options]

Options:
  -c, --config <PATH>      Path to discovery config file [default: migration.json]
  --output <PATH>          Output CSV file path [default: ./discovery-dependencies.csv]
  --wiql <EXPRESSION>      WIQL filter expression to scope analysis [optional]
  -v, --verbose            Enable verbose console output
  --disable-telemetry      Suppress all telemetry export
  -h, --help               Show help information
```

---

## Options

### `--config <PATH>` (or `-c`)

**Type**: `string` (file path)  
**Default**: `migration.json` in the current working directory  
**Required**: Yes (a valid `DiscoveryOptions`-format config file must exist)  
**Description**: Path to the JOSN configuration file. Must be a `DiscoveryOptions`-format file (the same format used by `discovery inventory`). Must NOT use a `source`/`target` section.

**Validation**:
- File must exist. If not: exit code 1 + message "Config file not found: {path}"
- File must be valid JSON. If not: exit code 1 + JSON parse error details
- `DiscoveryOptions.Validate()` is called on startup. If invalid: exit code 1 + validation message

### `--output <PATH>`

**Type**: `string` (file path)  
**Default**: `discovery-dependencies.csv` in the current working directory  
**Required**: No  
**Description**: File path for the output CSV. If the file already exists, it is overwritten and a warning is printed.

### `--wiql <EXPRESSION>`

**Type**: `string` (WIQL query)  
**Default**: `null` (analyse all work items in every configured project)  
**Required**: No  
**Description**: A WIQL expression used to filter which source work items are analysed. Only work items matching the expression will have their outbound links inspected. Example: `"SELECT [System.Id] FROM WorkItems WHERE [System.AreaPath] UNDER 'MyProject\\Sprint 1'"`

**Validation**:
- When provided, the expression is passed to the ADO/TFS query API before any link analysis begins. If the API rejects it (HTTP 400): exit code 1 + server-provided error message. No client-side WIQL parsing.

---

## Output Artefacts

### CSV File (`discovery-dependencies.csv` or `--output` path)

Header row always written. For zero results: header + zero data rows.

```
SourceWorkItemId,SourceWorkItemType,SourceProject,LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus
```

**Example rows**:
```csv
SourceWorkItemId,SourceWorkItemType,SourceProject,LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus
1234,User Story,ProjectA,Child,CrossProject,5678,ProjectB,https://dev.azure.com/myorg,Reachable
1234,User Story,ProjectA,Related,CrossProject,9012,ProjectC,https://dev.azure.com/myorg,Deleted
3456,Bug,ProjectA,Related,CrossOrganisation,7890,,https://dev.azure.com/anotherorg,Unknown
```

### Console Summary Table

Printed to stdout after the CSV is written (FR-007):

```
┌────────────────────────────────────────────────────────────────────────────┐
│                   Dependency Analysis Summary                              │
├──────────────────────────┬─────────────────────────────────────────────────┤
│ Work Items Analysed       │ 2,450                                          │
│ External Links Found      │ 87                                             │
│   CrossProject            │ 71                                             │
│   CrossOrganisation       │ 16  ⚠ ACTION REQUIRED — links will break      │
│ Report written to         │ ./discovery-dependencies.csv                   │
└──────────────────────────┴─────────────────────────────────────────────────┘
```

Cross-organisation counts are highlighted with a `⚠` warning symbol.

When zero external dependencies are found:
```
No external dependencies found.
Report written to ./discovery-dependencies.csv (header row only)
```

---

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success — report written (may have zero rows) |
| 1 | User error — invalid config, invalid WIQL, missing file |
| 2 | Connectivity error — could not reach any configured organisation |

---

## Registration

```csharp
config.AddBranch("discovery", branch => {
    branch.AddCommand<InventoryCommand>("inventory");
    branch.AddCommand<DependencyCommand>("dependencies");  // NEW
});
```

Added to `.vscode/launch.json`:

```json
{
    "name": "🔍 Migration CLI: Dependencies (Single Project)",
    "type": "coreclr",
    "request": "launch",
    "program": "${workspaceFolder}/src/DevOpsMigrationPlatform.CLI.Migration/bin/Debug/net10.0/devopsmigration.exe",
    "args": [ "discovery", "dependencies", "--config", "scenarios/discovery-dependency-ado-single-project.json" ],
    "env": {
        "AZDEVOPS_SYSTEM_TEST_ORG": "${env:AZDEVOPS_SYSTEM_TEST_ORG}",
        "AZDEVOPS_SYSTEM_TEST_PAT": "${env:AZDEVOPS_SYSTEM_TEST_PAT}"
    },
    "cwd": "${workspaceFolder}",
    "console": "integratedTerminal",
    "stopAtEntry": false,
    "preLaunchTask": "build-migration-cli"
}
```

---

## DI Registration Extension

```csharp
// Infrastructure.AzureDevOps/DependencyServiceCollectionExtensions.cs
public static IServiceCollection AddAzureDevOpsDependencyAnalysis(
    this IServiceCollection services,
    IConfiguration configuration)
{
    services.AddOptions<DiscoveryOptions>().Bind(configuration);
    services.AddSingleton<IAzureDevOpsClientFactory, AzureDevOpsClientFactory>();
    services.AddSingleton<IWorkItemLinkAnalysisService, AzureDevOpsDependencyAnalysisService>();
    services.AddSingleton<IDependencyDiscoveryService, DependencyDiscoveryService>();
    return services;
}
```

Called from `DependencyCommand.ExecuteInternalAsync` via `CreateHost(..., (services, config) => services.AddAzureDevOpsDependencyAnalysis(config))`.

---

## Invariants

- `SameProject` links are never written to the CSV (filtered before record creation).
- The `LinkScope` column ONLY ever contains `CrossProject` or `CrossOrganisation`.
- The CSV header row is always written, regardless of row count.
- The command does NOT submit a `MigrationJob` to the control plane.
- The command does NOT read a `source` or `target` section from the config.
- Cross-organisation links always trigger a distinct `⚠` warning in the terminal summary.
