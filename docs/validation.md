# Validation

## 14. Validation

Validation runs at two points in the lifecycle: **pre-flight** (before import begins) and **post-flight** (after import completes). Each is mandatory. Fail-fast is the default; continue-on-error must be explicitly configured.

---

## Pre-Flight Validation

Pre-flight validation runs:

- Before any `ImportAsync` call in **Import** mode.
- Between `ExportAsync` and `ImportAsync` in **Both** mode.
- As part of each module's `ValidateAsync` during the orchestrator's pre-execution pass.

### Required Checks

| Check | Description |
|---|---|
| Manifest schema | `manifest.json` must exist, be valid JSON, and conform to the declared `packageVersion`. |
| Required folders | All folders declared in `manifest.json` `includedTypes` must be present under `PackageRoot/`. |
| revision.json validity | Every `revision.json` must be valid JSON and contain all required fields (`workItemId`, `revisionIndex`, `changedDate`, `fields`, `externalLinks`, `relatedLinks`, `hyperlinks`, `attachments`). |
| Attachment existence | Every attachment entry in `revision.json` must have a corresponding file at the declared `relativePath` within the same folder. |
| Attachment hash | The `sha256` and `size` values in each attachment entry must match the file on disk. |
| Identity mapping integrity | The `mapping.json` in `Identities/` must be valid JSON. All referenced source identities must appear in `descriptors.jsonl`. |

### Failure Behaviour

- Any check failure causes immediate termination unless `policies.validation.continueOnError` is `true` in configuration.
- All failures are written to `Logs/` with enough detail to identify the offending file and field.
- The run does not begin import if any pre-flight check fails under the default policy.

---

## Post-Flight Validation

Post-flight validation runs after all `ImportAsync` calls complete.

### Required Checks

| Check | Description |
|---|---|
| Work item counts | The number of work items written to the target must match the number exported, within a configurable tolerance (see `policies.validation.workItemCountTolerance`). |
| Link integrity | A sample of imported work items must have their expected links present on the target. |
| Attachment integrity | A sample of revision folders with attachments must have those attachments present and reachable on the target. |
| Unresolved identities | All identities that could not be resolved during import must be recorded in `Identities/unresolved.json`. The presence of unresolved identities is logged as a warning, not an error, unless `policies.validation.failOnUnresolvedIdentities` is `true`. |
| Deterministic completion | The cursor for every module must be at `Completed` for the final item in its stream. Any cursor not at `Completed` is a validation failure. |

### Configuration

```json
"policies": {
  "validation": {
    "continueOnError": false,
    "workItemCountTolerance": 0,
    "failOnUnresolvedIdentities": false,
    "sampleRate": 0.05
  }
}
```

| Field | Default | Description |
|---|---|---|
| `continueOnError` | `false` | If `true`, pre-flight failures are logged but do not halt the run. Not recommended for production. |
| `workItemCountTolerance` | `0` | Number of work items that may be missing from the target without triggering a failure. `0` means exact match required. |
| `failOnUnresolvedIdentities` | `false` | If `true`, any unresolved identity causes a post-flight failure. |
| `sampleRate` | `0.05` | Fraction of items sampled for post-flight spot checks (links and attachments). `1.0` = full verification. |

---

## Module ValidateAsync Contract

Each module's `ValidateAsync` is called during the pre-flight pass. It must:

- Check that all required fields are present in every artefact file it owns.
- Check schema version compatibility against `manifest.json`.
- Report anomalies to `Logs/` rather than silently skipping them.
- Fail fast on missing required fields (unless `continueOnError` is set).
- Have no side effects on the package or the target system.

See [docs/modules.md](modules.md) for the full `IDataTypeModule` contract and [agents/module-template.md](../agents/module-template.md) for the per-module validation checklist.

---

## Validation Report

After validation completes, a machine-readable report is written to `Logs/validation-report.json`:

```json
{
  "runAt": "2026-02-25T18:30:00Z",
  "phase": "PreFlight | PostFlight",
  "passed": true,
  "checks": [
    {
      "name": "ManifestSchema",
      "passed": true,
      "detail": null
    },
    {
      "name": "AttachmentHash",
      "passed": false,
      "detail": "Hash mismatch: WorkItems/2026-02-25/638760123456789012-12345-17/screenshot.png"
    }
  ]
}
```

The report is written regardless of pass/fail to allow tooling to inspect results independently of the run exit code.
