# Architecture Discrepancies

**Feature**: Three-Channel Observability
**Flagged by**: speckit.specify
**Status**: Resolved

## Discrepancies

### 1. Progress endpoint misnamed as "logs" in control-plane.md
- **Source doc**: `docs/control-plane.md`
- **Section**: API Surface → Job Lifecycle table
- **Issue**: `GET /jobs/{jobId}/logs` and `GET /jobs/{jobId}/logs?follow=true` serve `ProgressEvent` records, not diagnostic logs. The spec renames these to `/progress`.
- **Suggested update**: Rename endpoint paths from `/logs` to `/progress` in the API table. Add new `/diagnostics` and `/diagnostics?follow=true` endpoints. Add `/logs/download` endpoint for package log file retrieval.
- **Status**: ✓ Resolved in speckit.implement

### 2. Progress reporting section in control-plane.md references "/logs"
- **Source doc**: `docs/control-plane.md`
- **Section**: Progress Reporting
- **Issue**: Text says "Powers `GET /jobs/{jobId}/logs`" and "Powers `GET /jobs/{jobId}/logs?follow=true`". Should reference `/progress`.
- **Suggested update**: Replace `/logs` with `/progress` in both references.
- **Status**: ✓ Resolved in speckit.implement

### 3. TUI status display references "/logs" for progress streaming
- **Source doc**: `docs/tui.md`
- **Section**: Status Display (Remote Mode) table
- **Issue**: Progress table endpoint listed as `GET /jobs/{jobId}/logs?follow=true`. Should be `/progress?follow=true`. No diagnostics panel is documented.
- **Suggested update**: Rename endpoint to `/progress?follow=true`. Add a third row to the table for the diagnostics panel streaming from `GET /jobs/{jobId}/diagnostics?follow=true`.
- **Status**: ✓ Resolved in speckit.implement

### 4. CLI "manage logs" command conflates progress with diagnostics
- **Source doc**: `docs/cli.md` and `.agents/context/cli-commands.md`
- **Section**: Job Management Commands (`manage`) table; Command Registration Pattern
- **Issue**: `manage logs` command described as fetching/streaming `ProgressEvent` records. The spec replaces it: `manage logs` becomes `manage diagnostics` (downloads package logs for completed jobs) and a new `manage progress` is added (snapshot of progress events, no `--follow`). Neither command supports `--follow` — all live streaming is TUI-only.
- **Suggested update**: Replace `manage logs` with `manage diagnostics` (download helper for completed job package logs with `--level` filter) and add `manage progress` (snapshot of `ProgressEvent` records). Remove `--follow` from both. Update `ExportCommand` docs to include `--follow` and `--level` options. Update command registration in `Program.cs` section.
- **Status**: ✓ Resolved in speckit.implement

### 8. Export command missing --follow and --level options
- **Source doc**: `docs/cli.md` and `.agents/context/cli-commands.md`
- **Section**: Migration Commands table; ExportCommandSettings
- **Issue**: The `export` command does not document `--follow` or `--level` options. The spec adds both: `--follow` streams diagnostic logs inline (implicit in standalone), `--level` sets the agent's diagnostic minimum level per job.
- **Suggested update**: Add `--follow` and `--level` to the `export` command settings table. Document standalone vs remote lifecycle. Add to `ExportCommandSettings`.
- **Status**: ✓ Resolved in speckit.implement

### 9. No documentation of tiered log level architecture
- **Source doc**: `docs/architecture.md`, `docs/control-plane.md`
- **Section**: Observability / Progress Reporting
- **Issue**: No documentation of the agent/CP log level independence. The agent's per-job log level (from `--level`) is independent of the CP's deployment-level minimum. The CP filters incoming diagnostics records below its own floor before buffering, streaming via SSE, or exporting to App Insights.
- **Suggested update**: Add a "Tiered Observability Levels" section to `docs/architecture.md` documenting the three-tier model (agent → CP filter → App Insights). Add a "Diagnostics Level Filtering" section to `docs/control-plane.md`.
- **Status**: ✓ Resolved in speckit.implement

### 5. Architecture.md progress section uses "logs" naming
- **Source doc**: `docs/architecture.md`
- **Section**: "Progress is Event-Driven"
- **Issue**: References `ConsoleProgressSink` (should be `AnsiProgressSink` to match code), `GET /jobs/{jobId}/logs?follow=true` (should be `/progress?follow=true`). No mention of diagnostic log streaming or the diagnostics channel.
- **Suggested update**: Correct sink name, rename endpoint, and add a paragraph describing the diagnostics channel (ILogger → `Logs/agent.jsonl` + control plane streaming).
- **Status**: ✓ Resolved in speckit.implement

### 6. Package format does not specify Logs/ folder contents
- **Source doc**: `.agents/context/package-format.md`
- **Section**: Package Structure
- **Issue**: `Logs/` is listed in the package structure tree but its expected contents are not documented.
- **Suggested update**: Add a `Logs/` subsection specifying: `progress.jsonl` (NDJSON of `ProgressEvent` records) and `agent.jsonl` (NDJSON of structured diagnostic log records).
- **Status**: ✓ Resolved in speckit.implement

### 7. Architecture Phase 2 items now implemented by this spec
- **Source doc**: `docs/architecture.md`
- **Section**: Implementation Priority → Phase 2
- **Issue**: Items 17 (`ControlPlaneProgressSink`), 18 (`JobProgressStore` ring buffer + endpoints), and 19 (`migrate logs --follow`) are listed as Phase 2 future work. The progress streaming infrastructure already exists in code. This spec completes the package persistence (item 7 `PackageProgressSink` from Phase 1) and adds the diagnostics channel.
- **Suggested update**: Mark items 7, 17, 18, 19 as completed or in-progress. Add new items for the diagnostics channel, endpoint rename, and TUI diagnostics panel.
- **Status**: ✓ Resolved in speckit.implement
