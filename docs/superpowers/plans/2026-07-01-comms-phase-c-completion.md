# Comms Phase C Completion — Remove All Legacy Agent→CP Channels

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Eliminate every legacy Agent→ControlPlane communication path so the only channel is `UnifiedWorkerEventWriter` → `POST /workers/{workerId}/events`.

**Architecture:** `ControlPlaneTelemetryTimer` (metrics/snapshot) and `AgentWorkerBase.SignalTerminalAsync` (terminal signal) both bypass `UnifiedWorkerEventWriter` and POST directly to old shim endpoints. Redirect them through the unified writer, then delete all dead code (`ControlPlaneProgressSink`, `ControlPlaneTelemetryClient`, the logger fallback path) and remove the now-unused DI registrations.

**Tech Stack:** .NET 10, `System.Threading.Channels`, ASP.NET Core BackgroundService, `System.Text.Json`.

**Branch:** `update-for-comms`. Commit after every task — never merge.

---

## Affected files

| File | Change |
|------|--------|
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/UnifiedWorkerEventWriter.cs` | Add `EnqueueMetrics` and `EnqueueSnapshot` public methods |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneTelemetryTimer.cs` | Replace `_client.PushMetricsAsync/PushSnapshotAsync` with `_writer.EnqueueMetrics/EnqueueSnapshot` |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/AgentWorkerBase.cs` | Replace `SignalTerminalAsync` POST with `_writer.EnqueueTerminal` + `FlushAsync` |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneLoggerProvider.cs` | Delete lines 168–201 (legacy HTTP fallback path) |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneProgressSink.cs` | **Delete file** |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneTelemetryClient.cs` | **Delete file** |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/TelemetryServiceExtensions.cs` | Remove `AddControlPlaneTelemetryClient` and `AddControlPlaneProgressSink` methods |
| `src/DevOpsMigrationPlatform.Infrastructure.Agent/CoreAgentServiceExtensions.cs` | Remove `ControlPlaneTelemetryTimer` registration, remove `AddControlPlaneTelemetryClient` call, update docstring |
| `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/IControlPlaneTelemetryClient.cs` | **Delete file** (interface no longer has an implementation) |

---

## Task 1: Add `EnqueueMetrics` and `EnqueueSnapshot` to `UnifiedWorkerEventWriter`

**Files:**
- Modify: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/UnifiedWorkerEventWriter.cs`

- [ ] **Step 1.1: Add the two enqueue methods**

Open `UnifiedWorkerEventWriter.cs`. After the existing `EnqueueTasks` method (line 110), add:

```csharp
public void EnqueueMetrics(JobMetrics metrics)
    => Enqueue(WorkerEventKind.Metrics, metrics);

public void EnqueueSnapshot(JobSnapshot snapshot)
    => Enqueue(WorkerEventKind.Snapshot, snapshot);
```

`JobMetrics` and `JobSnapshot` are already imported via `DevOpsMigrationPlatform.Abstractions.ControlPlaneApi` — no new using statements needed.

- [ ] **Step 1.2: Build to verify**

```powershell
dotnet build src/DevOpsMigrationPlatform.Infrastructure.Agent/DevOpsMigrationPlatform.Infrastructure.Agent.csproj --no-restore
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 1.3: Commit**

```powershell
git add src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/UnifiedWorkerEventWriter.cs
git commit -m "feat: add EnqueueMetrics and EnqueueSnapshot to UnifiedWorkerEventWriter"
```

---

## Task 2: Redirect `ControlPlaneTelemetryTimer` through `UnifiedWorkerEventWriter`

**Files:**
- Modify: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneTelemetryTimer.cs`

- [ ] **Step 2.1: Replace `IControlPlaneTelemetryClient` with `UnifiedWorkerEventWriter`**

Replace the entire file content with:

