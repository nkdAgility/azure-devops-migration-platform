# Contract: Simulated Source Configuration

**Version**: 1.0  
**Applies to**: `source.type == "Simulated"`  
**Location in codebase**: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Options/SimulatedSourceOptions.cs`

---

## JSON Configuration Schema

When `source.type` is `"Simulated"`, the following fields are accepted under the `source` block. No `orgOrCollection`, `project`, `apiVersion`, or `authentication` fields are required.

```json
{
  "source": {
    "type": "Simulated",
    "seed": 42,
    "workItemCount": 25000,
    "projectCount": 1,
    "workItemTypeDistribution": {
      "Bug": 40,
      "Task": 40,
      "Feature": 20
    },
    "avgRevisionsPerItem": 3,
    "includeAttachments": false,
    "includeLinks": true,
    "attachmentSizeBytes": 4096
  }
}
```

---

## Field Reference

| Field | JSON key | Type | Required | Default | Constraints | Description |
|-------|----------|------|----------|---------|-------------|-------------|
| Seed | `seed` | `integer` | No | *(auto)* | None | Determinism seed. If omitted, auto-generated and logged/recorded in manifest. |
| Work Item Count | `workItemCount` | `integer` | **Yes** | — | `≥ 1` | Total work items to generate. |
| Project Count | `projectCount` | `integer` | No | `1` | `≥ 1` | Number of simulated projects. Items distributed evenly. |
| Type Distribution | `workItemTypeDistribution` | `object` | No | `{"Bug":40,"Task":40,"Feature":20}` | Values sum to 100 | Percentage of each work item type. |
| Avg Revisions | `avgRevisionsPerItem` | `integer` | No | `3` | `1–100` | Mean revisions per work item. Actual count varies ±50% (min 1). |
| Include Attachments | `includeAttachments` | `boolean` | No | `false` | — | Generate and write attachment binaries to package. |
| Include Links | `includeLinks` | `boolean` | No | `true` | — | Generate `relatedLinks`, `externalLinks`, and `hyperlinks`. |
| Attachment Size | `attachmentSizeBytes` | `integer` | No | `4096` | `1–104857600` | Size of each generated binary in bytes. Only used when `includeAttachments: true`. |

---

## C# Options Class

```csharp
/// <summary>
/// Configuration options for the Simulated work item source.
/// Bound from the <c>"source"</c> config section when <c>source.type == "Simulated"</c>.
/// </summary>
public sealed class SimulatedSourceOptions
{
    public static string SectionName => "source";

    /// <summary>Optional determinism seed. Null triggers auto-generation.</summary>
    public int? Seed { get; init; }

    [Required]
    [Range(1, int.MaxValue)]
    public int WorkItemCount { get; init; }

    [Range(1, int.MaxValue)]
    public int ProjectCount { get; init; } = 1;

    public Dictionary<string, int>? WorkItemTypeDistribution { get; init; }

    [Range(1, 100)]
    public int AvgRevisionsPerItem { get; init; } = 3;

    public bool IncludeAttachments { get; init; } = false;
    public bool IncludeLinks { get; init; } = true;

    [Range(1, 104_857_600)]
    public int AttachmentSizeBytes { get; init; } = 4096;
}
```

---

## Validation Errors

| Condition | Error message |
|-----------|--------------|
| `workItemCount` missing or ≤ 0 | `"source.workItemCount is required and must be ≥ 1 for Simulated source."` |
| `workItemTypeDistribution` values do not sum to 100 | `"source.workItemTypeDistribution values must sum to exactly 100. Current sum: {N}."` |
| `avgRevisionsPerItem` < 1 or > 100 | `"source.avgRevisionsPerItem must be between 1 and 100."` |
| `attachmentSizeBytes` > 104857600 | `"source.attachmentSizeBytes must not exceed 100 MB."` |

---

## Interaction with `configHash`

All fields in `SimulatedSourceOptions` are included in the config serialisation before the `configHash` is computed. If any field changes between runs using the same package directory, the `configHash` in `manifest.json` will not match and the platform will reject the resume, requiring a fresh run.

---

## Example Minimal Config (25k items, default options)

```json
{
  "configVersion": "1.0",
  "mode": "Both",
  "artefacts": {
    "path": "${workspaceFolder}/Logs/SimulatedRun"
  },
  "source": {
    "type": "Simulated",
    "workItemCount": 25000
  },
  "target": {
    "type": "Simulated"
  },
  "modules": [
    {
      "name": "WorkItems",
      "scopes": [{ "type": "all", "parameters": {} }]
    }
  ]
}
```

## Example Reproducible Config (fixed seed)

```json
{
  "configVersion": "1.0",
  "mode": "Export",
  "artefacts": {
    "path": "${workspaceFolder}/Logs/SimulatedExport"
  },
  "source": {
    "type": "Simulated",
    "seed": 42,
    "workItemCount": 25000,
    "projectCount": 2,
    "workItemTypeDistribution": {
      "Bug": 50,
      "Task": 30,
      "UserStory": 20
    },
    "avgRevisionsPerItem": 5,
    "includeAttachments": true,
    "attachmentSizeBytes": 2048
  },
  "modules": [
    {
      "name": "WorkItems",
      "scopes": [{ "type": "all", "parameters": {} }]
    }
  ]
}
```
