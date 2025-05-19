# Azure DevOps Migration Platform

## Overview

This repository contains tools and libraries used to migrate data to and from Azure DevOps and on-premises Team Foundation Server (TFS). The solution is composed of multiple .NET projects that expose command line utilities as well as reusable libraries. The core components include:

- **MigrationPlatform.Abstractions** – shared models, options and services used across the solution.
- **MigrationPlatform.Infrastructure** – common infrastructure and helper services.
- **MigrationPlatform.Infrastructure.AzureDevOps** – Azure DevOps specific integrations.
- **MigrationPlatform.Infrastructure.TfsObjectModel** – APIs for interacting with TFS using the TFS object model.
- **MigrationPlatform.CLI.Migration** – CLI entry point for discovery and configuration commands.
- **MigrationPlatform.CLI.TfsExport** – CLI entry point for exporting data from TFS.

## Prerequisites

- [.NET SDK 9.0](https://dotnet.microsoft.com/) or later.
- .NET Framework 4.8.1 runtime (required for components targeting `net481`).

## Building the Solution

Run the following command from the repository root to restore dependencies and build all projects:

```bash
 dotnet build azure-devops-migration-platform.sln
```

## Running the CLI Tools

Use the `dotnet run` command to execute the CLI projects directly.

To run the general migration tool:

```bash
 dotnet run --project src/MigrationPlatform.CLI.Migration -- <command> [options]
```

To run the TFS export tool:

```bash
 dotnet run --project src/MigrationPlatform.CLI.TfsExport -- export [options]
```

Each CLI supports `--help` for additional commands and parameters.