```csharp
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;

/// <summary>
/// Background service that enqueues the latest <see cref="JobMetrics"/> and
/// <see cref="JobSnapshot"/> into <see cref="UnifiedWorkerEventWriter"/> on a
/// configurable interval while a lease is held.
/// </summary>
public sealed class ControlPlaneTelemetryTimer : BackgroundService
{
    private readonly IJobMetricsStore _metricsStore;
    private readonly IJobSnapshotStore _snapshotStore;
    private readonly UnifiedWorkerEventWriter _writer;
    private readonly IOptions<TelemetryOptions> _options;
    private readonly ILogger<ControlPlaneTelemetryTimer> _logger;

    public ControlPlaneTelemetryTimer(
        IJobMetricsStore metricsStore,
        IJobSnapshotStore snapshotStore,
        UnifiedWorkerEventWriter writer,
        IOptions<TelemetryOptions> options,
        ILogger<ControlPlaneTelemetryTimer> logger)
    {
        _metricsStore = metricsStore;
        _snapshotStore = snapshotStore;
        _writer = writer;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogDebug("ControlPlaneTelemetryTimer started.");

        var snapshotSignal = _snapshotStore.UpdateSignal;

        while (!stoppingToken.IsCancellationRequested)
        {
            var metrics = _metricsStore.Latest;
            if (metrics is not null)
                _writer.EnqueueMetrics(metrics);

            var snapshot = _snapshotStore.Latest;
            if (snapshot is not null)
                _writer.EnqueueSnapshot(snapshot);

            var intervalSeconds = _options.Value.SnapshotIntervalSeconds;
            try
            {
                var delayCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                var registration = ThreadPool.RegisterWaitForSingleObject(
                    snapshotSignal,
                    (_, _) => delayCts.Cancel(),
                    null,
                    Timeout.Infinite,
                    executeOnlyOnce: true);

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(intervalSeconds), delayCts.Token)
                              .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    // Woken by snapshot signal — push immediately.
                }
                finally
                {
                    registration.Unregister(null);
                    delayCts.Dispose();
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogDebug("ControlPlaneTelemetryTimer stopped.");
    }
}
```

Key changes: removed `ActiveLeaseState` and `IControlPlaneTelemetryClient` dependencies; replaced `_client.PushMetricsAsync/PushSnapshotAsync` with `_writer.EnqueueMetrics/EnqueueSnapshot`; removed the `leaseId` null-guard (the writer handles that itself in `FlushWithRetryAsync`).

- [ ] **Step 2.2: Build to verify**

```powershell
dotnet build src/DevOpsMigrationPlatform.Infrastructure.Agent/DevOpsMigrationPlatform.Infrastructure.Agent.csproj --no-restore
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 2.3: Commit**

```powershell
git add src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneTelemetryTimer.cs
git commit -m "refactor: route ControlPlaneTelemetryTimer through UnifiedWorkerEventWriter"
```

---

## Task 3: Move `SignalTerminalAsync` to use `UnifiedWorkerEventWriter`

**Files:**
- Modify: `src/DevOpsMigrationPlatform.Infrastructure.Agent/AgentWorkerBase.cs`

- [ ] **Step 3.1: Read the full `AgentWorkerBase` constructor and field section**

Read lines 1–80 of `AgentWorkerBase.cs` to identify the injected fields and constructor signature.

- [ ] **Step 3.2: Inject `UnifiedWorkerEventWriter` into the constructor**

In the constructor, add `UnifiedWorkerEventWriter writer` as a parameter and store it:

```csharp
private readonly UnifiedWorkerEventWriter _eventWriter;
```

Assign it in the constructor body: `_eventWriter = writer;`

The `UnifiedWorkerEventWriter` is already a singleton in DI — this is a safe constructor injection.

- [ ] **Step 3.3: Replace `SignalTerminalAsync` implementation**

Replace the body of `SignalTerminalAsync` (currently lines 249–273 in `AgentWorkerBase.cs`) with:

```csharp
protected async Task SignalTerminalAsync(
    HttpClient controlPlane, string leaseId, string terminal, CancellationToken ct)
{
    _eventWriter.EnqueueTerminal(failed: terminal == "fail");
    await _eventWriter.FlushAsync().ConfigureAwait(false);
}
```

The parameter `controlPlane` and `leaseId` are kept in the signature for now to avoid changing all call sites, but they are no longer used. The `terminal` string is `"complete"` or `"fail"` — map it to the `bool failed` that `EnqueueTerminal` expects.

- [ ] **Step 3.4: Build to verify**

```powershell
dotnet build src/DevOpsMigrationPlatform.Infrastructure.Agent/DevOpsMigrationPlatform.Infrastructure.Agent.csproj --no-restore
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3.5: Run all tests**

```powershell
dotnet test --no-build --logger "console;verbosity=normal" 2>&1 | tail -20
```

Expected: all 188 tests pass.

- [ ] **Step 3.6: Commit**

```powershell
git add src/DevOpsMigrationPlatform.Infrastructure.Agent/AgentWorkerBase.cs
git commit -m "refactor: route SignalTerminalAsync through UnifiedWorkerEventWriter"
```

