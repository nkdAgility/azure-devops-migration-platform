# Azure DevOps Migration Platform

> **Copyright (c) Naked Agility Limited.**
> Created and maintained by [Martin Hinshelwood](http://nkdagility.com). Licensed under [AGPL-3.0-only](LICENSE).

> ⚠️ **Pre-release — not production-ready.** This project is under active development and has not reached a stable release. APIs, configuration schemas, and package formats may change without notice. The repository is available for feedback on features and capability — [open an issue](https://github.com/nkdAgility/azure-devops-migration-platform/issues) to share your thoughts.

A versioned migration package platform with streaming chronological replay. Not a live migration tool — it produces and consumes portable, auditable, zip-friendly migration packages.

**Key properties:** deterministic, resumable, portable, auditable, extensible, memory-safe for large datasets.

---

## How It Works

The platform migrates data through an intermediate file-based package — never directly from source to target. This gives you a portable, auditable snapshot at every stage.

```
Source  ──Export──▶  Package (files)  ──Import──▶  Target
                         │
                      Prepare (validate & map before import)
```

The file-based package is a complete, self-contained snapshot. You can zip it, move it to a USB drive, and import on a completely different network. This makes it ideal for **air-gapped or on-premises environments** where source and target have no direct connectivity.

### Modes

| Mode | What it does |
|------|-------------|
| **Export** | Reads from source, writes a versioned package to disk. |
| **Prepare** | Reads the package + connects to target. Validates identities, nodes, fields. Produces mapping reports for operator review. Idempotent. |
| **Import** | Reads the package, writes to target. Auto-runs Prepare first if not already done. |
| **Migrate** | Export → Prepare → Import in one run. Stops after Prepare if blocking issues are found. |

### Supported Sources & Targets

| Connector | Export | Import | Notes |
|-----------|--------|--------|-------|
| **Azure DevOps Services** | ✅ | ✅ | REST API (`.NET 10`). Access token or service principal auth. |
| **Team Foundation Server** | ✅ | 🔜 | TFS Object Model (`.NET 4.8`). Windows-only. Separate agent process. |
| **Simulated** | ✅ | ✅ | For testing only. Deterministic seeded data, no external connectivity. |

---

## Prerequisites

- **.NET 10 SDK**
- **Azure DevOps access token** (PAT, service principal, or managed identity) with appropriate scopes for source and/or target
- **Windows** required only if migrating from Team Foundation Server (TFS Object Model dependency)

---

## Installation

> **This product is in active development and does not have a production release yet.** Install from source.

```bash
git clone https://github.com/nkdAgility/azure-devops-migration-platform.git
cd azure-devops-migration-platform
./build.ps1 install
```

This builds, runs unit tests, publishes for your platform, and installs to `%USERPROFILE%\source\Tools\MigrationPlatform\{version}\` with a `devopsmigrationdev` shim on your PATH (via `%USERPROFILE%\.dotnet\tools\`).

To also start the Aspire AppHost (ControlPlane + MigrationAgent) after install:

```bash
./build.ps1 start
```

---

## Quick Start

### 1. Create a configuration file

```bash
devopsmigration config new --output my-migration.json
```

Or start from one of the [example scenarios](scenarios/) in this repo. A minimal export config looks like:

```json
{
  "MigrationPlatform": {
    "ConfigVersion": "2.0",
    "Mode": "Export",
    "Source": {
      "Type": "AzureDevOpsServices",
      "Url": "https://dev.azure.com/myorg",
      "Project": "MyProject",
      "Authentication": {
        "Type": "Pat",
        "AccessToken": "$ENV:ADO_PAT"
      }
    },
    "Package": {
      "WorkingDirectory": "D:\\exports\\run-001"
    },
    "Modules": {
      "WorkItems": { "Enabled": true }
    }
  }
}
```

> **Tokens**: Use `$ENV:VARIABLE_NAME` to read secrets from environment variables. Never put PATs directly in config files you commit.

### 2. Run a discovery inventory (optional, recommended)

```bash
devopsmigration discovery inventory --config my-migration.json
```

This counts work items and revisions per project — a read-only pre-flight check that helps you estimate migration scope.

### 3. Export

```bash
devopsmigration queue --config my-migration.json --follow
```

With `Mode: "Export"` in the config, this exports your data to the package directory. `--follow` streams progress to the terminal.

### 4. Prepare (validate before import)

Change `Mode` to `"Prepare"` (or use a separate config), then:

```bash
devopsmigration prepare --config my-migration.json
```

Review the validation artefacts in the package — identity mapping reports, node validation, field compatibility. Fix any blocking issues before importing.

### 5. Import

Change `Mode` to `"Import"`, then:

```bash
devopsmigration queue --config my-migration.json --follow
```

---

## CLI Reference

| Command | Purpose |
|---------|---------|
| `queue` | Submit a migration job (Export, Prepare, Import, or Migrate depending on config `Mode`). |
| `prepare` | Submit a Prepare-only job. |
| `discovery inventory` | Count work items and revisions per project (read-only). |
| `discovery dependencies` | Analyse cross-project work item links. |
| `manage list` | List all jobs with status. |
| `manage status --job <id>` | Detailed status for a specific job. |
| `manage pause / resume / cancel` | Job lifecycle control. |
| `config new` | Interactive wizard to create a config file. |
| `config set / get` | User preference management. |
| `tui` | Open the interactive Terminal UI. |

See [docs/cli.md](docs/cli.md) for full command reference and options.

---

## Resuming a Failed Run

Every run is **checkpointed automatically**. If a run fails or is interrupted:

1. Fix the underlying issue (network, permissions, etc.).
2. Re-run the same command with the same config — it resumes from the last checkpoint.
3. Use `--force-fresh` to discard checkpoints and restart a module from the beginning.

Checkpoints are stored in `.migration/Checkpoints/` inside the package directory.

---

## Configuration

A single JSON file drives the entire run. Key sections:

| Section | Controls |
|---------|----------|
| `Source` | Where to read from (type, URL, project, auth). |
| `Target` | Where to write to (type, URL, project, auth). |
| `Package` | Working directory for the file-based package. |
| `Modules` | Which data to migrate (WorkItems, Identities, Nodes, Teams) and their options. |
| `Tools` | Cross-cutting transforms — field mapping, node translation, identity lookup. |
| `Policies` | Retry limits, throttle concurrency, checkpoint interval. |

See [docs/configuration.md](docs/configuration.md) for the full schema reference.

---

## Security & Data Handling

- The migration package contains your data — treat it like a database backup.
- Use `$ENV:VAR` token references for all secrets; never hard-code PATs.
- Do not include migration packages, logs, `.migration` folders, or customer data in issues or PRs.
- See [SECURITY.md](SECURITY.md) for vulnerability reporting.

---

## Further Documentation

| Topic | Document |
|---|---|
| **Operator guide** (start here) | [docs/operator-guide.md](docs/operator-guide.md) |
| Architecture & design decisions | [docs/architecture.md](docs/architecture.md) |
| CLI commands & options | [docs/cli.md](docs/cli.md) |
| Configuration schema | [docs/configuration.md](docs/configuration.md) |
| Module architecture | [docs/modules.md](docs/modules.md) |
| Source types & connectors | [docs/source-types.md](docs/source-types.md) |
| Orchestration | [docs/orchestration.md](docs/orchestration.md) |
| Control plane | [docs/control-plane.md](docs/control-plane.md) |
| Migration agent | [docs/migration-agent.md](docs/migration-agent.md) |
| Aspire integration | [docs/aspire-integration.md](docs/aspire-integration.md) |
| Package format & manifest | [docs/packaging-zip.md](docs/packaging-zip.md) |
| Validation | [docs/validation.md](docs/validation.md) |
| Resilience patterns | [docs/resilience.md](docs/resilience.md) |
| Terminal UI | [docs/tui.md](docs/tui.md) |

---

## Contributing

Contributions are welcome through pull requests. See [CONTRIBUTING.md](CONTRIBUTING.md) for the full process.

**Key points for contributors:**

- All contributions must pass the CLA check before merge.
- Open an issue before starting substantial work.
- Follow existing architecture and module boundaries — see [docs/architecture.md](docs/architecture.md).
- Include tests for behavioural changes.
- Do not include secrets, customer data, or migration packages in PRs.
- The platform core must remain independently buildable without separately licensed add-ins.

## Licensing

The platform core is licensed under AGPL-3.0-only. Optional add-in assemblies or separately distributed components may be provided by Naked Agility Limited under separate licence terms. See [NOTICE](NOTICE) and [LICENSE](LICENSE).
