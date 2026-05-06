# Quickstart: Implementing a New ICapture Handler

**Feature context**: `032-icapture-interface`  
**Audience**: Developer adding a new per-project capture operation (not a full migration module)

---

## When to Use ICapture vs IModule

| You need… | Use |
|-----------|-----|
| Per-project data discovery only (no export, import, validate phases) | `ICapture` |
| Full migration lifecycle (export + import + validate) with optional capture | `IModule : ICapture` |

`ICapture` is the minimum viable contract. Pure `ICapture` handlers are simpler to write,
have no unused method obligations, and are not affected by module phase flags.

---

## Step 1 — Implement `ICapture`

Create your class in `DevOpsMigrationPlatform.Infrastructure.Agent/Capture/`:

```csharp
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Capture;

/// <summary>
/// Captures [your domain] data per org+project into the artefact store.
/// </summary>
public sealed class MyCapture : ICapture
{
    private static readonly ActivitySource ActivitySource =
        new(WellKnownActivitySourceNames.Discovery);

    private readonly IMyService _service;
    private readonly ILogger<MyCapture> _logger;
    private readonly IPlatformMetrics? _metrics;
    private readonly IProgressSink? _progressSink;

    public MyCapture(
        IMyService service,
        ILogger<MyCapture> logger,
        IPlatformMetrics? metrics = null,
        IProgressSink? progressSink = null)
    {
        _service = service;
        _logger = logger;
        _metrics = metrics;
        _progressSink = progressSink;
    }

    // Name must match second dot-segment of the task ID:
    // "myfeature" for "capture.myfeature.{org}.{project}"
    public string Name => "myfeature";

    public async Task CaptureAsync(InventoryContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var tags = new MetricsTagList
        {
            { "job.id", context.Job.JobId },
            { "org.url", context.SourceEndpoint?.ResolvedUrl },
            { "project.name", context.Project }
        };

        using var activity = ActivitySource.StartActivity("capture.myfeature");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("org.url", context.SourceEndpoint?.ResolvedUrl);
        activity?.SetTag("project.name", context.Project);
        activity?.SetTag("capture.handler", Name);

        _logger.LogInformation(
            "Capture started for {Org}/{Project}",
            context.SourceEndpoint?.ResolvedUrl, context.Project);

        _progressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Capturing",
            Message = $"Capturing myfeature for {context.Project}",
            Timestamp = DateTimeOffset.UtcNow,
            TaskId = context.Job.JobId
        });

        try
        {
            // Do your capture work here. Write output via context.ArtefactStore.
            // Example:
            // var data = await _service.FetchAsync(context.SourceEndpoint, context.Project, ct);
            // await context.ArtefactStore.WriteAsync($"myfeature/{org}/{project}/data.json", json, ct);

            _logger.LogInformation(
                "Capture completed for {Org}/{Project} in {DurationMs}ms",
                context.SourceEndpoint?.ResolvedUrl, context.Project,
                sw.Elapsed.TotalMilliseconds);

            _progressSink?.Emit(new ProgressEvent
            {
                Module = Name,
                Stage = "Captured",
                Message = $"Captured myfeature for {context.Project}",
                Timestamp = DateTimeOffset.UtcNow,
                TaskId = context.Job.JobId
            });
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex,
                "Capture failed for {Org}/{Project}: {ErrorType} {ErrorMessage}",
                context.SourceEndpoint?.ResolvedUrl, context.Project,
                ex.GetType().Name, ex.Message);

            _progressSink?.Emit(new ProgressEvent
            {
                Module = Name,
                Stage = "Failed",
                Message = $"Capture failed for {context.Project}",
                Timestamp = DateTimeOffset.UtcNow,
                TaskId = context.Job.JobId
            });

            throw;
        }
    }
}
```

---

## Step 2 — Register in DI

Add a dedicated extension method. Do **not** scatter `AddSingleton` calls in `Program.cs`:

```csharp
// In Infrastructure.Agent/ServiceCollectionExtensions.cs or a dedicated file:

public static IServiceCollection AddMyFeatureCaptureServices(this IServiceCollection services)
{
    // Register as ICapture only — NOT as IModule
    services.AddSingleton<ICapture, MyCapture>();

    // Register your dependencies
    services.AddSingleton<IMyService, MyService>();

    return services;
}
```

Then call it from the host:

```csharp
// In MigrationAgent/Program.cs:
builder.Services.AddMyFeatureCaptureServices();
```

---

## Step 3 — Name Convention

Ensure `ICapture.Name` matches the second dot-segment of task IDs emitted by the plan builder:

| ICapture.Name | Task ID pattern emitted by plan builder |
|---------------|----------------------------------------|
| `"myfeature"` | `capture.myfeature.{org}.{project}` |

If the plan builder does not emit tasks for your handler, you must update
`IJobExecutionPlanBuilder` to include your handler's tasks in the Dependencies plan.

---

## Step 4 — Add Connector Coverage

Per Constitution Principle XI, every feature that interacts with source systems must support
**all three connectors** (or document a TFS exemption with API rationale):

| Connector | Action |
|-----------|--------|
| AzureDevOps | Implement using Azure DevOps REST API via `IMyService` |
| Simulated | Create `SimulatedMyFeatureService : IMyService` in `Infrastructure.Simulated`; register keyed `"Simulated"` |
| TFS | Implement or document exemption with graceful skip |

---

## Step 5 — Add Observability

For every new capture operation, the minimum required observability is:

| Signal | Requirement |
|--------|-------------|
| O-1 Trace span | `ActivitySource.StartActivity("capture.{name}")` with `job.id`, `org.url`, `project.name` tags |
| O-2 Metrics | At minimum: count (Counter), duration_ms (Histogram), errors (Counter) in `WellKnownAgentMetricNames` |
| O-3 Logging | `Information` at start and completion; `Error` on failure — structured params only |
| O-4 ProgressSink | `Stage = "Capturing"` at start; `Stage = "Captured"` on success; `Stage = "Failed"` on error |

---

## Step 6 — Write Tests

1. **Unit test**: Verify `CaptureAsync` calls the underlying service with correct `org.url` and `project`.
2. **Unit test**: Verify span name, metrics calls, log events, and `IProgressSink.Emit` calls via mocks.
3. **Simulated integration test**: Build a Dependencies job plan with Simulated source → run → verify output artefact written.
4. **ATDD acceptance test**: Add a Given/When/Then scenario in `features/capture/{name}/` and implement via the ATDD inner loop.

---

## Reference Implementation

See `DependencyCapture` as the canonical example of a pure `ICapture` handler:
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Capture/DependencyCapture.cs`
- `tests/.../Capture/DependencyCaptureTests.cs`