---

## Task 4: Remove `ControlPlaneLoggerProvider` legacy HTTP fallback

**Files:**
- Modify: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneLoggerProvider.cs`

- [ ] **Step 4.1: Read the full `FlushBatchAsync` method**

Read lines 154–202 to see the exact fallback block. The fallback is lines 168–201 (everything after `return;` inside `if (writer is not null)`).

- [ ] **Step 4.2: Delete the fallback block**

Replace `FlushBatchAsync` so it only has the primary path:

```csharp
private async Task FlushBatchAsync(
    List<DiagnosticLogRecord> batch,
    CancellationToken cancellationToken)
{
    var writer = EventWriter;
    if (writer is null)
    {
        Interlocked.Add(ref _droppedCount, batch.Count);
        return;
    }

    writer.EnqueueDiagnostic(batch.ToArray());
    await Task.CompletedTask.ConfigureAwait(false);
}
```

Wait — `EnqueueDiagnostic` is synchronous (it just calls `TryWrite`). The method can be simplified:

```csharp
private Task FlushBatchAsync(
    List<DiagnosticLogRecord> batch,
    CancellationToken cancellationToken)
{
    var writer = EventWriter;
    if (writer is null)
        Interlocked.Add(ref _droppedCount, batch.Count);
    else
        writer.EnqueueDiagnostic(batch.ToArray());

    return Task.CompletedTask;
}
```

Also remove the now-unused `_jsonOptions` field and any `using` directives that were only needed for the fallback HTTP path (check for `System.Net.Http.Json`).

- [ ] **Step 4.3: Build to verify**

```powershell
dotnet build src/DevOpsMigrationPlatform.Infrastructure.Agent/DevOpsMigrationPlatform.Infrastructure.Agent.csproj --no-restore
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 4.4: Commit**

```powershell
git add src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneLoggerProvider.cs
git commit -m "refactor: remove ControlPlaneLoggerProvider legacy HTTP fallback path"
```

---

## Task 5: Delete `ControlPlaneProgressSink.cs` and `ControlPlaneTelemetryClient.cs`

**Files:**
- Delete: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneProgressSink.cs`
- Delete: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneTelemetryClient.cs`

- [ ] **Step 5.1: Verify nothing references `ControlPlaneProgressSink`**

```powershell
grep -r "ControlPlaneProgressSink" src/ --include="*.cs" -l
```

Expected: only the file itself. If other files appear, investigate before deleting.

- [ ] **Step 5.2: Verify nothing references `ControlPlaneTelemetryClient`**

```powershell
grep -r "ControlPlaneTelemetryClient" src/ --include="*.cs" -l
```

Expected: only the file itself and `TelemetryServiceExtensions.cs` (which will be cleaned in Task 6).

- [ ] **Step 5.3: Delete both files**

```powershell
Remove-Item "src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneProgressSink.cs"
Remove-Item "src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneTelemetryClient.cs"
```

- [ ] **Step 5.4: Build to see which references break**

```powershell
dotnet build src/DevOpsMigrationPlatform.Infrastructure.Agent/DevOpsMigrationPlatform.Infrastructure.Agent.csproj --no-restore 2>&1
```

Any errors here guide what still needs removing in Tasks 6 and 7.

- [ ] **Step 5.5: Commit**

```powershell
git add -A
git commit -m "refactor: delete dead ControlPlaneProgressSink and ControlPlaneTelemetryClient"
```

---

## Task 6: Delete `IControlPlaneTelemetryClient` and clean `TelemetryServiceExtensions`

