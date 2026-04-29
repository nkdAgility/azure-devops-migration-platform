# Coding Standards — Code Examples

This file contains illustrative examples for the rules defined in `coding-standards.md`.
Agents read `coding-standards.md`; humans use this file to understand intent.

---

## SOLID: Single Responsibility

❌ **REJECT:** Commands with business logic
```csharp
public class ExportCommand : Command<ExportSettings>
{
    public override int Execute(CommandContext context, ExportSettings settings)
    {
        var modules = LoadModules();
        ExecuteMigration(modules); // VIOLATION: business logic in CLI
        return 0;
    }
}
```

✅ **ACCEPT:** Commands delegate to services
```csharp
public class ExportCommand : CommandBase<ExportSettings>
{
    private readonly IMigrationOrchestrator _orchestrator;
    protected override async Task<int> ExecuteInternalAsync(...)
        => await _orchestrator.ExecuteExportAsync(...);
}
```

---

## SOLID: Open/Closed

❌ **REJECT:** Modifying a class to add new export types
```csharp
public class ExportService
{
    public void Export(string type)
    {
        if (type == "WorkItems") { /* ... */ }
        else if (type == "GitRepos") { /* ... */ }
    }
}
```

✅ **ACCEPT:** New implementations without changing existing code
```csharp
public interface IExportModule { }
public class WorkItemsExportModule : IExportModule { }
public class GitReposExportModule : IExportModule { }
```

---

## SOLID: Liskov Substitution

❌ **REJECT:** Inconsistent null/throw contracts between implementations
```csharp
// FileSystem throws, Blob returns null — callers can't substitute
public async Task<Stream> ReadAsync(string path)
{
    if (!File.Exists(path)) throw new FileNotFoundException();
    return File.OpenRead(path);
}
```

✅ **ACCEPT:** Identical contracts across all implementations
```csharp
public async Task<Stream?> ReadAsync(string path)
{
    // Both FileSystem and Blob return null if not found, throw on access errors
}
```

---

## SOLID: Interface Segregation

❌ **REJECT:** Fat interfaces with mixed concerns
```csharp
public interface IArtefactStore
{
    Task WriteAsync(string path, Stream content);
    Task ClearCacheAsync();    // not all stores need this
    Task CompressAsync(string path); // not all stores need this
}
```

✅ **ACCEPT:** Focused interfaces
```csharp
public interface IArtefactStore
{
    Task WriteAsync(string path, Stream content);
    Task<Stream?> ReadAsync(string path);
    IAsyncEnumerable<string> EnumerateAsync(string prefix);
}
```

---

## SOLID: Dependency Inversion

❌ **REJECT:** Modules that new up concrete types
```csharp
public class WorkItemsExportModule
{
    public WorkItemsExportModule()
    {
        _store = new FileSystemArtefactStore("./package"); // VIOLATION
    }
}
```

✅ **ACCEPT:** Constructor injection of abstractions
```csharp
public class WorkItemsExportModule
{
    private readonly IArtefactStore _store;
    public WorkItemsExportModule(IArtefactStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));
}
```

---

## Async: No Blocking

❌ **REJECT:**
```csharp
var result = _store.ReadAsync(path).Result;
var items = GetItemsAsync(token).GetAwaiter().GetResult();
```

✅ **ACCEPT:**
```csharp
var result = await _store.ReadAsync(path, cancellationToken);
await foreach (var item in GetItemsAsync(cancellationToken)) { ... }
```

---

## Integration: No Direct SDK Calls in Modules

❌ **REJECT:**
```csharp
public class WorkItemsImportModule
{
    public async Task ImportAsync(...)
    {
        var client = new WorkItemTrackingHttpClient(...); // VIOLATION: SDK in module
        await client.CreateWorkItemAsync(...);
    }
}
```

✅ **ACCEPT:**
```csharp
public class WorkItemsImportModule
{
    private readonly IWorkItemImportService _importService;
    public async Task ImportAsync(WorkItemRevision revision, CancellationToken ct)
        => await _importService.CreateOrUpdateAsync(revision, ct);
}
```

---

## Observability: O-1 Activity Spans

```csharp
using var activity = s_activitySource.StartActivity("teams.export");
activity?.SetTag("module", "Teams");
// ... do work ...
activity?.SetTag("teams.count", count);
```

---

## Observability: O-2 Business Metrics

```csharp
_migrationMetrics?.IncrementTeamExportInFlight(tags);
var sw = Stopwatch.StartNew();
try
{
    // ... do work ...
    _migrationMetrics?.RecordTeamExportCount(tags);
}
catch { _migrationMetrics?.RecordTeamExportError(tags); throw; }
finally
{
    sw.Stop();
    _migrationMetrics?.DecrementTeamExportInFlight(tags);
    _migrationMetrics?.RecordTeamExportDuration(sw.Elapsed.TotalMilliseconds, tags);
}
```

---

## Observability: O-4 ProgressEvent Emission

```csharp
// Start
_progressSink?.Emit(new ProgressEvent
{
    Module = ModuleName,
    Stage = "Export",
    Message = $"[Teams] Starting export for project '{project}'."
});

// Per-item / per-batch (≤50 items)
_progressSink?.Emit(new ProgressEvent
{
    Module = ModuleName,
    Stage = "Export",
    Message = $"[Teams] Exported {count} teams.",
    Metrics = new JobMetrics
    {
        Migration = new MigrationCounters
        {
            Teams = new ModuleCounters { Completed = count }
        }
    }
});
```

> ⚠️ `ProgressEvent.Metrics` is only populated by the TFS subprocess (net481).
> For .NET 10 agents it is always `null`.
> CLI MUST read counters from `GET /jobs/{id}/telemetry`, NOT from `ProgressEvent.Metrics`.
