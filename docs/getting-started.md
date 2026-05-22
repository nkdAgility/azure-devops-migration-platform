# Getting Started

Audience: Operators and new users.

This guide takes you from zero to a completed first run.

## Prerequisites

- .NET 10 SDK (for running from source) or the published `devopsmigration` binary
- PowerShell 7+ (Windows or Linux)
- Access tokens for your source and target Azure DevOps organisations (or Windows credentials for TFS)

## Quick Path

```
1. Create a configuration file (migration.json)
2. Run: devopsmigration queue --config migration.json --mode Export --follow
3. Review Identities/mapping.json and Identities/prepare-report.json
4. Edit mapping.json to resolve any unmapped identities
5. Run: devopsmigration queue --config migration.json --mode Import --follow
6. Run: devopsmigration manage status --job <id>   # verify completion
```

## Create a Configuration File

Minimum viable config:

```json
{
  "MigrationPlatform": {
    "ConfigVersion": "1.0",
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
devopsmigration queue --config migration.json --mode Export --follow
```

This runs the Export phase. Output appears in `D:\migration-output\<org>\<project>\`.

## Next Steps

- See [`operator-guide.md`](operator-guide.md) for the full operator manual.
- See [`configuration-reference.md`](configuration-reference.md) for the complete schema.
- See [`migration-process-guide.md`](migration-process-guide.md) for details on each phase.
- See [`troubleshooting-guide.md`](troubleshooting-guide.md) for common failure diagnosis.