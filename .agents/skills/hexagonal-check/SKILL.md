---
name: hexagonal-check
description: Scans the codebase for Hexagonal Architecture violations — infrastructure leakage into domain/module code — and produces a prioritised fix list.
---

# Skill: Hexagonal Architecture Check

Use this skill when you need to audit the codebase for boundary violations between domain/module logic and infrastructure concerns, or before declaring a feature complete after touching module or infrastructure code.

---

## Role

When this skill is active, scan the specified scope (entire solution, single project, or single file) for violations of the Hexagonal Architecture boundary rules enforced by this platform. Produce a prioritised list of violations and, for each one, suggest the minimal safe fix. Do **not** apply fixes automatically unless explicitly instructed — report findings first.

---

## Boundary Model

```
┌─────────────────────────────────────────────────────────────┐
│  DevOpsMigrationPlatform.Abstractions                       │
│  (interfaces, domain records, DTOs)                         │
└──────────────────────────┬──────────────────────────────────┘
                           │ depends on ▲ only
┌──────────────────────────▼──────────────────────────────────┐
│  Module code (Export/Import modules, Job Engine)            │
│  Only permitted dependencies: Abstractions + IOptions<T>    │
└──────────────────────────┬──────────────────────────────────┘
                           │ implemented by ▼
┌──────────────────────────▼──────────────────────────────────┐
│  Infrastructure (FileSystemArtefactStore,                   │
│  AzureBlobArtefactStore, AzureDevOps SDK adapters, …)       │
└─────────────────────────────────────────────────────────────┘
```

**The hard rule:** Module and domain code MUST NOT reference any type that lives in an infrastructure project. Infrastructure flows in only through injected interfaces.

---

## Check 1 — Concrete Store Reference in Module Code

**Smell:** A module or domain class directly constructs or names `FileSystemArtefactStore` or `AzureBlobArtefactStore`.

```csharp
// BAD — module coupled to filesystem implementation
public class WorkItemsExportModule
{
    private readonly FileSystemArtefactStore _store; // ❌ concrete type

    public WorkItemsExportModule(string basePath)
    {
        _store = new FileSystemArtefactStore(basePath); // ❌ direct construction
    }
}
```

**Fix:** Accept `IArtefactStore` via constructor injection.

```csharp
// GOOD — module depends only on the abstraction
public class WorkItemsExportModule
{
    private readonly IArtefactStore _store;

    public WorkItemsExportModule(IArtefactStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }
}
```

**How to find:**

```bash
grep -rn "FileSystemArtefactStore\|AzureBlobArtefactStore" \
  src/ --include="*.cs" \
  --exclude-dir=DevOpsMigrationPlatform.Infrastructure \
  --exclude-dir=DevOpsMigrationPlatform.AppHost
```

Any hit outside of `Infrastructure`, `AppHost`, and test-fixture registration files is a violation. Test-fixture DI registrations typically reside in files matching `*Fixture.cs`, `*Bootstrap.cs`, or `*ServiceCollectionExtensions.cs` inside `tests/` — hits in those files may be expected and should be reviewed in context.

---

## Check 2 — Direct Azure DevOps or TFS SDK Call in Module Code

**Smell:** A module instantiates or calls an Azure DevOps / TFS SDK client directly, rather than going through an abstraction interface.

```csharp
// BAD — SDK leak into module layer
public class WorkItemsImportModule
{
    public async Task ImportAsync(WorkItemRevision revision, CancellationToken ct)
    {
        var client = new WorkItemTrackingHttpClient(_uri, _credentials); // ❌
        await client.CreateWorkItemAsync(revision.Fields, revision.WorkItemType, ct);
    }
}
```

**Fix:** Wrap the SDK call in an infrastructure adapter and expose it behind an interface defined in `DevOpsMigrationPlatform.Abstractions`.

```csharp
// GOOD — module calls only its abstraction
public class WorkItemsImportModule
{
    private readonly IWorkItemImportService _importService;

    public async Task ImportAsync(WorkItemRevision revision, CancellationToken ct)
    {
        await _importService.CreateOrUpdateAsync(revision, ct); // ✅
    }
}
```

**How to find:**

```bash
grep -rn "WorkItemTrackingHttpClient\|VssConnection\|TfsTeamProjectCollection" \
  src/ --include="*.cs" \
  --exclude-dir=DevOpsMigrationPlatform.Infrastructure.AzureDevOps \
  --exclude-dir=DevOpsMigrationPlatform.Infrastructure.TfsObjectModel \
  --exclude-dir=DevOpsMigrationPlatform.CLI.TfsMigration
```

---

## Check 3 — Interface Defined Outside Abstractions Project

**Smell:** A public interface that is shared between modules or between a module and its infrastructure adapter is declared in an infrastructure or CLI project instead of `DevOpsMigrationPlatform.Abstractions`.

```csharp
// BAD — interface in wrong project
// File: DevOpsMigrationPlatform.Infrastructure/Services/IWorkItemImportService.cs
public interface IWorkItemImportService { ... } // ❌ belongs in Abstractions
```

**Fix:** Move the interface to `DevOpsMigrationPlatform.Abstractions` under the appropriate namespace.

**How to find:**

```bash
grep -rn "^public interface " \
  src/DevOpsMigrationPlatform.Infrastructure \
  src/DevOpsMigrationPlatform.CLI.Migration \
  src/DevOpsMigrationPlatform.MigrationAgent \
  --include="*.cs"
```

Each result should be evaluated: if the interface is consumed by more than one project, it must live in `Abstractions`.

---

## Check 4 — Direct File I/O in Module Code

