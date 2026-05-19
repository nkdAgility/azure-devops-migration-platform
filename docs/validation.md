# Validation

## 14. Validation

Validation runs at four points in the lifecycle. Fail-fast is the default at every tier; continue-on-error must be explicitly configured.

| Tier | When | Who runs it | Network required |
|---|---|---|---|
| **0 — Structural** | Before CLI submits anything | CLI | No |
| **1 — Connectivity** | Before CLI submits anything | CLI | Yes |
| **2 — Pre-flight** | Before import begins | Migration Agent / Job Engine | Yes |
| **3 — Post-flight** | After import completes | Migration Agent / Job Engine | Yes |

Tiers 0 and 1 together are the **CLI pre-validation pass**. The CLI creates a `Job` (with `ConfigPayload`) and submits it to the control plane only if both tiers pass.

---

## Tier 0 — Structural Validation (no network)

Runs entirely locally. No credentials are used. No network calls are made.

### Required Checks

| Check | Description |
|---|---|
| Config parses | The config file must be valid JSON. |
| Schema version | `configVersion` must be a version supported by this CLI binary. |
| Required fields | `mode`, `artefacts.path`, and `modules` must be present. |
| Mode value | `mode` must be `Inventory`, `Export`, `Prepare`, `Import`, `Validate`, or `Migrate`. |
| Module names | Every entry in `modules[].name` must match a module registered in the CLI binary. |
| Module scope schema | Each module's `scopes[].parameters` must conform to the JSON Schema bundled with that module in the CLI binary. |
| Policy ranges | Retry `max` and concurrency `maxConcurrency` must be positive integers within allowed bounds. |
| Path normalisation | `artefacts.path` must be normalisable to a valid URI (`file:///` or a standard Azure Blob Storage HTTPS URL). |

Module scope schemas are bundled inside the CLI binary — one JSON Schema file per module. This lets the CLI catch obvious config errors (missing `query` in a `wiql` scope, unknown field names) without any network call.

### Failure Behaviour

Any structural failure causes the CLI to exit immediately with a human-readable error message identifying the field and the violation. No control plane call is made.

---

## Tier 1 — Connectivity and Permission Checks (network required)

Runs after Tier 0 passes. Verifies that the operator has the access needed to execute the job before committing it to the queue.

### Required Checks

| Check | Applies to | Description |
|---|---|---|
| Source reachable | `Inventory`, `Export`, `Migrate` | The source org/collection URL returns a successful response. |
| Source project exists | `Inventory`, `Export`, `Migrate` | The specified source project exists in the source org. |
| Source read permissions | `Inventory`, `Export`, `Migrate` | The source credentials have at minimum read access to work items in the source project. |
| Target reachable | `Prepare`, `Import`, `Validate`, `Migrate` | The target org URL returns a successful response. |
| Target project exists | `Prepare`, `Import`, `Validate`, `Migrate` | The specified target project exists in the target org. |
| Target write permissions | `Import`, `Migrate` | The target credentials have at minimum write access to work items in the target project. |
| Target read permissions | `Prepare`, `Validate` | The target credentials have at minimum read access to the target project (Prepare and Validate only query, they do not write to the target). |
| Package URI accessible | All | For `file:///`: the path exists (export) or is writable (import). For Azure Blob Storage URLs (`https://*.blob.core.windows.net/...`): the container exists and credentials are valid (SAS token or `DefaultAzureCredential`). |

### Failure Behaviour

Any connectivity failure causes the CLI to exit with an actionable error message (e.g. "Source project 'MyProject' not found in collection — verify the `source.project` field and credentials"). No control plane call is made.

### What Connectivity Checks Do NOT Do

- They do not execute any migration work.
- They do not verify that specific work items match the configured WIQL query (that is expensive and deferred to the agent).
- They do not guarantee the migration will succeed — only that the prerequisites are met.

---

## Job Creation

After Tiers 0 and 1 pass, the CLI:

1. Normalises `artefacts.path` to a URI (`packageUri`).
2. Assigns a UUID `jobId`.
3. Computes `configHash` (SHA-256 of the normalised config JSON).
4. Constructs the `Job` (serialises config into `ConfigPayload`, sets `Kind`, `Connectors`, and `Diagnostics`).
5. Serialises and submits it to the control plane.

The control plane performs a deduplication check on `jobId` and a final schema validation before accepting the job.

---

---

## Tier 2 — Pre-Flight Validation

Pre-flight validation runs:

- Before any `ImportAsync` call in **Import** mode (after the Prepare gate passes).
- Between `ExportAsync` and `ImportAsync` in **Migrate** mode (after Prepare completes).
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
- All failures are written to `.migration/Logs/` with enough detail to identify the offending file and field.
- The run does not begin import if any pre-flight check fails under the default policy.

---

## Tier 3 — Post-Flight Validation

Post-flight validation runs after all `ImportAsync` calls complete. It also runs as the explicit **Validate** pipeline phase when `mode: Validate` is used.

Tier 3 checks can be triggered in three ways:
1. **Automatically** — at the end of Import or Migrate mode.
2. **Explicitly** — by running `mode: Validate` as a standalone phase after import.
3. **Re-run** — Validate is idempotent and can be re-run at any time to re-check the target.

In addition to writing `validation-report.json`, the post-flight validation pass emits OTel metrics via `IMigrationMetrics` — count parity histograms (`migration.correctness.revision_source_count`, `migration.correctness.revision_target_count`, `migration.correctness.revision_delta`) and error counters (`migration.correctness.broken_links`, `migration.correctness.missing_workitems`). These metrics respect the `sampleRate` configuration and are recorded under the consolidated `DevOpsMigrationPlatform.Migration` meter.

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
- Report anomalies to `.migration/Logs/` rather than silently skipping them.
- Fail fast on missing required fields (unless `continueOnError` is set).
- Have no side effects on the package or the target system.

See [docs/module-development-guide.md](module-development-guide.md) for the full `IModule` contract and [.agents/20-guardrails/domains/module-rules.md](../.agents/20-guardrails/domains/module-rules.md) for the per-module validation checklist.

---

## Validation Report

After validation completes, a machine-readable report is written to `.migration/Logs/validation-report.json`:

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

