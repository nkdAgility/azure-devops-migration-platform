---
name: modular-monolith-check
description: Scans the codebase for Modular Monolith violations — cross-module coupling, missing module boundaries, and structural integrity issues — and produces a prioritised fix list.
---

# Skill: Modular Monolith Check

Use this skill when you need to audit the codebase for violations of module boundary rules, or before declaring a feature complete after touching module or shared infrastructure code.

---

## Role

When this skill is active, scan the specified scope (entire solution, single project, or single file) for violations of the Modular Monolith structural rules enforced by this platform. Produce a prioritised list of violations and, for each one, suggest the minimal safe fix. Do **not** apply fixes automatically unless explicitly instructed — report findings first.

---

## Boundary Model

```
┌──────────────────────────────────────────────────────────────────┐
│  DevOpsMigrationPlatform.Abstractions                            │
│  (shared interfaces, domain records, DTOs, enums)               │
└──────┬────────────────────────────────────────────────────┬──────┘
       │ depends on ▲ only                                  │ depends on ▲ only
┌──────▼──────────────┐                      ┌─────────────▼──────────┐
│  Export Modules      │                      │  Import Modules         │
│  (WorkItems, …)      │                      │  (WorkItems, …)         │
└──────────────────────┘                      └────────────────────────┘
       │ implemented by ▼                              │ implemented by ▼
┌──────────────────────────────────────────────────────────────────┐
│  DevOpsMigrationPlatform.Infrastructure                          │
│  (FileSystemArtefactStore, SDK adapters, …)                     │
└──────────────────────────────────────────────────────────────────┘
```

**The hard rule:** No module project may reference another module project directly. All cross-module communication must flow through `DevOpsMigrationPlatform.Abstractions`. Shared state must use `IArtefactStore` or `IStateStore`.

---

## Check 1 — Direct Project Reference Between Modules

**Smell:** A module's `.csproj` file lists another module project as a `<ProjectReference>` instead of depending only on `Abstractions`.

```xml
<!-- BAD — Export module coupled to Import module -->
<ProjectReference Include="..\DevOpsMigrationPlatform.WorkItems.Import\..." />
```

**Fix:** Remove the direct reference. Expose the shared contract through `DevOpsMigrationPlatform.Abstractions` and inject the dependency via an interface.

**How to find:**

```bash
grep -rn "ProjectReference" \
  src/ --include="*.csproj" \
  | grep -v "Abstractions\|Infrastructure\|AppHost\|ServiceDefaults\|MigrationAgent\|CLI\|Tests"
```

Any module-to-module `ProjectReference` that bypasses `Abstractions` is a violation.

---

## Check 2 — Module Exposes No Public Registration Entry Point

**Smell:** A module project lacks a `ServiceCollectionExtensions` or equivalent registration class that encapsulates all its DI registrations in a single, self-contained extension method.

```csharp
// BAD — registration scattered across CLI startup
services.AddSingleton<WorkItemExportOrchestrator>();
services.AddSingleton<WorkItemRevisionMapper>();
services.AddSingleton<WorkItemFieldFilter>();
// ... 20 more lines in Program.cs or equivalent
```

**Fix:** Create a `ServiceCollectionExtensions` in the module project that exposes one `Add<ModuleName>` extension method.

```csharp
// GOOD — module self-registers
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWorkItemsExportModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<WorkItemExportOrchestrator>();
        // ... all module-internal registrations
        return services;
    }
}
```

**How to find:**

```bash
grep -rn "AddSingleton\|AddScoped\|AddTransient" \
  src/DevOpsMigrationPlatform.CLI.Migration \
  src/DevOpsMigrationPlatform.MigrationAgent \
  --include="*.cs" \
  | grep -v "ServiceCollectionExtensions"
```

Any DI registration in host startup code that should belong in a module's own `ServiceCollectionExtensions` is a violation.

---

## Check 3 — Shared Type Declared Inside a Module

**Smell:** A record, interface, or enum that is used by more than one module or by the infrastructure layer is declared inside a single module project.

```csharp
// BAD — type used by multiple modules, but declared in WorkItems.Export
// File: DevOpsMigrationPlatform.WorkItems.Export/Models/WorkItemRevision.cs
public record WorkItemRevision(...); // ❌ must live in Abstractions
```

