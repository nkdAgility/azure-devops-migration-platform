# GitHub Copilot Coding Agent Instructions

This document provides explicit instructions to the GitHub Copilot Coding Agent for correctly interacting with and assisting development on the Migration Platform.

## Repository Overview

The project is structured with clear separation into components targeting either `.NET 10` (cross-platform) or `.NET Framework 4.8` (Windows-only). The Coding Agent should primarily focus on the cross-platform components targeting `.NET 10` but can provide suggestions for legacy code improvements without attempting builds or tests.

## Project Structure

```
src/
├── Common (multi-target: net481;net10.0)
│   ├── MigrationPlatform.Abstractions
│   ├── MigrationPlatform.Infrastructure
│
├── ControlPlane (ASP.NET Core Web API, net10.0)
│   └── MigrationPlatform.ControlPlane
│
├── Agent (Console/Worker Service, net10.0)
│   └── MigrationPlatform.Agent
│
├── AzureDevOps (CLI tooling, net10.0)
│   ├── MigrationPlatform.Infrastructure.AzureDevOps
│   └── MigrationPlatform.CLI.Migration
│
└── TfsObjectModel (Windows-only, net481 - SUGGESTIONS ONLY, NO BUILD/TEST)
    ├── MigrationPlatform.Infrastructure.TfsObjectModel
    └── MigrationPlatform.CLI.TfsExport
```

## What to Assist With:

* **Common Libraries** (`MigrationPlatform.Abstractions`, `MigrationPlatform.Infrastructure`)

  * Multi-targeted (`net481;net10.0`).
  * Ensure compatibility across both frameworks.

* **Control Plane** (`MigrationPlatform.ControlPlane`)

  * ASP.NET Core Web API (targeting `.NET 10`).
  * REST API design, controller implementation, business logic, and infrastructure.

* **Agent** (`MigrationPlatform.Agent`)

  * Long-running worker or console app (targeting `.NET 10`).
  * Job execution, polling logic, state management, resumable operations.

* **AzureDevOps CLI** (`MigrationPlatform.CLI.Migration`)

  * Command-line tooling for migrations (targeting `.NET 10`).
  * CLI parsing, commands implementation, input validation, interaction with control plane and infrastructure.

* **AzureDevOps Infrastructure** (`MigrationPlatform.Infrastructure.AzureDevOps`)

  * Data access, REST API clients, serialization, data mapping, Azure DevOps interactions (targeting `.NET 10`).

## Suggestions Only (No Build/Test):

* **TfsObjectModel** directory (`MigrationPlatform.Infrastructure.TfsObjectModel`, `MigrationPlatform.CLI.TfsExport`):

  * Built only for `.NET Framework 4.8` and Windows.
  * Provide suggestions for improvements, refactoring, code quality, and readability.
  * Do not attempt builds or provide guidance for running or executing tests.

## Coding Guidelines:

* Use modern `.NET 10` idioms, syntax, and APIs.
* Ensure cross-platform compatibility.
* Apply SOLID principles, clean code best practices, and explicit dependency management.
* Use explicit `<ProjectReference>` tags in `.csproj` for inter-project dependencies.

## Testing Guidance:

* Unit tests reside next to their corresponding components (e.g., `MigrationPlatform.Agent.Tests` adjacent to `MigrationPlatform.Agent`).
* Encourage high test coverage and meaningful, focused unit tests.
* Prefer xUnit, Moq, and FluentAssertions for testing.

## Environment:

* VS Code centric development.
* `.sln` files are optional; builds primarily use explicit project references.
* Focus on providing guidance compatible with VS Code debugging (`launch.json`) and terminal builds (`dotnet build`).

## Summary of Scope for Copilot Agent:

* ✅ Assist explicitly with `.NET 10` cross-platform components.
* ✅ Provide suggestions for improving `.NET Framework 4.8` legacy code without builds or tests.
* ❌ Do not attempt to build or test legacy components.

These instructions ensure the GitHub Copilot Coding Agent effectively contributes to the project, maintaining clarity, productivity, and alignment with the project's overall architecture and objectives.
