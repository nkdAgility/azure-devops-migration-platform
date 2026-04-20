# Contract: Simulated Target Configuration

**Version**: 1.0  
**Applies to**: `target.type == "Simulated"`  
**Location in codebase**: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Options/SimulatedTargetOptions.cs`

---

## JSON Configuration Schema

When `target.type` is `"Simulated"`, the following fields are accepted under the `target` block. No `orgOrCollection`, `project`, `apiVersion`, or `authentication` fields are required or used.

```json
{
  "target": {
    "type": "Simulated",
    "validateOnWrite": true,
    "failOnFirstError": true
  }
}
```

---

## Field Reference

| Field | JSON key | Type | Required | Default | Description |
|-------|----------|------|----------|---------|-------------|
| Validate On Write | `validateOnWrite` | `boolean` | No | `true` | Validate each `WorkItemRevision` against the package schema as it is written. Errors are reported to `Logs/simulated-import-validation.jsonl`. |
| Fail On First Error | `failOnFirstError` | `boolean` | No | `true` | Immediately fail the import when the first schema validation error is detected. When `false`, all errors are collected and reported at the end. Only meaningful when `validateOnWrite: true`. |

---

## C# Options Class

```csharp
/// <summary>
/// Configuration options for the Simulated work item import target.
/// Bound from the <c>"target"</c> config section when <c>target.type == "Simulated"</c>.
/// </summary>
public sealed class SimulatedTargetOptions
{
    public static string SectionName => "target";

    public bool ValidateOnWrite { get; init; } = true;
    public bool FailOnFirstError { get; init; } = true;
}
```

---

## Behaviour Guarantees

1. **No external writes**: `SimulatedWorkItemImportSink` never calls any external API, writes to any database, or touches any file outside the package boundary (`IArtefactStore`).
2. **Schema validation**: When `ValidateOnWrite = true`, each `WorkItemRevision` is validated for:
   - Presence of all required fields (`workItemId`, `revisionIndex`, `changedDate`, `fields`)
   - `changedDate` is a valid ISO 8601 UTC timestamp
   - `attachments` entries have valid `sha256` format (64-char hex string)
3. **Progress events**: Emits `ProgressEvent` records through `IProgressSink` at the same granularity as the real ADO import target (per-revision), allowing the TUI to display live import progress.
4. **Counts**: Tracks total revisions accepted and total items seen. These are emitted in a final progress event on `CompleteAsync`.
5. **Log output**: Writes `Logs/simulated-import-summary.jsonl` with final counts and any validation errors encountered.

---

## Error Handling

| Condition | Behaviour when `failOnFirstError = true` | Behaviour when `failOnFirstError = false` |
|-----------|------------------------------------------|------------------------------------------|
| Missing required field in revision | Throws `SimulatedImportValidationException`; import halted | Appends error to list; continues |
| Invalid `changedDate` format | Throws `SimulatedImportValidationException`; import halted | Appends error to list; continues |
| Invalid attachment `sha256` format | Throws `SimulatedImportValidationException`; import halted | Appends error to list; continues |

On `CompleteAsync`, if errors were collected (`failOnFirstError = false`), a `SimulatedImportValidationException` is thrown summarising all errors, causing the job to fail with a structured error report.

---

## Interaction with Other Source Types

The simulated target accepts packages produced by **any** valid source type — not just `source.type: Simulated`. This is by design: operators can use the simulated target to validate a real ADO export package without writing to a real ADO target.

When `target.type: Simulated` is used without `source.type: Simulated`, the platform proceeds normally. The simulated target does not restrict the source type.

---

## Example: Simulated Import Only (real export package, simulated target)

```json
{
  "configVersion": "1.0",
  "mode": "Import",
  "artefacts": {
    "path": "/exports/real-ado-export"
  },
  "target": {
    "type": "Simulated",
    "validateOnWrite": true,
    "failOnFirstError": false
  },
  "modules": [
    {
      "name": "WorkItems",
      "scopes": [{ "type": "all", "parameters": {} }]
    }
  ]
}
```
