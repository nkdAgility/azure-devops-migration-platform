# Azure DevOps Migration Platform

> ⚠️ **Pre-release — not production-ready.** Under active development. APIs, configuration schemas, and package formats may change without notice. [Open an issue](https://github.com/nkdAgility/azure-devops-migration-platform/issues) to share feedback.

[![Licence: AGPL-3.0-only](https://img.shields.io/badge/licence-AGPL--3.0--only-blue)](LICENSE)
[![Build status](https://github.com/nkdAgility/azure-devops-migration-platform/actions/workflows/build.yml/badge.svg)](https://github.com/nkdAgility/azure-devops-migration-platform/actions)

Migrate Azure DevOps and TFS data through a portable, auditable file package — not directly between live systems. Export once, prepare and validate offline, import when you're ready. Works across air-gapped networks, across organisations, and across versions.

Created and maintained by [Martin Hinshelwood](http://nkdagility.com) at Naked Agility Limited.

---

## The problem with live migration

Live migration tools connect directly from source to target. If the network drops, you start over. There's no audit trail. You can't review what will be imported before it lands. Air-gapped or on-premises environments aren't supported at all.

This platform takes a different approach:

```mermaid
flowchart LR
    Source["🗄️ Source\nADO Services / TFS"]
    Package["📦 Package\nfiles on disk"]
    Target["🎯 Target\nADO Services"]

    Source -->|"1️⃣ Export\nread-only"| Package
    Package -->|"2️⃣ Prepare\nvalidate identities, nodes,\nfields — no writes to target yet"| Package
    Package -->|"3️⃣ Import\nwhen ready"| Target
```

The package is a versioned, portable snapshot on disk.Zip it, move it, review it, resume it. Export and import don't need to run on the same machine or at the same time.

---

## What you can migrate

> **Status key:**
> ✅ Complete
> 🚧 Soon
> 🔜 In Progress
> 📋 Later
> ❌ Never

### Migration Modules

Stages run left-to-right: **Capture** → **Export** → **Prepare** → **Import** → **Validate**

| Connector | Module | Capture | Export | Prepare | Import | Validate |
|-----------|--------|:-------:|:------:|:-------:|:------:|:--------:|
| **Azure DevOps Services** | Work Items | ✅ | ✅ | 🔜 | 🔜 | 📋 |
| | Identities | ✅ | ✅ | 🔜 | 🔜 | 📋 |
| | Area & Iteration Nodes | ✅ | ✅ | 🔜 | 🔜 | 📋 |
| | Teams | ✅ | ✅ | 🔜 | 🔜 | 📋 |
| | Git Repositories | ✅ | 🔜 | 🔜 | 🔜 | 📋 |
| | Pipelines | 🔜 | 🔜 | 🔜 | 🔜 | 📋 |
| | Permissions | 🔜 | 🔜 | 🔜 | 🔜 | 📋 |
| **Team Foundation Server** | Work Items | 🚧 | 🚧 | 📋 | 📋 | 📋 |
| | Identities | 🚧 | 🚧 | 📋 | 📋 | 📋 |
| | Area & Iteration Nodes | 🚧 | 🚧 | 📋 | 📋 | 📋 |
| | Teams | 🚧 | 🚧 | 📋 | 📋 | 📋 |📋
| | Git Repositories | 📋 | 📋 | 📋 | 📋 | 📋 |
| | TFVC Repositories* | ❌ | ❌ | ❌ | ❌ | ❌ |
| | Pipelines | 🔜 | 🔜 | 📋 | 📋 | 📋 |
| | Permissions | 🔜 | 🔜 | 📋 | 📋 | 📋 |

> \* We may integrate GitTfs for TFVC repository migration in the future.

### Analisers

**Analysers** run during Capture and write reports to the package for operator review. They never write to source or target.

| Analyser | Status | Purpose |
|----------|:------:|---------|
| Inventory | ✅ | Consolidates per-module counts into a unified package-level report |
| Dependencies | ✅ | Maps cross-project work item links so operators can assess scope before import |

### Tools

**Tools** are stateless transformation services that apply during Export and Import with no I/O of their own.

| Tool | Status | Purpose |
|------|:------:|---------|
| FieldTransform | ✅ | Apply declarative field-rewrite rules to each work item revision |
| NodeTranslation | ✅ | Translate area and iteration path strings against configured mappings |
| IdentityLookup | ✅ | Resolve and remap user identities across organisations |

---

## Install

> No binary release yet — install from source.

**Requirements:**
- .NET 10 SDK
- PowerShell 7+
- Windows (required only for TFS source)

```powershell
git clone https://github.com/nkdAgility/azure-devops-migration-platform.git
cd azure-devops-migration-platform
./build.ps1 install
```

This builds, tests, and installs `devopsmigration` to your PATH via `%USERPROFILE%\.dotnet\tools\`.

To start the Control Plane and Migration Agent locally after install:

```powershell
./build.ps1 start
```

---

## Get started

```mermaid
flowchart LR
    S1["1️⃣ Create config\ndevopsmigration config new"]
    S2["2️⃣ Check scope\ndevopsmigration discovery inventory"]
    S3["3️⃣ Export\ndevopsmigration queue --mode Export"]
    S4["4️⃣ Prepare\ndevopsmigration queue --mode Prepare"]
    S5["5️⃣ Import\ndevopsmigration queue --mode Import"]
    S1 --> S2 --> S3 --> S4 --> S5
```

### 1. Create a config file

```powershell
devopsmigration config new --output migration.json
```

Or start from a [scenario in this repo](scenarios/). A minimal export config:

```json
{
  "MigrationPlatform": {
    "ConfigVersion": "1.0",
    "Mode": "Export",
    "Source": {
      "Type": "AzureDevOpsServices",
      "Url": "https://dev.azure.com/myorg",
      "Project": "MyProject",
      "Authentication": {
        "Type": "Pat",
        "AccessToken": "$ENV:SOURCE_PAT"
      }
    },
    "Package": {
      "WorkingDirectory": "D:\\migration-output"
    },
    "Modules": {
      "WorkItems": { "Enabled": true }
    }
  }
}
```

Set your token as an environment variable — never put it in the config file:

```powershell
$env:SOURCE_PAT = "your-pat-here"
```

### 2. Check scope (optional)

```powershell
devopsmigration discovery inventory --config migration.json
```

Counts work items and revisions per project. Read-only — nothing is written.

### 3. Export

```powershell
devopsmigration queue --config migration.json --follow
```

Exports to the package directory. `--follow` streams live progress to the terminal.

### 4. Review and prepare

```powershell
devopsmigration queue --config migration.json --mode Prepare --follow
```

Validates identities, nodes, and fields against the target. Produces mapping reports in the package directory. Review and fix any blocking issues before importing.

### 5. Import

```powershell
devopsmigration queue --config migration.json --mode Import --follow
```

### Resuming after failure

Every run checkpoints automatically. If it fails or is interrupted, re-run the same command — it picks up where it left off. Use `--force-fresh` to discard checkpoints and restart a module.

---

## Key CLI commands

| Command | Purpose |
|---------|---------|
| `queue` | Submit a job (Export, Prepare, Import, or Migrate). |
| `discovery inventory` | Count work items and revisions (read-only). |
| `discovery dependencies` | Analyse cross-project work item links. |
| `manage list` | List all jobs with status. |
| `manage status --job <id>` | Detailed status for a specific job. |
| `manage pause / resume / cancel` | Job lifecycle control. |
| `config new` | Wizard to create a config file. |
| `tui` | Open the interactive Terminal UI. |

Full reference: [docs/cli-guide.md](docs/cli-guide.md)

---

## Documentation

Full documentation lives in [`docs/`](docs/README.md):

- **Operators** → start with [Getting Started](docs/getting-started.md) and the [Operator Guide](docs/operator-guide.md)
- **Advanced operators** → [Control Plane](docs/control-plane.md), [Agent Hosting](docs/agent-hosting.md), [Observability](docs/observability.md), [Security & Data Sovereignty](docs/security-and-data-sovereignty.md)
- **Contributors** → [Contributor Guide](docs/contributor-guide.md) and [Development Setup](docs/development-setup.md)
- **Reference** → [Configuration](docs/configuration-reference.md), [Package Format](docs/package-format-reference.md), [Architecture](docs/architecture.md)

---

## Getting help

- [Troubleshooting guide](docs/troubleshooting-guide.md) for common failures
- [Open an issue](https://github.com/nkdAgility/azure-devops-migration-platform/issues) for bugs or feature requests
- [SECURITY.md](SECURITY.md) for vulnerability reporting — do not include customer data or migration packages in issues

---

## Contributing

Contributions are welcome. Please read [CONTRIBUTING.md](CONTRIBUTING.md) and [docs/contributor-guide.md](docs/contributor-guide.md) before opening a pull request.

- Open an issue before starting substantial work
- All pull requests must pass the CLA check before merge
- Follow existing architecture and module boundaries
- Include tests for behavioural changes
- Never include secrets, customer data, or migration packages in PRs

---

## Licence

The platform core is licensed under **AGPL-3.0-only**. Optional add-in assemblies may be provided by Naked Agility Limited under separate licence terms.

Copyright (c) Naked Agility Limited. See [LICENSE](LICENSE) and [NOTICE](NOTICE).