**Smell:** A module uses `System.IO` types (`File`, `Directory`, `FileStream`, `Path`) or `System.IO.Abstractions` directly, bypassing `IArtefactStore`.

```csharp
// BAD — direct file I/O in module
public async Task WriteRevisionAsync(string path, WorkItemRevision revision)
{
    var json = JsonSerializer.Serialize(revision);
    await File.WriteAllTextAsync(path, json); // ❌
}
```

**Fix:** Use `IArtefactStore.WriteAsync` or `IArtefactStore.WriteBinaryAsync`.

```csharp
// GOOD — write via store abstraction
public async Task WriteRevisionAsync(string relativePath, WorkItemRevision revision, CancellationToken ct)
{
    using var stream = /* serialise to MemoryStream */;
    await _artefactStore.WriteAsync(relativePath, stream, ct); // ✅
}
```

**How to find:**

```bash
grep -rn "System\.IO\.\|File\.\|Directory\.\|FileStream\|StreamReader\|StreamWriter" \
  src/ --include="*.cs" \
  --exclude-dir=DevOpsMigrationPlatform.Infrastructure \
  --exclude-dir=DevOpsMigrationPlatform.CLI.TfsMigration \
  --exclude-dir=DevOpsMigrationPlatform.CLI.Migration
```

Hits in module/domain code are violations. Hits in `Infrastructure` implementations are expected.

---

## Check 5 — Domain Type Declared in Infrastructure or CLI Project

**Smell:** A record, DTO, or enum that forms part of the migration domain model is declared in an infrastructure or CLI project instead of `DevOpsMigrationPlatform.Abstractions`.

```csharp
// BAD — domain type in wrong project
// File: DevOpsMigrationPlatform.Infrastructure/Models/WorkItemRevision.cs
public record WorkItemRevision(...); // ❌ should be in Abstractions
```

**Fix:** Move the type to `DevOpsMigrationPlatform.Abstractions` and update all references. If the type is infrastructure-specific (e.g., an EF Core entity), it may remain in the infrastructure project but must not be passed across the module boundary.

**How to find:**

```bash
grep -rn "^public record \|^public sealed record \|^public class \|^public enum " \
  src/DevOpsMigrationPlatform.Infrastructure/Models \
  --include="*.cs"
```

Compare against what `DevOpsMigrationPlatform.Abstractions` already exports. Any shared domain concept missing from `Abstractions` is a candidate violation.

---

## Check 6 — Environment Branching in Module or Domain Code

**Smell:** A module uses `if (env == "Production")` or reads `Environment.GetEnvironmentVariable` to vary behaviour.

```csharp
// BAD — environment branching in module
if (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Development")
{
    _logger.LogDebug("Skipping retry in dev mode"); // ❌
}
```

**Fix:** Externalise the flag into `IOptions<T>` and inject it.

```csharp
// GOOD — behaviour controlled by configuration
if (_options.Value.SkipRetryInDevelopment) { ... } // ✅
```

**How to find:**

```bash
grep -rn "Environment\.GetEnvironmentVariable\|ASPNETCORE_ENVIRONMENT\|IHostEnvironment" \
  src/ --include="*.cs" \
  --exclude-dir=DevOpsMigrationPlatform.AppHost \
  --exclude-dir=DevOpsMigrationPlatform.ServiceDefaults
```

---

## Check 7 — `Console` Usage Outside CLI/TUI Boundary

**Smell:** Module, Job Engine, or infrastructure adapter code writes to `System.Console`.

```csharp
// BAD — Console output in module code
Console.WriteLine($"Processing work item {workItemId}"); // ❌
```

**Fix:** Use `ILogger<T>` for diagnostics and `IProgressSink` for progress events.

```csharp
// GOOD
_logger.LogDebug("Processing work item {WorkItemId}", workItemId); // ✅
await _progressSink.EmitAsync(ProgressEvent.WorkItemProcessed(workItemId), ct);
```

**How to find:**

```bash
grep -rEn "Console\.(Write|Read)" \
  src/ --include="*.cs" \
  --exclude-dir=DevOpsMigrationPlatform.CLI.Migration \
  --exclude-dir=DevOpsMigrationPlatform.CLI.TfsMigration \
  --exclude-dir=DevOpsMigrationPlatform.ServiceDefaults
```

---

## Severity Classification

| Severity | Criteria |
|---|---|
| **Critical** | Concrete infrastructure type leaked into module/domain code; breaks store-substitutability (cloud vs local) |
| **High** | SDK client called directly from module; domain type declared in wrong project |
| **Medium** | Interface defined outside Abstractions but consumed by multiple projects |
| **Low** | `Console.Write` in module code; environment branching |

---

## Hexagonal Check Checklist

Run this checklist after any code change that touches module, infrastructure, or abstraction layers:

- [ ] **Check 1**: No `FileSystemArtefactStore` or `AzureBlobArtefactStore` referenced in module/domain code.
- [ ] **Check 2**: No Azure DevOps or TFS SDK types instantiated in module/domain code.
- [ ] **Check 3**: All shared public interfaces live in `DevOpsMigrationPlatform.Abstractions`.
- [ ] **Check 4**: No `System.IO` or `File.*` calls in module/domain code — all file access via `IArtefactStore`.
- [ ] **Check 5**: All shared domain records and DTOs declared in `DevOpsMigrationPlatform.Abstractions`.
- [ ] **Check 6**: No `Environment.GetEnvironmentVariable` or environment-name branching in module/domain code.
- [ ] **Check 7**: No `Console.Write*` in module, Job Engine, or infrastructure adapter code.

All items must be checked before a feature or refactoring is declared complete. Any unchecked item is a blocking violation.
