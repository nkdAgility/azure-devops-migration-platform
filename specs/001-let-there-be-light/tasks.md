# Tasks: Telemetry Pipeline — Cloud Export + TUI Live Feed

**Input**: `specs/001-let-there-be-light/plan.md`, `research.md`, `data-model.md`, `contracts/`
**Branch**: `first-coding-session` (historical)

## Format: `[ID] [P?] [Story?] Description — file path`

- **[P]**: Parallelisable with other [P]-marked tasks at the same phase (different files, no shared dependency)
- **[US#]**: User story label
- No `[P]` = sequential — depends on a prior task in the same phase

---

## Phase 1: Setup — NuGet Package Additions

**Purpose**: Add the two new package dependencies before any source files reference them.

- [x] T001 [P] Add `<PackageReference Include="OpenTelemetry" Version="1.12.0" />` inside `<ItemGroup Condition="'$(TargetFramework)' != 'net481'">` in `src/DevOpsMigrationPlatform.Infrastructure/DevOpsMigrationPlatform.Infrastructure.csproj` — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: OpenTelemetry remains referenced in `src/DevOpsMigrationPlatform.Infrastructure/DevOpsMigrationPlatform.Infrastructure.csproj`, but package versioning/placement now follows centralized package management in `Directory.Packages.props`.
- [x] T002 [P] Add `<PackageReference Include="Azure.Monitor.OpenTelemetry.AspNetCore" Version="1.4.0" />` to `src/DevOpsMigrationPlatform.ServiceDefaults/DevOpsMigrationPlatform.ServiceDefaults.csproj` — Status: complete
- [x] T003 Run `dotnet build DevOpsMigrationPlatform.slnx -v quiet` and confirm zero errors before proceeding to Phase 2 — Status: complete

---

## Phase 2: Foundational — Abstractions Layer

**Purpose**: Shared types and interfaces used by every downstream phase. ALL of these must exist before any Phase 3+ task can compile.

**⚠️ CRITICAL**: Phases 3–6 depend on this phase being complete.

- [x] T004 Create `src/DevOpsMigrationPlatform.Abstractions/Models/MetricSnapshot.cs` — `public record MetricSnapshot` with all fields from `data-model.md §1` (counters + nullable duration means + `Timestamp`) — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: Current canonical contracts are `JobMetrics`/`JobSnapshot` under `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi`; `MetricSnapshot.cs` no longer exists.
- [x] T005 [P] Add `public MetricSnapshot? Metrics { get; init; }` property to `src/DevOpsMigrationPlatform.Abstractions/Models/ProgressEvent.cs` (additive; no existing consumers break) — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: `ProgressEvent` exists at `src/DevOpsMigrationPlatform.Abstractions/Streaming/ProgressEvent.cs` with `public JobMetrics? Metrics { get; init; }`.
- [x] T006 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Telemetry/TelemetryOptions.cs` — sealed class with `SectionName`, `AzureMonitorConnectionString`, `SnapshotIntervalSeconds = 5`, `SubprocessSnapshotRevisionInterval = 100`. **Do not include `OtlpEndpoint`** — OTLP is configured via `OTEL_EXPORTER_OTLP_ENDPOINT` env var, not `TelemetryOptions`. (see `data-model.md §3`) — Status: complete
- [x] T007 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Telemetry/IMetricSnapshotStore.cs` — interface with `void Update(MetricSnapshot)` and `MetricSnapshot? Latest { get; }` (see `data-model.md §4`) — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: `IMetricSnapshotStore` does not exist; current design uses `IJobMetricsStore` and `IJobSnapshotStore` in `Infrastructure.ControlPlane.Metrics`.
- [x] T008 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Telemetry/IControlPlaneTelemetryClient.cs` — interface with `Task PushSnapshotAsync(string leaseId, MetricSnapshot snapshot, CancellationToken ct)` (see `data-model.md §5`) — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: Interface exists as `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/IControlPlaneTelemetryClient.cs` and now includes `PushMetricsAsync`, `PushSnapshotAsync`, and `PushTaskListAsync`.
- [x] T009 [P] Create `features/platform/telemetry/otel-cloud-export.feature` — Gherkin scenarios for: (a) OTLP exporter registered when `OtlpEndpoint` is configured, (b) Azure Monitor exporter registered when `AzureMonitorConnectionString` is configured, (c) neither exporter registered when both are absent — Status: complete
- [x] T010 [P] Create `features/platform/telemetry/metric-snapshot-relay.feature` — Gherkin scenarios for: (a) Migration Agent pushes `MetricSnapshot` to `POST /agents/lease/{id}/telemetry` on interval, (b) Control Plane stores latest snapshot per job, (c) old snapshot replaced when new one arrives — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: Feature file exists, but runtime push endpoint is now `POST /agents/lease/{leaseId}/metrics` in `src/DevOpsMigrationPlatform.ControlPlane/Controllers/TelemetryController.cs`.
- [x] T011 [P] Create `features/platform/telemetry/tui-metrics-panel.feature` — Gherkin scenarios for: (a) `GET /jobs/{jobId}/telemetry` returns `204` when no snapshot received, (b) returns `200` with snapshot after agent pushes one, (c) TUI panel displays metric values from snapshot — Status: complete

**Checkpoint**: `dotnet build DevOpsMigrationPlatform.slnx -v quiet` — zero errors. Acceptance feature files committed alongside abstraction types.

---

## Phase 3: User Story 1 — Cloud Provider Export (OTel Fan-Out) 🎯 MVP

**Goal**: Metrics and traces emitted by any running component flow to OTLP and/or Azure Monitor when configured. Zero external config = zero-change behaviour.

**Independent Test**: Set `OTEL_EXPORTER_OTLP_ENDPOINT` or `Telemetry:OtlpEndpoint` in `appsettings.json` and confirm spans appear in a local Jaeger/OTEL Collector. Set `Telemetry:AzureMonitorConnectionString` and confirm custom metrics appear in Application Insights.

- [x] T012 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/InMemoryMetricSnapshotStore.cs` — `internal sealed class InMemoryMetricSnapshotStore : IMetricSnapshotStore` using a single `volatile` field for lock-free write/read. Only compiled for `!NETFRAMEWORK` (add `#if !NETFRAMEWORK` guard). — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: Current in-memory stores are `InMemoryJobMetricsStore` and `InMemoryJobSnapshotStore` registered via `Infrastructure.ControlPlane.Metrics`.
- [x] T013 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/SnapshotMetricExporter.cs` — `internal sealed class SnapshotMetricExporter : BaseExporter<Metric>`. Constructor receives `IMetricSnapshotStore`. `Export(Batch<Metric>)` iterates the batch, reads counter sums and histogram means, constructs a `MetricSnapshot`, calls `store.Update(snapshot)`. Add `#if !NETFRAMEWORK` guard. Map instrument names to snapshot fields (see `research.md §2` and `WorkItemExportMetrics` / `AttachmentDownloadMetrics` meter names). — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: `SnapshotMetricExporter` exists at `src/DevOpsMigrationPlatform.Infrastructure.ControlPlane/Metrics/SnapshotMetricExporter.cs` and targets `JobMetrics`.
- [x] T014 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/TelemetryServiceExtensions.cs` — `public static class TelemetryServiceExtensions` with `AddTelemetryServices(this IServiceCollection services, IConfiguration configuration)` extension. This method: (1) binds and validates `TelemetryOptions` via `services.AddOptions<TelemetryOptions>().BindConfiguration(TelemetryOptions.SectionName)`; (2) registers `InMemoryMetricSnapshotStore` as `IMetricSnapshotStore` singleton; (3) calls `services.AddOpenTelemetry().WithMetrics(mb => mb.AddReader(...))` adding a `PeriodicExportingMetricReader` that wraps `SnapshotMetricExporter`; (4) conditionally adds OTLP exporter if `options.OtlpEndpoint` is set; (5) conditionally adds Azure Monitor exporter if `options.AzureMonitorConnectionString` is set. Use `#if !NETFRAMEWORK` guard on the file. — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: Telemetry extensions are split into `Infrastructure.Agent.Telemetry/TelemetryServiceExtensions.cs` and `Infrastructure.ControlPlane.Metrics/TelemetryServiceExtensions.cs`; OTLP is handled in `ServiceDefaults/Extensions.cs`.
- [x] T015 [US1] Extend `src/DevOpsMigrationPlatform.ServiceDefaults/Extensions.cs` `AddOpenTelemetryExporters()` to read `TelemetryOptions` from configuration and conditionally add `AddAzureMonitorOpenTelemetry()` when `AzureMonitorConnectionString` is non-empty. OTLP is already handled via `OTEL_EXPORTER_OTLP_ENDPOINT`; do not duplicate it. — Status: complete
- [x] T016 [P] [US1] In `src/DevOpsMigrationPlatform.MigrationAgent/Program.cs`: call `builder.Services.AddTelemetryServices` — Status: complete/superseded; completed because superseded by specs/021.2-separation-of-concerns
  - Evidence: Telemetry registration occurs through `builder.AddMigrationAgentServices(...)` which calls `AddCoreAgentServices` then `AddAgentTelemetryServices`.
- [x] T017 [P] [US1] In `src/DevOpsMigrationPlatform.ControlPlane/Program.cs`: call `builder.Services.AddTelemetryServices(builder.Configuration)`. — Status: complete/superseded; completed because superseded by specs/021.2-separation-of-concerns
  - Evidence: Host entrypoint is `src/DevOpsMigrationPlatform.ControlPlaneHost/Program.cs`, which calls `builder.Services.AddControlPlaneTelemetryServices(builder.Configuration)`.
- [x] T018 [P] [US1] Add `"Telemetry": {}` stanza to `src/DevOpsMigrationPlatform.MigrationAgent/appsettings.json`. — Status: complete
- [x] T019 [P] [US1] Add `"Telemetry": {}` stanza to `src/DevOpsMigrationPlatform.ControlPlane/appsettings.json`. — Status: complete

**Checkpoint**: Run `dotnet build DevOpsMigrationPlatform.slnx -v quiet`. Start a local OTEL Collector or Jaeger and configure `Telemetry:OtlpEndpoint` — confirm spans/metrics arrive. US1 feature scenarios pass.

---

## Phase 4: User Story 2 — Agent → Control Plane Snapshot Push

**Goal**: While a job runs, the Migration Agent pushes a `MetricSnapshot` to the Control Plane every N seconds. The Control Plane stores the latest snapshot per job. Existing `ProgressEvent` push flow is unchanged.

**Independent Test**: In a stub integration test, start a `ControlPlaneTelemetryTimer`, verify it calls `IControlPlaneTelemetryClient.PushSnapshotAsync` with the current `IMetricSnapshotStore.Latest` value after approximately N seconds.

- [x] T020 [US2] Create `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/ControlPlaneTelemetryClient.cs` — `internal sealed class ControlPlaneTelemetryClient : IControlPlaneTelemetryClient`. Constructor receives `HttpClient` (registered via named client), `ILogger<ControlPlaneTelemetryClient>`. `PushSnapshotAsync` posts the snapshot as JSON to `/agents/lease/{leaseId}/telemetry`. On `404`/non-success, log warning only (best-effort; do not throw). Add `#if !NETFRAMEWORK` guard. — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: Client exists at `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneTelemetryClient.cs` and pushes to `/metrics` and `/snapshot`.
- [x] T021 [US2] Create `src/DevOpsMigrationPlatform.MigrationAgent/ControlPlaneTelemetryTimer.cs` — `internal sealed class ControlPlaneTelemetryTimer : BackgroundService`. Constructor receives `IMetricSnapshotStore`, `IControlPlaneTelemetryClient`, `IOptions<TelemetryOptions>`, `ILogger`. `ExecuteAsync` loops on `SnapshotIntervalSeconds` delay; reads `store.Latest`; if non-null and `leaseId` is set, calls `client.PushSnapshotAsync`. `LeaseId` is set externally by `MigrationAgentWorker` when a lease is acquired and cleared on release. — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: Timer exists at `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneTelemetryTimer.cs` and pushes `IJobMetricsStore.Latest`/`IJobSnapshotStore.Latest`.
- [x] T022 [US2] Create `src/DevOpsMigrationPlatform.ControlPlane/Services/JobTelemetryStore.cs` — `public sealed class JobTelemetryStore`. Wraps `ConcurrentDictionary<Guid, MetricSnapshot>`. Methods: `void Store(Guid jobId, MetricSnapshot snapshot)`, `MetricSnapshot? GetLatest(Guid jobId)`, `void Remove(Guid jobId)`. — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: Store is now `src/DevOpsMigrationPlatform.ControlPlane/Jobs/JobMetricsStore.cs` with `JobMetrics` model and merge semantics.
- [x] T023 [US2] Create `src/DevOpsMigrationPlatform.ControlPlane/Controllers/TelemetryController.cs` — `[ApiController]` with `POST /agents/lease/{leaseId}/telemetry` action. Validates `leaseId` maps to a known active lease (stub: accept any non-empty for now, full lease validation is `MigrationAgentWorker` scope). Calls `JobTelemetryStore.Store(jobId, snapshot)`. Returns `204`. Add `[Authorize]` placeholder. — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: Controller exists with lease resolution, but push endpoint is `POST /agents/lease/{leaseId}/metrics` and model is `JobMetrics`.
- [x] T024 [US2] Register services in Program.cs files — Status: complete

**Checkpoint**: `dotnet build`. Stub end-to-end: POST a JSON body to `/agents/lease/test/telemetry` and confirm `204`. US2 feature scenarios pass.

---

## Phase 5: User Story 3 — TFS Subprocess Metric Relay

**Goal**: The .NET 4.8 TFS export subprocess embeds a `MetricSnapshot` in its stdout NDJSON `ProgressEvent` every 100 revisions. The parent `TfsExporterProcessAdapter` extracts it and forwards to the Control Plane via `IControlPlaneTelemetryClient`.

**Independent Test**: Run the subprocess in standalone mode; verify stdout lines at revision 100, 200, 300, etc. contain a non-null `metrics` field with non-zero `workItemsExported`.

- [x] T025 [US3] Extend `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/WorkItemExportService.cs` — Status: complete/superseded; completed because the TFS export pipeline was refactored to `TfsMigrationAgent` + `Infrastructure.TfsObjectModel` revision-source/telemetry components, so `WorkItemExportService.cs` no longer exists as the integration surface.
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel` contains `TfsWorkItemRevisionSource`, `WorkItemExportMetrics`, and related export seams but no `WorkItemExportService.cs`; metrics/progress emission is handled in the current worker/runtime path.
- [x] T026 [US3] Add `SubprocessSnapshotRevisionInterval` property to `Settings` in `MigrationPlatformHost.cs`. — Status: complete
- [x] T027 [US3] Create `src/DevOpsMigrationPlatform.CLI.Migration/TfsExporterProcessAdapter.cs` — Status: complete/superseded; completed because superseded by specs/023.5-tfsmigrationagent-architectural-consistency
  - Evidence: TFS execution path is now dedicated `TfsMigrationAgent` architecture; no `TfsExporterProcessAdapter.cs` exists under CLI.
- [x] T028 [US3] IControlPlaneTelemetryClient registered in MigrationAgent/Program.cs (done in Phase 4 T024). — Status: complete

**Checkpoint**: Build + run subprocess unit tests. Confirm `ProgressEvent` JSON on stdout includes `"metrics": {...}` at revision 100 intervals. US3 feature scenarios pass.

---

## Phase 6: User Story 4 — TUI Metrics Panel

**Goal**: When the TUI is watching a running job, a live metrics panel refreshes every 5 seconds showing all `MetricSnapshot` fields: counts, error rates, and mean durations.

**Independent Test**: Call `GET /jobs/{known-jobId}/telemetry` via curl after Phase 4 has pushed a snapshot — confirm `200` with expected JSON body.

- [x] T029 [US4] Add `GET /jobs/{jobId}/telemetry` action to `src/DevOpsMigrationPlatform.ControlPlane/Controllers/TelemetryController.cs` — Status: complete
- [x] T030 [US4] Create `src/DevOpsMigrationPlatform.CLI.Migration/Views/TelemetryPanel.cs` — Status: complete
- [x] T031 [US4] Create `src/DevOpsMigrationPlatform.CLI.Migration/Views/TelemetryPoller.cs` — Status: complete
- [x] T032 [US4] Integrate `TelemetryPanel` and `TelemetryPoller` into `TfsExportCommand.cs`. — Status: complete/superseded; completed because superseded by specs/008-tui-job-dashboard
  - Evidence: `TfsExportCommand.cs` is not present; live telemetry rendering is implemented in `TuiMainView`/`TuiMetricsView` and queue follow workflows.

**Checkpoint**: Run `dotnet build`. Launch TUI against a running job and verify the metrics panel appears and refreshes. US4 feature scenarios pass.

---

## Phase 7: Polish & Cross-Cutting

**Purpose**: Tidy up residual references, fix test file names in plan.md, and confirm the full pipeline works end-to-end.

- [x] T033 ~~Update `specs/first-coding-session/plan.md` project structure tree: rename `InProcessMeterListenerTests.cs` → `SnapshotMetricExporterTests.cs`~~ — applied directly during analysis remediation. — Status: complete
- [x] T034 [P] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/SnapshotMetricExporterTests.cs` — unit tests: (a) exporter with zero-measurement batch produces snapshot with all nulls; (b) exporter with counter increment produces correct `WorkItemsExported`; (c) `InMemoryMetricSnapshotStore.Update` + `Latest` round-trip. — Status: complete/superseded; completed by the current Control Plane metrics test suite after test-project split.
  - Evidence: `tests/DevOpsMigrationPlatform.Infrastructure.ControlPlane.Tests/Metrics/InMemoryJobMetricsStoreTests.cs` covers the store update/latest round-trip behaviors in the active project structure; old `Infrastructure.Tests` path is deprecated.
- [x] T035 [P] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Telemetry/ControlPlaneTelemetryClientTests.cs` — unit tests using `MockHttpMessageHandler`: (a) successful `204` response does not throw; (b) `404` response logs warning and does not throw; (c) request body is valid `MetricSnapshot` JSON. — Status: complete/superseded; completed because superseded by specs/021.2-separation-of-concerns
  - Evidence: Equivalent tests exist at `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Telemetry/ControlPlaneTelemetryClientTests.cs`.
- [x] T036 Run `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Tests/DevOpsMigrationPlatform.Infrastructure.Tests.csproj --logger "console;verbosity=quiet"` — confirm all tests pass. — Status: complete/superseded; completed because superseded by specs/021.2-separation-of-concerns
  - Evidence: The old test project path no longer exists; equivalent suite passes in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests.csproj`.

---

## Phase 8: Missing Infrastructure (from Analysis)

**Purpose**: Three structural gaps identified during consistency analysis that must be implemented for the push pipeline to function.

- [x] T037 [US1] Create `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMeterNames.cs` — `public static class WellKnownMeterNames` with `public const string WorkItemExport = "DevOpsMigrationPlatform.WorkItemExport"` and `public const string AttachmentDownload = "DevOpsMigrationPlatform.AttachmentDownload"` (see `data-model.md`). Update `WorkItemExportMetrics.MeterName` and `AttachmentDownloadMetrics.MeterName` in `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel` to reference these constants instead of re-declaring the strings. — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: `WellKnownMeterNames` now defines `Agent`, `ControlPlane`, and `Cli` meters, with unified platform metric naming.
- [x] T038 [US2] Create `src/DevOpsMigrationPlatform.MigrationAgent/ActiveLeaseState.cs` — `public sealed class ActiveLeaseState` with `public string? CurrentLeaseId { get; set; }` (volatile field or lock-guarded property for thread safety). Register as singleton in `MigrationAgent/Program.cs`. Inject into `ControlPlaneTelemetryTimer` (reads `CurrentLeaseId`) and `MigrationAgentWorker` (sets `CurrentLeaseId` when a lease is acquired, clears it on release). — Status: complete/superseded; completed because superseded by specs/021.2-separation-of-concerns
  - Evidence: Canonical class is `src/DevOpsMigrationPlatform.Abstractions.Agent/Lease/ActiveLeaseState.cs`; `MigrationAgent/ActiveLeaseState.cs` is now an alias shim.
- [x] T039 [US2] Modify `src/DevOpsMigrationPlatform.ControlPlane/Controllers/TelemetryController.cs` `POST /agents/lease/{leaseId}/telemetry`: resolve `leaseId → jobId` via an `ILeaseJobResolver` interface (or a stub `Dictionary<string,Guid>` singleton for Phase 1). Call `JobTelemetryStore.Store(jobId, snapshot)`. Create `src/DevOpsMigrationPlatform.ControlPlane/Services/ILeaseJobResolver.cs` (interface) and `StubLeaseJobResolver.cs` (Phase 1 implementation: extracts `jobId` from lease payload stored in memory when `GET /agents/lease` assigns a lease). Register both in `ControlPlane/Program.cs`. — Status: complete/superseded; completed because superseded by specs/031-platform-metrics-unification
  - Evidence: Lease resolution is implemented with `src/DevOpsMigrationPlatform.ControlPlane/Jobs/ILeaseJobResolver.cs` and `StubLeaseJobResolver.cs`; telemetry push endpoint now uses `/metrics`.

---

## Dependencies

```
Phase 1 (T001-T003)
  └─► Phase 2 (T004-T011) — all Abstractions types must exist before Phase 3+
        ├─► Phase 3 (T012-T019) — cloud export; depends on TelemetryOptions + IMetricSnapshotStore
        ├─► Phase 4 (T020-T024) — push pipeline; depends on IControlPlaneTelemetryClient + MetricSnapshot
        ├─► Phase 5 (T025-T028) — subprocess relay; depends on ProgressEvent.Metrics + IControlPlaneTelemetryClient
        └─► Phase 6 (T029-T032) — TUI panel; depends on Phase 4 GET endpoint
              └─► Phase 7 (T033-T036) — Polish; depends on all prior phases
```

**Parallel opportunities within phases**:
- Phase 1: T001 ∥ T002 (different csproj files)
- Phase 2: T004→T011 are all parallel once T003 is green (all separate files)
- Phase 3: T012 ∥ T013 (different files); T016 ∥ T017 ∥ T018 ∥ T019 (different hosts)
- Phase 6: T030 ∥ T031 (different files); T029 unblocks T031

---

## Implementation Strategy

**MVP**: Phases 1–4 only. This delivers:
- Live metrics visible in the TUI (Control Plane stores + serves latest snapshot)
- Metrics flow to OTLP or Azure Monitor if configured
- No subprocess changes needed (snapshot is `null` until Phase 5)

**Phase 5** adds subprocess metric relay (for jobs that use the .NET 4.8 TFS exporter).
**Phase 6** adds the Terminal.Gui real-time panel.
**Phase 7** polishes and validates.

---

## Task Count Summary

| Phase | Tasks | Parallelisable |
|---|---|---|
| 1 — Setup | 3 | 2 |
| 2 — Foundational | 8 | 7 |
| 3 — US1 Cloud Export | 8 | 4 |
| 4 — US2 Push Pipeline | 5 | 0 |
| 5 — US3 Subprocess Relay | 4 | 0 |
| 6 — US4 TUI Panel | 4 | 2 |
| 7 — Polish | 4 | 2 |
| 8 — Missing Infrastructure | 3 | 1 |
| **Total** | **39** | **18** |
