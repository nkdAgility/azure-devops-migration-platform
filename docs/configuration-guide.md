# Configuration Guide

Audience: Operators.

This guide covers the most common configuration patterns. For the full schema reference, see [`configuration-reference.md`](configuration-reference.md).

## File Structure

All configuration lives under a single `MigrationPlatform` root key in a JSON file:

```json
{
  "MigrationPlatform": {
    "ConfigVersion": "1.0",
    "Mode": "Export",
    "Package": { ... },
    "Source": { ... },
    "Target": { ... },
    "Modules": { ... },
    "Environment": { ... }
  }
}
```

## Token Resolution

Credentials in `AccessToken` fields resolve in this order:

1. `$ENV:VARNAME` — reads the named environment variable (fails if not set)
2. Non-empty literal — used as-is (not recommended for secrets)
3. Null or empty — Windows Integrated Auth (TFS only)

**Always use `$ENV:` for PATs and secrets. Never commit tokens to config files.**

## Source Configuration

### Azure DevOps Services

```json
"Source": {
  "Type": "AzureDevOpsServices",
  "Url": "https://dev.azure.com/myorg",
  "Project": "MyProject",
  "Authentication": {
    "Type": "AccessToken",
    "AccessToken": "$ENV:SOURCE_PAT"
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

TFS sources require Windows and are exported via the TFS Migration Agent (net481).

## Target Configuration

Target configuration follows the same shape as Source. Only `AzureDevOpsServices` is supported as a target.

## Package Configuration

```json
"Package": {
  "WorkingDirectory": "D:\\migration-output"
}
```

The CLI automatically appends `<org>/<project>/` to this path. Ensure sufficient disk space — the package can be large.

## Mode

| Mode | What runs |
|---|---|
| `Inventory` | Counts and catalogues source items |
| `Export` | Inventory → write items to package |
| `Prepare` | Validate target readiness |
| `Import` | Load package into target |
| `Migrate` | All phases in sequence |

## Modules

Enable or disable individual modules:

```json
"Modules": {
  "WorkItems": { "Enabled": true },
  "Teams": { "Enabled": true },
  "Identities": { "Enabled": true }
}
```

## Environment

Controls how the CLI starts the Control Plane and agent:

```json
"Environment": {
  "Type": "Standalone"
}
```

| Type | Meaning |
|---|---|
| `Standalone` (default) | CLI starts Control Plane and agent locally |
| `Hosted` | CLI connects to a remote Control Plane at `ControlPlane.BaseUrl` |

## Safe Handling of PATs

- Store PATs in environment variables, not files.
- Use minimum required scopes: Work Items (Read/Write), Project and Team (Read).
- Rotate PATs regularly.
- The system supports `$ENV:VARIABLE` syntax everywhere `AccessToken` appears.

## Further Reading

- [`configuration-reference.md`](configuration-reference.md) — full schema with all properties
- [`scenarios-guide.md`](scenarios-guide.md) — pre-built config examples
- [`troubleshooting-guide.md`](troubleshooting-guide.md) — fixing configuration errors