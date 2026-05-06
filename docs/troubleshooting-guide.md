# Troubleshooting Guide

Audience: Operators.

## Authentication Failures

**Symptom:** Job fails immediately with `401 Unauthorized` or `TF400813`.

**Causes and fixes:**
- Token expired — generate a new PAT.
- Wrong scope — PAT needs at minimum: Work Items (Read), Project and Team (Read).
- `$ENV:VARNAME` not set — verify `echo $env:VARNAME` before running.
- Org URL incorrect — confirm with `https://dev.azure.com/<org>/_apis/projects`.

## Missing Permissions

**Symptom:** Job fails with `TF200009` or `403 Forbidden`.

**Fixes:**
- Source export requires read-only access to the project.
- Target import requires Project Administrator or Contribute access.
- TFS: verify Windows credentials have access to the collection.

## Package Path Issues

**Symptom:** `Package not found` or `ArtefactStoreException`.

**Causes and fixes:**
- `WorkingDirectory` path does not exist — create it before running.
- Insufficient disk space — the package can be large; allow at least 2x the source project size.
- Path contains special characters — use a simple path without spaces or non-ASCII characters.

## Failed Jobs

**Symptom:** Job enters `Failed` state.

**Diagnosis:**
1. Run `devopsmigration manage logs --job <id>` to retrieve structured logs.
2. Check `.migration/Logs/progress.jsonl` in the package for per-event diagnostics.
3. Check `.migration/Logs/` for error-level entries.

**Safe to re-run:** Most failures are transient. Re-queue the same job — checkpointing ensures work already done is not repeated.

## Resume Problems

**Symptom:** Re-run starts from the beginning instead of resuming.

**Causes:**
- `WorkingDirectory` changed between runs — the cursor is in the package.
- Package was moved or deleted.
- `ConfigVersion` schema mismatch — check `migration-config.json` at the package root.

**Fix:** Point `WorkingDirectory` to the same location as the previous run. The cursor in `.migration/Checkpoints/` is the resume state.

## Telemetry Gaps

**Symptom:** Metrics panel shows zeros or counters stop updating.

**Causes:**
- TUI is reading from `ProgressEvent.Metrics` — this is only populated by the TFS agent. For .NET 10 agents, counters come from `GET /jobs/{id}/telemetry`.
- Control Plane not reachable — check `Environment.ControlPlane.BaseUrl`.

## Configuration Errors

**Symptom:** `ConfigValidationException` on job submission.

**Fix:**
- Validate against `migration.schema.json` using a JSON schema validator.
- Ensure all required fields are present: `ConfigVersion`, `Mode`, `Package.WorkingDirectory`, `Source.Type`, `Source.Url`.
- Check boolean flags are `true`/`false` not `"true"`/`"false"`.

## Where to Find Logs

| Location | Contents |
|---|---|
| `.migration/Logs/progress.jsonl` | Per-event progress log (in package) |
| `.migration/Logs/*.log` | Module diagnostic logs (in package) |
| Control Plane API: `GET /jobs/{id}/diagnostics` | Structured log stream from the running agent |
| Control Plane API: `GET /jobs/{id}/progress` | SSE progress event stream |

## Further Help

- See [`operator-guide.md`](operator-guide.md) for common workflows.
- See [`configuration-reference.md`](configuration-reference.md) for schema details.
- See [`migration-process-guide.md`](migration-process-guide.md) for phase behaviour.