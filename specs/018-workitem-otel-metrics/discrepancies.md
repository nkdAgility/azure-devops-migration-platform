# Architecture Discrepancies

**Feature**: Work Item OpenTelemetry Metrics
**Flagged by**: speckit.specify
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### Metric naming convention not documented
- **Source doc**: `docs/configuration.md`
- **Section**: (absent — no telemetry section exists)
- **Issue**: The spec defines a `migration.` dot-separated naming convention for all OTel instruments (FR-001). No existing doc describes the naming convention for metrics. The current codebase uses underscore-separated names (`work_item_exported_total`) with no documented standard.
- **Suggested update**: Add a "Telemetry" or "Observability" section to `docs/configuration.md` (or a new `docs/observability.md`) documenting the `migration.` dot-separated naming convention and the mandatory dimension tags (`job.id`, `operation`, `module`).

### Existing metric names in WellKnownMetricNames being replaced
- **Source doc**: `docs/architecture.md`
- **Section**: Phase 2, item 20 ("CLI-level OpenTelemetry")
- **Issue**: Phase 2 item 20 mentions CLI-level OTel but does not list specific instrument names. The spec replaces all existing underscore-separated names with dot-separated names (FR-002). The architecture doc should reflect this convention once implemented.
- **Suggested update**: Add a reference in Phase 2 item 20 or a new section noting that all migration metrics follow the `migration.*` dot-separated convention defined in `WellKnownMetricNames`.

### MetricSnapshot expansion not reflected in control plane docs
- **Source doc**: `docs/control-plane.md`
- **Section**: `GET /jobs/{jobId}/telemetry` endpoint
- **Issue**: The control plane doc references `MetricSnapshot` but does not enumerate its fields. The spec expands `MetricSnapshot` significantly (FR-032, FR-033). The endpoint description should note that the payload evolves as new instruments are added.
- **Suggested update**: Add a note to the `/telemetry` endpoint documentation stating that `MetricSnapshot` is a versioned DTO whose fields correspond to registered OTel instruments, and link to the `WellKnownMetricNames` constant class as the canonical reference.

### Tier 3 validation does not currently reference OTel metric emission
- **Source doc**: `docs/validation.md`
- **Section**: Tier 3 — Post-Flight Validation
- **Issue**: The spec (FR-024) requires correctness metrics to be emitted during Tier 3 post-flight validation. The existing Tier 3 doc describes checks (work item counts, link integrity, attachment integrity) but does not mention OTel metric emission as part of the validation pass.
- **Suggested update**: Add a paragraph to Tier 3 noting that post-flight validation emits OTel metrics (count parity histograms and error counters) alongside the `validation-report.json` output, and that these metrics respect the `sampleRate` configuration.

### Single meter name replacing two existing meters
- **Source doc**: `docs/aspire-integration.md`
- **Section**: `ConfigureOpenTelemetry` code sample
- **Issue**: The spec (FR-035) consolidates `DevOpsMigrationPlatform.WorkItemExport` and `DevOpsMigrationPlatform.AttachmentDownload` into a single `DevOpsMigrationPlatform.Migration` meter. The Aspire integration doc's code sample may need to register the new meter name.
- **Suggested update**: Update the `ConfigureOpenTelemetry` sample to register `DevOpsMigrationPlatform.Migration` instead of the two separate meter names.
