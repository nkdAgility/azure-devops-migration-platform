# Tasks: OpenTelemetry Observability — CLI DI and Phase 2 Live Progress Streaming

**Input**: Design documents from `/specs/002-otel-observability-phase2/`
**Prerequisites**: plan.md ✅ spec.md ✅ research.md ✅ data-model.md ✅ contracts/ ✅ quickstart.md ✅
**Branch**: `002-otel-observability-phase2`

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel with other [P] tasks in the same phase (different files, no blocking deps)
- **[Story]**: `[US1]`, `[US2]`, `[US3]` — user story this task belongs to
- File paths are relative to repository root

---

## Phase 1: Setup

**Purpose**: Create the new test project and register it in the solution so all subsequent test tasks have a home.

- [ ] T001 Create `tests/DevOpsMigrationPlatform.ControlPlane.Tests/DevOpsMigrationPlatform.ControlPlane.Tests.csproj` — `net10.0`; `Nullable=enable`; `ImplicitUsings=enable`; `IsTestProject=true`; packages: `Microsoft.NET.Test.Sdk 17.*`, `MSTest.TestAdapter 3.*`, `MSTest.TestFramework 3.*`, `Reqnroll.MSTest 2.*`, `Moq 4.*`, `Microsoft.Extensions.DependencyInjection 10.0.0`; project references: `../../src/DevOpsMigrationPlatform.Abstractions/DevOpsMigrationPlatform.Abstractions.csproj`, `../../src/DevOpsMigrationPlatform.ControlPlane/DevOpsMigrationPlatform.ControlPlane.csproj`
- [ ] T002 Add `tests/DevOpsMigrationPlatform.ControlPlane.Tests/DevOpsMigrationPlatform.ControlPlane.Tests.csproj` to the `/tests/` folder in `DevOpsMigrationPlatform.slnx` — depends on T001

**Checkpoint**: `ControlPlane.Tests` project builds successfully — all US2 test tasks can now proceed.

---

## Phase 2: Foundational

> No cross-story foundational tasks required. US1 and US2 are independently implementable after Phase 1. US3 depends on US2 being complete.

---

## Phase 3: User Story 1 — CLI Command Sends Telemetry to Azure Monitor (Priority: P1) 🎯 MVP

**Goal**: `Program.cs` wires OTel SDK with Azure Monitor export; each CLI command emits a child Activity span; `TracerProvider`/`MeterProvider` are flushed and disposed before process exit.

**Independent Test**: Run any CLI command with `Telemetry:AzureMonitorConnectionString` configured — a trace span should appear in Application Insights. Run without the connection string — command runs normally with no error. No Control Plane or Agent required.

### Gherkin Feature File for User Story 1

- [ ] T003 [US1] Create `features/platform/telemetry/cli-otel.feature` — translate `spec.md` User Story 1 acceptance scenarios 1–4 into conformant Gherkin (`Feature: CLI OTel Observability`, `As a platform operator…`; four scenarios: span appears in Azure Monitor, span marked failed on error, command runs normally without connection string, root ActivitySource created on startup); follow `.agents/guardrails/acceptance-test-format.md` naming and tag rules

### Implementation for User Story 1

