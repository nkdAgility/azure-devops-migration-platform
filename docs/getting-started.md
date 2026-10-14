# Getting Started

Audience: Operators and new users.

This guide takes you from zero to a completed first run.

## Prerequisites

- .NET 10 SDK (for running from source) or the published `devopsmigration` binary
- PowerShell 7+ (Windows or Linux)
- Access tokens for your source and target Azure DevOps organisations (or Windows credentials for TFS)
- **TFS sources:** Windows, and the TFS agent (`tfsmigration.exe`) installed beside the control plane. A TFS → Azure DevOps migration currently needs **two configs**: an Export config with a TFS `Source` and no `Target`, then an Import config with the Azure DevOps `Target` against the same `Package.WorkingDirectory`. A single config with both is not picked up by either agent.

## Quick Path

```
1. Create a configuration file (migration.json)
2. Set "Mode": "Export" in migration.json, then run: devopsmigration queue --config migration.json --follow
3. Review Identities/mapping.json and Identities/prepare-report.json
4. Edit mapping.json to resolve any unmapped identities
5. Set "Mode": "Import" in migration.json, then run: devopsmigration queue --config migration.json --follow
6. Run: devopsmigration manage status --job <id>   # verify completion
```

## Create a Configuration File

Minimum viable config:

```json
{
  "MigrationPlatform": {
    "ConfigVersion": "2.0",
    "Mode": "Export",
    "Package": {
      "WorkingDirectory": "D:\\migration-output"
    },
    "Source": {
      "Type": "AzureDevOpsServices",
      "Url": "https://dev.azure.com/myorg",
      "Project": "MyProject",
      "Authentication": {
        "Type": "AccessToken",
        "AccessToken": "$ENV:SOURCE_PAT"
      }
    },
    "Target": {
      "Type": "AzureDevOpsServices",
      "Url": "https://dev.azure.com/targetorg",
      "Project": "TargetProject",
      "Authentication": {
        "Type": "AccessToken",
        "AccessToken": "$ENV:TARGET_PAT"
      }
    },
    "Modules": {
      "WorkItems": { "Enabled": true }
    }
  }
}
```

Set credentials as environment variables — never put tokens in the config file:

```powershell
$env:SOURCE_PAT = "your-source-token"
$env:TARGET_PAT = "your-target-token"
```

## Run Your First Export

```
devopsmigration queue --config migration.json --follow
```

This runs the phase named by the config's `Mode` field — `Export` in the example above. There is no `--mode` flag: to run Prepare or Import, change `Mode` in the config, or use `"Mode": "Migrate"` to run Inventory → Export → Prepare → Import → Validate as one job. Output appears in `D:\migration-output\<org>\<project>\`.

## Next Steps

- See [`operator-guide.md`](operator-guide.md) for the full operator manual.
- See [`configuration-reference.md`](configuration-reference.md) for the complete schema.
- See [`migration-process-guide.md`](migration-process-guide.md) for details on each phase.
- See [`troubleshooting-guide.md`](troubleshooting-guide.md) for common failure diagnosis.