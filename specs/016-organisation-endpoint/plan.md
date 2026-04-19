# Implementation Plan: OrganisationEndpoint

**Branch**: `016-organisation-endpoint` | **Date**: 2026-04-17 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/016-organisation-endpoint/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Replace `DiscoveryJobOrganisation` codebase-wide. Introduce `MigrationEndpointOptions` as the abstract polymorphic endpoint base accepted by all Abstractions-level service interfaces (replacing separate `string url`/`string pat` parameters). Retain `OrganisationEndpoint` as the ADO/TFS-specific resolved connection context for `IAzureDevOpsClientFactory`. Introduce `ScopedOrganisationEndpoint` as the job-contract wrapper pairing `MigrationEndpointOptions` with `Projects`. Pure structural refactor — no behavioural changes.

## Technical Context

**Language/Version**: C# 10+, .NET 10 (Abstractions multi-targeted: net481;net10.0)  
**Primary Dependencies**: No new dependencies — uses existing `DevOpsMigrationPlatform.Abstractions`  
**Storage**: N/A — no persistence changes  
**Testing**: MSTest + Reqnroll.MSTest + Moq  
**Target Platform**: Windows/Linux (multi-targeted)  
**Project Type**: Library refactor across Abstractions, Infrastructure, and CLI layers  
**Performance Goals**: N/A — pure signature refactor, no runtime behaviour change  
**Constraints**: net481 compatibility in Abstractions (no C# `record` keyword)  
**Scale/Scope**: 7 source files with type references + 7 interfaces with ~11 methods to update

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **Package-First (I):** N/A — no package read/write changes.
- [x] **Streaming (II):** N/A — no streaming logic affected.
- [x] **WorkItems Layout (III):** N/A — no folder structure changes.
- [x] **Checkpointing (IV):** N/A — no checkpoint changes.
- [x] **Module Isolation (V):** New types live in `DevOpsMigrationPlatform.Abstractions`. No concrete store references added. ✓
- [x] **Separation of Planes (VI):** CLI builds `ScopedOrganisationEndpoint`, passes to control plane. No migration logic in CLI. ✓
- [x] **Determinism (VII):** No schema or config version changes needed (property names unchanged in JSON). ✓
- [x] **ATDD-First (VIII):** Spec has 3 user stories with Given/When/Then acceptance scenarios. ✓
- [x] **SOLID & DI (IX):** New types are sealed, init-only. Interfaces defined in Abstractions. No new service registrations needed. ✓

**Post-Phase-1 re-check**: All gates still pass. No violations introduced by the design.

## Project Structure

### Documentation (this feature)

```text
specs/016-organisation-endpoint/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output — 5 research decisions
├── data-model.md        # Phase 1 output — type definitions and interface changes
├── quickstart.md        # Phase 1 output — before/after code examples
├── discrepancies.md     # Architecture doc discrepancies to resolve
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   ├── Models/
│   │   ├── OrganisationEndpoint.cs              # NEW — ADO/TFS resolved connection context
│   │   ├── OrganisationEndpointAuthentication.cs # NEW — replaces DiscoveryJobAuthentication
│   │   ├── ScopedOrganisationEndpoint.cs         # NEW — job-contract wrapper (Endpoint: MigrationEndpointOptions)
│   │   ├── DiscoveryJob.cs                       # MODIFIED — Organisations type changed
│   │   └── DiscoveryJobOrganisation.cs           # DELETED
│   ├── Options/
│   │   ├── MigrationEndpointOptions.cs           # NEW — abstract polymorphic endpoint base
│   │   └── OrganisationEntry.cs                  # MODIFIED — now abstract, gains abstract ToEndpointOptions()
│   └── Services/
│       ├── IWorkItemDiscoveryService.cs          # MODIFIED — MigrationEndpointOptions param
│       ├── IWorkItemQueryWindowStrategy.cs       # MODIFIED — MigrationEndpointOptions param
│       ├── IProjectDiscoveryService.cs           # MODIFIED — MigrationEndpointOptions param
│       ├── ICatalogService.cs                    # MODIFIED — MigrationEndpointOptions param
│       ├── IWorkItemLinkAnalysisService.cs       # MODIFIED — MigrationEndpointOptions param
│       ├── IWorkItemCommentSourceFactory.cs      # MODIFIED — MigrationEndpointOptions param
│       ├── IInventoryServiceFactory.cs           # MODIFIED — ScopedOrganisationEndpoint param
│       └── IDependencyDiscoveryServiceFactory.cs # MODIFIED — ScopedOrganisationEndpoint param
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
│   ├── IAzureDevOpsClientFactory.cs              # MODIFIED — OrganisationEndpoint param (ADO-specific)
│   ├── AzureDevOpsClientFactory.cs               # MODIFIED — implementation update
│   └── Factories/
│       ├── InventoryServiceFactory.cs            # MODIFIED — ScopedOrganisationEndpoint
│       └── DependencyDiscoveryServiceFactory.cs  # MODIFIED — ScopedOrganisationEndpoint
└── DevOpsMigrationPlatform.CLI.Migration/
    └── Commands/Discovery/
        ├── InventoryCommand.cs                   # MODIFIED — constructs ScopedOrganisationEndpoint
        └── DependencyCommand.cs                  # MODIFIED — constructs ScopedOrganisationEndpoint

tests/
└── (existing tests — update mock setup to use new types where needed)
```

**Structure Decision**: No new projects. Changes distributed across Abstractions, Infrastructure, and CLI projects. New `MigrationEndpointOptions` abstract base in `Abstractions/Options/`, three new files in `Abstractions/Models/`, one file deleted, ~20 files modified.

## Complexity Tracking

No constitution violations. No additional complexity beyond what the spec requires.

## Design Decisions (from research.md)

| # | Decision | Rationale |
|---|----------|-----------|
| 1 | `Projects` moves to `ScopedOrganisationEndpoint` wrapper on `DiscoveryJob` | Keeps endpoint types clean; projects are job-scope, not connection-scope. `ApiVersion` stays on `OrganisationEndpoint` since it is a connection property. |
| 2 | No JSON changes needed for scenario files | Property names are stable; only C# class names change |
| 3 | `IAzureDevOpsClientFactory` accepts `OrganisationEndpoint` (resolved ADO/TFS type) | Push the resolved type all the way down to the SDK boundary |
| 4 | `AuthenticationType` enum (not string) on `OrganisationEndpointAuthentication` | Type safety; matches existing config-layer enum |
| 5 | Sealed classes with init-only properties (not records) | net481 compatibility in multi-targeted Abstractions |
| 6 | `MigrationEndpointOptions` abstract base at Abstractions interface boundary | Evolved during 017 (Simulated Infrastructure) — enables polymorphic dispatch for ADO, TFS, and Simulated connectors without modifying service interfaces (OCP) |
| 7 | `OrganisationEntry` became abstract with `ToEndpointOptions()` | Each connector subclass provides its own config-to-endpoint mapping |

## Implementation Order

The refactor is executed in dependency order — types first, then interfaces, then implementations, then callers:

1. **New types**: `MigrationEndpointOptions` (abstract base), `OrganisationEndpoint` (with `ApiVersion`), `OrganisationEndpointAuthentication`, `ScopedOrganisationEndpoint`
2. **Config conversion**: `OrganisationEntry` becomes abstract with `ToEndpointOptions()` returning `MigrationEndpointOptions`
3. **Interface updates**: All Abstractions-level service interfaces accept `MigrationEndpointOptions`
4. **Update `DiscoveryJob`**: Change `Organisations` property type
5. **Delete old types**: `DiscoveryJobOrganisation`, `DiscoveryJobAuthentication`
6. **Implementation updates**: All concrete service implementations resolve `MigrationEndpointOptions` → `OrganisationEndpoint` internally; `IAzureDevOpsClientFactory` accepts `OrganisationEndpoint`
7. **CLI updates**: `InventoryCommand`, `DependencyCommand` — construct `ScopedOrganisationEndpoint` via `entry.ToEndpointOptions()`
8. **Test updates**: Fix any compilation errors in test mocks/fakes
9. **Verification**: `dotnet clean && dotnet build --no-incremental`, `dotnet test`, scenario run
