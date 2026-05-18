# Architecture Discrepancies

**Feature**: TUI Job Dashboard
**Flagged by**: speckit.specify
**Status**: Reconciled

## Discrepancies

### Missing `/jobs/{jobId}/telemetry` endpoint in control-plane.md

- **Source doc**: `docs/tui-guide.md`
- **Section**: "Status Display (Remote Mode)" table
- **Issue**: `docs/tui-guide.md` lists `GET /jobs/{jobId}/telemetry` as the endpoint for the Metrics Panel ("counts, rates"), but this endpoint does not appear in the API surface table in `docs/control-plane.md`. All other endpoints referenced in the TUI doc (`/progress`, `/diagnostics`, `/progress?follow=true`, `/diagnostics?follow=true`) are present in `control-plane.md`.
- **Resolution**: `GET /jobs/{jobId}/telemetry` is the canonical Metrics Panel data source (confirmed by user, 2026-04-09). FR-006 in `spec.md` has been updated accordingly. The doc gap will be closed by T038/T039 which add this endpoint to the `docs/control-plane.md` Job Lifecycle API table.
- **Status**: Resolved — implementation task T038 covers the doc update.