- [ ] T004 [P] [US1] Add OTel and Azure Monitor exporter package references to `src/DevOpsMigrationPlatform.CLI.Migration/DevOpsMigrationPlatform.CLI.Migration.csproj`: `OpenTelemetry.Extensions.Hosting` (latest stable), `Azure.Monitor.OpenTelemetry.Exporter` (latest stable) — do NOT add the `AspNetCore` variant
- [ ] T005 [US1] Wire OTel DI in `src/DevOpsMigrationPlatform.CLI.Migration/Program.cs`: (1) create `ActivitySource cliSource = new("DevOpsMigrationPlatform.CLI")` and register as `services.AddSingleton(cliSource)`; (2) call `services.AddOpenTelemetry().WithTracing(b => b.AddSource("DevOpsMigrationPlatform.CLI").AddHttpClientInstrumentation())` and `.WithMetrics(b => b.AddHttpClientInstrumentation())`; (3) inside the tracing and metrics builders conditionally call `.AddAzureMonitorTraceExporter(o => o.ConnectionString = ...)` and `.AddAzureMonitorMetricExporter(o => o.ConnectionString = ...)` respectively (from `Azure.Monitor.OpenTelemetry.Exporter`), only when `TelemetryOptions.AzureMonitorConnectionString` is non-null/non-empty; (4) after `await app.RunAsync(spectreArgs)`, retrieve `TracerProvider` and `MeterProvider` from the built `ServiceProvider`, call `ForceFlush(TimeSpan.FromSeconds(5))` then `Dispose()` on each before `Main` returns
- [ ] T006 [US1] Add constructor injection of `ActivitySource` and a child Activity span to `src/DevOpsMigrationPlatform.CLI.Migration/Commands/TfsExportCommand.cs` and `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — add `public TfsExportCommand(ActivitySource activitySource)` constructor; inside `ExecuteAsync` wrap the command body in `using var activity = activitySource.StartActivity(commandName)` and call `activity?.SetStatus(ActivityStatusCode.Error, ex.Message)` on any caught exception before re-throwing

### Tests for User Story 1

- [ ] T007 [US1] Link `cli-otel.feature` in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/DevOpsMigrationPlatform.Infrastructure.Tests.csproj` (add `<None Include="..\..\features\platform\telemetry\cli-otel.feature">` with `<Link>` and `CopyToOutputDirectory`); write step defs in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/CliOtelSteps.cs` and `CliOtelContext.cs` — use `MockBehavior.Strict`; stub `ActivitySource` and an in-process OTel exporter to assert spans are started/tagged without an Azure Monitor connection

**Checkpoint**: US1 is complete when a real CLI command emits a trace span and the Reqnroll scenarios are green.

---

## Phase 4: User Story 2 — Agent Streams ProgressEvents to Control Plane in Real Time (Priority: P1)

**Goal**: `ControlPlaneProgressSink` POSTs individual `ProgressEvent` records to the Control Plane. The Control Plane stores them in a bounded ring buffer and exposes them via snapshot REST and SSE endpoints.

**Independent Test**: Start the agent with a short export job → call `GET /jobs/{jobId}/logs` immediately after start → ring buffer must contain at least one `ProgressEvent`. No `migrate logs` command or TUI required.

### Gherkin Feature Files for User Story 2

- [ ] T008 [P] [US2] Create `features/platform/telemetry/progress-sink.feature` — translate US2 acceptance scenarios 1, 4, 5 into conformant Gherkin: (1) sink POSTs event to Control Plane within 1 s of `Emit`; (4) fresh ring buffer created on Control Plane restart when agent resumes posting; (5) transient HTTP failure → event dropped, job continues, debug log emitted
- [ ] T009 [P] [US2] Create `features/platform/telemetry/job-progress-store.feature` — translate US2 acceptance scenario 3 into Gherkin: ring buffer at capacity → oldest event evicted → new event stored; capacity never exceeded
- [ ] T010 [P] [US2] Create `features/platform/telemetry/progress-controller.feature` — translate US2 acceptance scenario 2 and FR-014a into Gherkin: event retrievable immediately via `GET /jobs/{jobId}/logs`; 403 returned when caller lacks job visibility

### Implementation for User Story 2

- [ ] T011 [P] [US2] Create `src/DevOpsMigrationPlatform.ControlPlane/Services/JobProgressOptions.cs` — `public sealed class JobProgressOptions` with `public const string SectionName = "JobProgress";`, `[Range(1, 100_000)] public int Capacity { get; init; } = 1000;`
- [ ] T012 [P] [US2] Create `src/DevOpsMigrationPlatform.ControlPlane/Services/JobProgressStore.cs` — define internal `JobProgressEntry` record holding `ConcurrentQueue<ProgressEvent> Queue` and `List<ChannelWriter<ProgressEvent>> Subscribers` (mutations guarded by `lock`); implement `JobProgressStore` as a singleton with `ConcurrentDictionary<Guid, JobProgressEntry>` and these public methods: `void Append(Guid jobId, ProgressEvent evt)` (enqueue to snapshot queue evicting oldest when > Capacity; `TryWrite` to each subscriber channel), `IReadOnlyList<ProgressEvent> GetSnapshot(Guid jobId)` (returns `queue.ToArray()`; empty list if unknown), `ChannelReader<ProgressEvent> Subscribe(Guid jobId)` (creates bounded subscriber channel with `DropOldest`; adds writer to entry), `void Unsubscribe(Guid jobId, ChannelWriter<ProgressEvent> writer)` (removes and completes writer), `void CompleteJob(Guid jobId)` (completes all subscriber writers), `void Remove(Guid jobId)` (evicts entry)
- [ ] T013 [P] [US2] Create `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/ControlPlaneProgressSink.cs` — `public sealed class ControlPlaneProgressSink : BackgroundService, IProgressSink` with constructor `(HttpClient http, ActiveLeaseState leaseState, ILogger<ControlPlaneProgressSink> logger)`; declare `private const int ChannelCapacity = 100` and create the internal channel as `Channel.CreateBounded<ProgressEvent>(new BoundedChannelOptions(ChannelCapacity) { FullMode = BoundedChannelFullMode.DropOldest })`; `void Emit(ProgressEvent evt)` calls `_channel.Writer.TryWrite(evt)` (never blocks — `DropOldest` evicts oldest on overflow); `ExecuteAsync` loops `await foreach (var e in _channel.Reader.ReadAllAsync(ct))` and posts each event to `POST /agents/lease/{leaseId}/progress` via `http.PostAsJsonAsync`; on `HttpRequestException` log at debug and continue
- [ ] T014 [US2] Create `src/DevOpsMigrationPlatform.ControlPlane/Controllers/ProgressController.cs` — `[ApiController]` MVC controller with constructor `(JobProgressStore store, ILeaseJobResolver resolver, ILogger<ProgressController> logger)`; three action methods: (1) `[HttpPost("/agents/lease/{leaseId}/progress")]` → resolve job from lease (404 if unknown), call `store.Append`, return 204; (2) `[HttpGet("/jobs/{jobId}/logs")]` → auth check (403), if `follow == false` return `store.GetSnapshot` as JSON 200; (3) **same method** overloads with `[FromQuery] bool follow = false` — use a **single** `GetLogs` action that branches on the `follow` flag: when `follow == true`, set `Content-Type: text/event-stream` and `Cache-Control: no-cache`, call `store.Subscribe`, loop `ChannelReader.ReadAllAsync` writing `data: {json}\n\n` per event, race with `PeriodicTimer(15s)` for heartbeat comment `:\n\n`, on channel completion send `event: job-ended\ndata: {}\n\n`, call `store.Unsubscribe` in `finally` — depends on T012
- [ ] T015 [P] [US2] Create `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/CompositeProgressSink.cs` — `public sealed class CompositeProgressSink : IProgressSink` with constructor `(params IProgressSink[] sinks)` storing sinks as `_sinks = sinks.ToList()`; `void Emit(ProgressEvent evt)` iterates `_sinks`, wraps each `sink.Emit(evt)` in a `try/catch`, logs any exception at debug and continues to the next sink — a failing sink must never suppress sibling sinks
- [ ] T016 [US2] Modify `src/DevOpsMigrationPlatform.ControlPlane/Services/ControlPlaneServiceExtensions.cs` — add `services.AddSingleton<JobProgressStore>()` and `services.AddOptions<JobProgressOptions>().BindConfiguration(JobProgressOptions.SectionName).ValidateDataAnnotations().ValidateOnStart()` inside the existing `AddControlPlaneServices` extension method — depends on T011, T012
- [ ] T017 [US2] Modify `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/TelemetryServiceExtensions.cs` — add public extension method `AddControlPlaneProgressSink(this IServiceCollection services, Uri controlPlaneBaseUrl)` that: (1) registers named `HttpClient` for the sink pointing at `controlPlaneBaseUrl`; (2) `services.AddSingleton<ControlPlaneProgressSink>()`; (3) `services.AddHostedService(sp => sp.GetRequiredService<ControlPlaneProgressSink>())`; **does not** register `IProgressSink` directly — that registration is handled by T018 via `CompositeProgressSink` — depends on T013, T015
- [ ] T018 [US2] Modify `src/DevOpsMigrationPlatform.MigrationAgent/Program.cs` — inside the `controlPlaneBaseUrl` block (after `AddControlPlaneTelemetryClient`), call `builder.Services.AddControlPlaneProgressSink(controlPlaneBaseUrl)`; then register the composite: `builder.Services.AddSingleton<IProgressSink>(sp => new CompositeProgressSink(new AnsiProgressSink(), new PackageProgressSink(), sp.GetRequiredService<ControlPlaneProgressSink>()))` so all three sinks receive every `ProgressEvent` — depends on T015, T017

### Tests for User Story 2

- [ ] T019 [P] [US2] Link `job-progress-store.feature` in `tests/DevOpsMigrationPlatform.ControlPlane.Tests/DevOpsMigrationPlatform.ControlPlane.Tests.csproj`; write `tests/DevOpsMigrationPlatform.ControlPlane.Tests/Progress/JobProgressStoreSteps.cs` and `JobProgressStoreContext.cs` — instantiate `JobProgressStore` directly (with a small test capacity, e.g., 5 events); test `Append` fills the queue to capacity then evicts oldest; assert `GetSnapshot` returns a list not exceeding that capacity — depends on T012
- [ ] T020 [P] [US2] Link `progress-controller.feature` in `tests/DevOpsMigrationPlatform.ControlPlane.Tests/DevOpsMigrationPlatform.ControlPlane.Tests.csproj`; write `tests/DevOpsMigrationPlatform.ControlPlane.Tests/Progress/ProgressControllerSteps.cs` and `ProgressControllerContext.cs` — instantiate `ProgressController` with a real `JobProgressStore` (concrete, small capacity) and `Mock<ILeaseJobResolver>(MockBehavior.Strict)`; verify 204 on valid POST, 404 on unknown lease, 200 JSON array on GET snapshot (follow=false), 403 when resolver returns no job — depends on T014
- [ ] T021 [US2] Link `progress-sink.feature` in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/DevOpsMigrationPlatform.Infrastructure.Tests.csproj`; write `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/ControlPlaneProgressSinkSteps.cs` and `ControlPlaneProgressSinkContext.cs` — use `MockHttpMessageHandler` to simulate the Control Plane endpoint; verify `Emit` enqueues and drain loop POSTs; verify `HttpRequestException` results in debug log and no exception propagated — depends on T013