**Files:**
- Delete: `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/IControlPlaneTelemetryClient.cs`
- Modify: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/TelemetryServiceExtensions.cs`

- [ ] **Step 6.1: Verify nothing else implements or injects `IControlPlaneTelemetryClient`**

```powershell
grep -r "IControlPlaneTelemetryClient" src/ --include="*.cs" -l
```

Expected: `IControlPlaneTelemetryClient.cs`, `TelemetryServiceExtensions.cs`, and `ControlPlaneTelemetryTimer.cs` (which no longer references it after Task 2). If other files appear, investigate.

- [ ] **Step 6.2: Delete the interface file**

```powershell
Remove-Item "src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/IControlPlaneTelemetryClient.cs"
```

- [ ] **Step 6.3: Remove dead extension methods from `TelemetryServiceExtensions.cs`**

Remove the `AddControlPlaneTelemetryClient` method (lines 60–67) and the `AddControlPlaneProgressSink` method (lines 73–84) entirely. The `AddUnifiedWorkerEventWriter` and `AddCompositeProgressSink` methods stay.

After edits the file should contain only: `AddAgentTelemetryServices`, `AddAgentJobMetricsServices`, `AddUnifiedWorkerEventWriter`, and `AddCompositeProgressSink`.

- [ ] **Step 6.4: Build to verify**

```powershell
dotnet build src/ --no-restore 2>&1 | grep -E "error|Error"
```

Expected: no errors.

- [ ] **Step 6.5: Commit**

```powershell
git add -A
git commit -m "refactor: delete IControlPlaneTelemetryClient and clean TelemetryServiceExtensions"
```

---

## Task 7: Remove legacy registrations and update docstring in `CoreAgentServiceExtensions`

**Files:**
- Modify: `src/DevOpsMigrationPlatform.Infrastructure.Agent/CoreAgentServiceExtensions.cs`

- [ ] **Step 7.1: Remove `AddControlPlaneTelemetryClient` call**

In `AddControlPlaneIntegration` (line 104), remove:
```csharp
services.AddControlPlaneTelemetryClient(controlPlaneBaseUrl);
```

- [ ] **Step 7.2: Update the `AddCoreAgentServices` XML docstring**

The summary still mentions `ControlPlaneProgressSink` as a registered service. Update the `<item>` that references it to instead reference `UnifiedWorkerEventWriter`:

Replace:
```xml
///   <item><see cref="ControlPlaneProgressSink"/>, <see cref="PackageProgressSink"/>, and <see cref="CompositeProgressSink"/> as <c>IProgressSink</c>.</item>
///   ...
///   <item><see cref="ControlPlaneTelemetryTimer"/> background service.</item>
```

With:
```xml
///   <item><see cref="UnifiedWorkerEventWriter"/>, <see cref="PackageProgressSink"/>, and <see cref="CompositeProgressSink"/> as <c>IProgressSink</c>.</item>
///   <item><see cref="ControlPlaneTelemetryTimer"/> background service (enqueues via <see cref="UnifiedWorkerEventWriter"/>).</item>
```

- [ ] **Step 7.3: Build the full solution**

```powershell
dotnet build --no-restore 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 7.4: Commit**

```powershell
git add src/DevOpsMigrationPlatform.Infrastructure.Agent/CoreAgentServiceExtensions.cs
git commit -m "refactor: remove AddControlPlaneTelemetryClient from CoreAgentServiceExtensions and update docstring"
```

---

## Task 8: Full test run and final verification

- [ ] **Step 8.1: Run all 188 tests**

```powershell
dotnet test --no-build --logger "console;verbosity=normal" 2>&1 | tail -30
```

Expected: `Passed: 188, Failed: 0`.

- [ ] **Step 8.2: Verify no legacy endpoints are referenced in agent code**

```powershell
grep -r "agents/lease" src/DevOpsMigrationPlatform.Infrastructure.Agent/ --include="*.cs"
grep -r "PushMetricsAsync\|PushSnapshotAsync\|PushTaskListAsync" src/ --include="*.cs"
```

Both should return empty results (no matches).

- [ ] **Step 8.3: Verify no dead classes remain**

```powershell
grep -r "ControlPlaneProgressSink\|ControlPlaneTelemetryClient\|IControlPlaneTelemetryClient" src/ --include="*.cs"
```

Expected: empty.

---

## Summary of legacy paths being removed

| What | Where | Replaced by |
|------|-------|-------------|
| `PushMetricsAsync` / `PushSnapshotAsync` | `ControlPlaneTelemetryTimer` | `_writer.EnqueueMetrics` / `EnqueueSnapshot` |
| `POST /agents/lease/{id}/complete|fail` | `AgentWorkerBase.SignalTerminalAsync` | `_eventWriter.EnqueueTerminal` + `FlushAsync` |
| Legacy HTTP fallback in logger | `ControlPlaneLoggerProvider.FlushBatchAsync` | Deleted (null-guard only) |
| `ControlPlaneProgressSink` class | Entire file | Deleted (replaced by `UnifiedWorkerEventWriter`) |
| `ControlPlaneTelemetryClient` class | Entire file | Deleted |
| `IControlPlaneTelemetryClient` interface | Entire file | Deleted |
| `AddControlPlaneTelemetryClient` DI helper | `TelemetryServiceExtensions` | Deleted |
| `AddControlPlaneProgressSink` DI helper | `TelemetryServiceExtensions` | Deleted |
