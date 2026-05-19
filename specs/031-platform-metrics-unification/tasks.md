# Tasks: Platform Metrics Unification (Reconciled)

**Reconciled**: 2026-05-17  
**Authority order used**: `.agents` guidance → newer specs (032-035) → this spec → implementation evidence

- [X] T001 [D1] Consolidate agent meter to `WellKnownMeterNames.Agent` (`DevOpsMigrationPlatform.Agent`) — Status: complete
- [ ] T002 [D2] Enforce fully unified `platform.<domain>.<phase>.<measure>` naming across all components — Status: incomplete
- [X] T003 [D3] Replace dual agent metric interfaces with `IPlatformMetrics` — Status: complete
- [X] T004 [D4] Consolidate constants into `WellKnownAgentMetricNames` — Status: complete
- [X] T005 [D5] Consolidate implementations into `PlatformMetrics` — Status: complete
- [X] T006 [D6] Rename control plane metric constants to `WellKnownControlPlaneMetricNames` with `platform.job.*` strings — Status: complete
- [X] T007 [D7] Rename CLI metric strings to `platform.command.*` — Status: complete
- [X] T008 [D8] Keep `WellKnownMeterNames.ControlPlane` and `.Cli` unchanged — Status: complete
- [ ] T009 [D9] Apply abstraction package version bump for the breaking metric contract change — Status: incomplete
- [X] T010 [D10] Keep old constants as `[Obsolete]` tombstones — Status: complete/superseded; superseded by clean-break decision in `spec.md` § Implementation Notes
- [X] T011 [D11] Restructure `WellKnownTagNames` into nested groups — Status: complete
- [X] T012 [D12] Remove high-cardinality `WorkItemId` and `RevisionIndex` tag constants — Status: complete
- [ ] T013 [BP-1] Publish release-note mapping for all renamed metric strings — Status: incomplete
- [ ] T014 [BP-2] Document ops migration/runbook updates for dashboards/alerts on old prefixes — Status: incomplete
- [ ] T015 [DOC-1] Remove stale `WellKnownMetricNames` XML doc references — Status: incomplete

## Incomplete evidence notes

- **T002**: `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Telemetry/WorkItemExportMetrics.cs` and `AttachmentDownloadMetrics.cs` still emit legacy literal names (`work_item_*`, `attachment_*`) instead of `platform.*`.
- **T009**: `src/DevOpsMigrationPlatform.Abstractions/DevOpsMigrationPlatform.Abstractions.csproj` and `src/DevOpsMigrationPlatform.Abstractions.Agent/DevOpsMigrationPlatform.Abstractions.Agent.csproj` do not define `<Version>` for the declared bump.
- **T013**: No release-note artifact in this spec folder or adjacent docs capturing the full rename migration.
- **T014**: No runbook update artifact in this spec folder documenting old-prefix dashboard/alert migration completion.
- **T015**: `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/MigrationDiagnostics.cs` still references removed `WellKnownMetricNames` in XML docs.

## Superseded evidence notes

- **T010 supersession source**: `specs/031-platform-metrics-unification/spec.md` § Implementation Notes explicitly records clean-break replacement of obsolete tombstones.