**Checkpoint**: US2 is complete when `GET /jobs/{jobId}/logs` returns a snapshot from a real agent run and all Reqnroll scenarios are green.

---

## Phase 5: User Story 3 — `migrate logs --follow` Tails Live Events in the Terminal (Priority: P2)

**Goal**: `migrate logs --job <id>` prints NDJSON snapshot and exits; `migrate logs --job <id> --follow` streams live events until the job completes or Ctrl+C is pressed.

**Prerequisite**: US2 must be complete (SSE endpoint and ring buffer must be live).

**Independent Test**: Run `migrate logs --job <id> --follow` against a job in progress — NDJSON lines appear as events arrive; Ctrl+C exits the CLI without stopping the job.

### Gherkin Feature File for User Story 3

- [ ] T022 [US3] Create `features/cli/execute/migrate-logs.feature` — translate US3 acceptance scenarios 1–6 into conformant Gherkin under `cli/execute/` tier (CLI tier: stdout NDJSON format, exit codes, Ctrl+C behaviour, error messages for unknown job and 403); tag scenarios per `.agents/guardrails/acceptance-test-format.md`

### Implementation for User Story 3

- [ ] T023 [P] [US3] Add `GetLogsAsync` and `FollowLogsAsync` to `src/DevOpsMigrationPlatform.CLI.Migration/JobRunners/ControlPlaneClient.cs`: `Task<IReadOnlyList<ProgressEvent>> GetLogsAsync(Guid jobId, CancellationToken ct)` — GET `/jobs/{jobId}/logs`, deserialise JSON array; `IAsyncEnumerable<ProgressEvent> FollowLogsAsync(Guid jobId, CancellationToken ct)` — GET `/jobs/{jobId}/logs?follow=true` with `HttpCompletionOption.ResponseHeadersRead`, read lines with `StreamReader`, parse `data:` prefix, yield deserialised `ProgressEvent`, break on `event: job-ended` line; propagate `HttpRequestException` (non-200) to caller
- [ ] T024 [P] [US3] Create `src/DevOpsMigrationPlatform.CLI.Migration/Commands/LogsCommand.cs` — `public sealed class LogsCommand : AsyncCommand<LogsCommand.Settings>`; `Settings` has `[CommandOption("--job")] public Guid JobId { get; init; }` (required) and `[CommandOption("--follow")] public bool Follow { get; init; }`; constructor injects `ControlPlaneClient client, ActivitySource activitySource`; `ExecuteAsync`: wrap in `using var activity = activitySource.StartActivity("logs")`; without `--follow` call `client.GetLogsAsync`, write each event as `JsonSerializer.Serialize(evt)` to stdout, return 0; with `--follow` call `client.FollowLogsAsync` in `await foreach`, write each event to stdout, return 0 on clean completion; catch `OperationCanceledException` (Ctrl+C) → return 0; catch `HttpRequestException` → print error, set `activity?.SetStatus(Error)`, return 1
- [ ] T025 [US3] Register `LogsCommand` in `src/DevOpsMigrationPlatform.CLI.Migration/Program.cs` — add `config.AddCommand<LogsCommand>("logs")` inside `app.Configure(config => { ... })` — depends on T024
- [ ] T026 [US3] Link `migrate-logs.feature` in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/DevOpsMigrationPlatform.Infrastructure.Tests.csproj`; write `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Cli/MigrateLogsSteps.cs` and `MigrateLogsContext.cs` — mock `ControlPlaneClient` with `MockBehavior.Strict`; stub `GetLogsAsync` returning a fixed list; stub `FollowLogsAsync` yielding events then completing; assert NDJSON lines written to a captured stdout writer; assert exit code 0 on clean completion; assert exit code 1 on `HttpRequestException` — depends on T023, T024

**Checkpoint**: US3 is complete when `migrate logs --follow` streams live events from a running job in the Aspire environment and all Reqnroll scenarios are green.

---

## Phase 6: Polish & Cross-Cutting

- [ ] T027 [P] Add `"JobProgress": { "Capacity": 1000 }` section to `src/DevOpsMigrationPlatform.ControlPlaneHost/appsettings.json` so the configured default is explicit and visible
- [ ] T028 [P] [US2] Wire `JobProgressStore.CompleteJob(jobId)` into the existing job state-machine transition in `DevOpsMigrationPlatform.ControlPlane` — locate the handler that transitions a job to a terminal state (`Completed`, `Failed`, or `Cancelled`) and add a call to `store.CompleteJob(jobId)` so all active SSE subscribers receive the `event: job-ended` close signal and `migrate logs --follow` exits cleanly (FR-013); inject `JobProgressStore` via constructor wherever the state transition is handled — depends on T012, T016
- [ ] T029 [P] Verify all 5 new Gherkin feature files (`cli-otel.feature`, `progress-sink.feature`, `job-progress-store.feature`, `progress-controller.feature`, `migrate-logs.feature`) have a `Feature:` declaration matching the filename, an `As a / I want / So that` header, at least one `@` tag per scenario, and all `Given/When/Then` steps are in the imperative form — reject any file with Scenario Outlines that could be plain Scenarios

> **SC-003** (SSE latency < 2 s) and **SC-006** (CLI cold-start overhead < 50 ms) are verified manually via `quickstart.md` steps 3b and 1a respectively. No automated performance task is included in this session.

---

## Summary

| Phase | Tasks | Parallel opportunities |
|---|---|---|
| Phase 1: Setup | T001–T002 | T001 then T002 (sequential) |
| Phase 3: US1 CLI OTel (P1) | T003–T007 | T003 + T004 in parallel; T005 + T006 sequential |
| Phase 4: US2 Progress Streaming (P1) | T008–T021 | T008+T009+T010+T011+T012+T013+T015 all in parallel; T014 after T012; T016+T017 sequential; T018 after T015+T017; T019+T020 in parallel after T012/T014 |
| Phase 5: US3 migrate logs (P2) | T022–T026 | T022 + T023 + T024 in parallel |
| Phase 6: Polish | T027–T029 | T027 + T028 + T029 in parallel |
| **Total** | **29 tasks** | |

## Dependencies

```
T001 → T002 (sequential; T002 needs the .csproj T001 creates)

