# Azure DevOps Migration Platform – Agent and Platform Overview

This document provides an overview of the architecture, components, and development workflow for the Azure DevOps Migration Platform. It covers the Agent, Control Plane, CLI, configuration, and best practices for building, running, and debugging the platform.

---

## 🧱 Project Structure

```plaintext
/src
├── DevOpsMigrationPlatform.Agent         # Worker that executes migration jobs
├── DevOpsMigrationPlatform.ControlPlane  # ASP.NET Core Web API for job orchestration
├── DevOpsMigrationPlatform.CLI           # Command-line tool for job lifecycle management
├── MigrationPlatform.CLI.TfsExport       # TFS export CLI tool (requires .NET 4.5)
├── MigrationPlatform.Abstractions        # Shared contracts and interfaces
├── MigrationPlatform.Infrastructure      # Core infrastructure and service implementations
├── MigrationPlatform.Infrastructure.TfsObjectModel # TFS-specific integration and services (requires .NET 4.5)
```

### Entry Points

| Component     | Path                                               | Main Entry File            |
| ------------- | -------------------------------------------------- | -------------------------- |
| Agent         | `/src/DevOpsMigrationPlatform.Agent`               | `Program.cs`               |
| Control Plane | `/src/DevOpsMigrationPlatform.ControlPlane`        | `Program.cs`, `Startup.cs` |
| CLI           | `/src/DevOpsMigrationPlatform.CLI.Migration`       | `Program.cs`               |
| CLI for TFS   | `/src/MigrationPlatform.CLI.TfsExport`             | `Program.cs`               |

---

## ⚙️ Dev Container Setup

A `.devcontainer/devcontainer.json` is provided for a consistent development environment with:

* .NET SDK 10
* Azure CLI tools (for Azure deployments and management)
* Docker-in-Docker support (optional)
* Pre-installed debugging configuration

**Getting Started:**

1. Open the repo in Visual Studio Code
2. Use `Dev Containers: Reopen in Container`
3. All dependencies will be installed automatically

---

## 🚀 CLI Commands

The CLI (`devopsmigration.exe`) is the main entry point for managing migration jobs.

```bash
devopsmigration.exe prepare     # Validates and creates a job definition
devopsmigration.exe queue       # Sends job to the control plane
devopsmigration.exe status      # Gets job progress
devopsmigration.exe agent       # Starts a long-running agent
devopsmigration.exe run-local   # Runs agent + control plane locally for offline migration
```

CLI commands are implemented in `/src/DevOpsMigrationPlatform.CLI/Commands/`.

---

## 🛠️ Configuration

Two configuration systems are used:

### 1. Application Configuration (`appsettings.json` per project)

* Controls logging, telemetry (OpenTelemetry), and API endpoints
* Located in each `/src/Project/appsettings.json`
* Supports Azure best practices for configuration and secrets

### 2. Migration Job Configuration (`*.migration.json`)

* Defines processors and migration flow
* Loaded by CLI and passed to Agent
* Example:

```json
{
  "Processors": ["WorkItems", "GitRepos"],
  "Source": "Filesystem",
  "Target": "AzureDevOps"
}
```

---

## 🔁 Agent Responsibilities

The Agent (`DevOpsMigrationPlatform.Agent`) is responsible for:

1. Polling the Control Plane for jobs
2. Loading job configuration and processor pipeline
3. Executing processors in order:
   * WorkItems
   * Revisions
   * Links
   * Attachments
   * GitRepos
4. Writing checkpoint data:
   * `.resume.json` (resume state)
   * `.map.db` (ID mapping)
5. Sending status updates to the Control Plane

Checkpointing enables pause/resume and migration recovery.

---

## 🌐 Control Plane

The Control Plane (`DevOpsMigrationPlatform.ControlPlane`) is an ASP.NET Core REST API that:

* Accepts and queues job definitions
* Assigns jobs to agents
* Tracks progress and state
* Supports tiered service levels (`Standard`, `Premium`, `Platinum`)
* Enables cooperative cancellation and resume
* Can be hosted on Azure App Service, Container Apps, or self-hosted

OpenAPI/Swagger UI is available for API exploration.

---

## 🧪 Development & Debugging

### Agent

```bash
dotnet run --project ./src/DevOpsMigrationPlatform.Agent
```
* Set breakpoints in `JobExecutionService.cs`
* Use `.vscode/launch.json` for debugging

### CLI

```bash
dotnet run --project ./src/DevOpsMigrationPlatform.CLI -- prepare --config ./config/sample.migration.json
```

### Control Plane

```bash
dotnet run --project ./src/DevOpsMigrationPlatform.ControlPlane
```
* Open `https://localhost:5001/swagger` for API docs

---

## 🧪 Unit Tests

Unit tests are located in `/tests` for each project.

```bash
dotnet test ./tests/DevOpsMigrationPlatform.Agent.Tests
dotnet test ./tests/DevOpsMigrationPlatform.ControlPlane.Tests
```

---

## 🌍 Hosting Options

| Component     | Options                                          |
| ------------- | ------------------------------------------------ |
| Control Plane | Azure App Service, Azure Container App, or self-hosted |
| Agent         | Azure Container App, Docker, or `run-local`      |
| CLI           | Local only (dev machine or pipeline)             |

---

## 🧩 Key Files

| File                                         | Purpose                                 |
| -------------------------------------------- | --------------------------------------- |
| `.devcontainer/devcontainer.json`            | Dev environment with tools preinstalled |
| `agents.md`                                  | Entry doc for contributors and Codex    |
| `Program.cs` (each project)                  | Entry points                            |
| `JobExecutionService.cs`                     | Main logic runner inside the agent      |
| `JobController.cs`                           | Control plane REST entry point          |
| `appsettings.json`                           | Runtime config                          |
| `*.migration.json`                           | Job definition and processor list       |
| `.resume.json`                               | Agent resume state                      |
| `.map.db`                                    | Source/target ID map                    |

---

## 📚 References

* Follows [SOLID principles](https://en.wikipedia.org/wiki/SOLID)
* Uses [IOptions pattern](https://learn.microsoft.com/en-us/dotnet/core/extensions/options) for configuration
* Observability via [OpenTelemetry](https://opentelemetry.io/) (with Azure Monitor integration)
* Azure hosting and deployment best practices

---

> If you are generating or fixing code, see `JobExecutionService.cs`, `Program.cs`, or the `Commands/` folder for CLI entry logic.