**Fix:** Move the type to `DevOpsMigrationPlatform.Abstractions` under the appropriate namespace.

**How to find:**

```bash
grep -rn "^public record \|^public interface \|^public enum " \
  src/ --include="*.cs" \
  --exclude-dir=DevOpsMigrationPlatform.Abstractions \
  --exclude-dir=DevOpsMigrationPlatform.Infrastructure \
  --exclude-dir=DevOpsMigrationPlatform.CLI.Migration \
  --exclude-dir=DevOpsMigrationPlatform.CLI.TfsMigration \
  --exclude-dir=DevOpsMigrationPlatform.AppHost \
  --exclude-dir=DevOpsMigrationPlatform.ServiceDefaults \
  --exclude-dir=DevOpsMigrationPlatform.MigrationAgent
```

Each hit must be verified: if any other project imports the type, it must live in `Abstractions`.

---

## Check 4 — Module Bypasses Abstractions for Cross-Cutting Concerns

**Smell:** A module directly instantiates a concrete implementation of a cross-cutting concern (checkpointing, telemetry, state storage) rather than using an injected interface.

```csharp
// BAD — module instantiates checkpoint service directly
public class WorkItemsImportJob
{
    public WorkItemsImportJob(IStateStore stateStore)
    {
        _checkpointing = new CheckpointingService(stateStore); // ❌
    }
}
```

**Fix:** Inject `ICheckpointingService` (or its factory) via constructor injection; let the DI container wire the concrete type.

```csharp
// GOOD — module receives the service via injection
public class WorkItemsImportJob
{
    public WorkItemsImportJob(ICheckpointingService checkpointing)
    {
        _checkpointing = checkpointing; // ✅
    }
}
```

**How to find:**

```bash
grep -rn "new CheckpointingService\|new PhaseTrackingService\|new PackageLoggerProvider" \
  src/ --include="*.cs" \
  --exclude-dir=DevOpsMigrationPlatform.Infrastructure
```

---

## Check 5 — Missing Concern-First Folder Organisation Within a Module

**Smell:** Module internals are organised by technical layer (Services, Models, Repositories) rather than by responsibility, making it hard to understand what the module does at a glance.

```
// BAD — layer-first structure inside a module
DevOpsMigrationPlatform.WorkItems.Export/
  Services/
    RevisionMapper.cs
    FieldFilter.cs
    AttachmentDownloader.cs
  Models/
    ...
```

**Fix:** Reorganise by concern so that all collaborating classes are co-located.

```
// GOOD — concern-first structure
DevOpsMigrationPlatform.WorkItems.Export/
  Orchestration/
    WorkItemExportOrchestrator.cs
  Revision/
    RevisionMapper.cs
    FieldFilter.cs
  Attachments/
    AttachmentDownloader.cs
```

**How to find:** Inspect each module project for generic folder names (`Services/`, `Models/`, `Helpers/`). Any module with such folders whose classes span multiple distinct responsibilities is a candidate violation.

---

## Severity Classification

| Severity | Criteria |
|---|---|
| **Critical** | Module directly references another module project — breaks independent deployability and testability |
| **High** | Shared type declared inside a module project — creates hidden coupling between modules |
| **Medium** | Module has no self-contained registration entry point — forces host to know module internals |
| **Low** | Layer-first folder structure inside a module — reduces discoverability without breaking runtime behaviour |

---

## Modular Monolith Check Checklist

Run this checklist after any code change that touches module projects, shared types, or DI registration:

- [ ] **Check 1**: No module project contains a `<ProjectReference>` to another module project.
- [ ] **Check 2**: Every module project exposes a single `Add<ModuleName>` extension method for self-registration.
- [ ] **Check 3**: All types shared between modules are declared in `DevOpsMigrationPlatform.Abstractions`.
- [ ] **Check 4**: No module instantiates cross-cutting services directly — all wired via constructor injection.
- [ ] **Check 5**: Module internals are organised by concern, not by technical layer.

All items must be checked before a feature or refactoring is declared complete. Any unchecked item is a blocking violation.