US1: T003, T004 parallel → T005 → T006 → T007

US2 parallel batch 1: T008 + T009 + T010 + T011 + T012 + T013 + T015
     T014 → depends on T012
     T016 → depends on T011 + T012
     T017 → depends on T013 + T015
     T018 → depends on T015 + T017
     T019 → depends on T012 (concrete store, instantiate directly)
     T020 → depends on T014
     T021 → depends on T013
     T028 → depends on T012 + T016

US3 (requires US2 complete):
     T022 + T023 + T024 parallel
     T025 → depends on T024
     T026 → depends on T023 + T024
```

## Parallel Execution Examples

**Start of US2 — run all seven together**:
```
T008 + T009 + T010 + T011 + T012 + T013 + T015
```

**After T012 is done**:
```
T014 (controller) + T019 (store tests) in parallel
```

**Start of US3 — run together**:
```
T022 + T023 + T024
```

## Implementation Strategy

**MVP Scope**: US1 + US2 (T001–T021 + T027–T028) — delivers both P1 user stories (CLI OTel + live progress pipeline). After this, every operator can trace CLI commands in Azure Monitor and the Control Plane is receiving live progress events with clean SSE stream termination.

**Increment 2**: US3 (T022–T026) — adds `migrate logs` command on top of the US2 infrastructure.

**Deferred**: US4 (TUI SSE consumer, P3) is not included. It is planned for a separate session once US2 and US3 infrastructure is proven stable.
