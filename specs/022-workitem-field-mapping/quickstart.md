# Quickstart: Work Item Field Transformation

**Feature**: 022-workitem-field-mapping

## What This Feature Does

The FieldTransformTool applies a configurable pipeline of field transformations to work item revisions during migration. It supports 14 transform types — from simple field copies and value remapping to regex cleanup and computed fields.

## Minimal Configuration

Add this to your migration config JSON to remap Agile states to Scrum states:

```json
{
  "MigrationPlatform": {
    "Tools": {
      "FieldTransform": {
        "transformGroups": [
          {
            "name": "state-remap",
            "transforms": [
              {
                "type": "MapValue",
                "field": "System.State",
                "valueMap": {
                  "Active": "In Progress",
                  "Resolved": "Done",
                  "New": "To Do"
                }
              }
            ]
          }
        ]
      }
    },
    "Modules": {
      "WorkItems": {
        "Extensions": {
          "Revisions": {
            "tools": {
              "FieldTransform": {
                "phase": "import"
              }
            }
          }
        }
      }
    }
  }
}
```

## Key Concepts

1. **Transform groups** — Ordered array. Each group can target specific work item types via `applyTo`.
2. **Transforms** — Individual operations within a group. Each has a `type` discriminator.
3. **Pipeline order** — Groups execute top-to-bottom, transforms within each group top-to-bottom. Each transform sees the output of the previous one.
4. **Phase** — Transforms run during `import` by default. Set `phase: "export"` to transform before writing to the package.
5. **Validation** — Run `prepare` before migration to validate all field references and types against source/target.

## Available Transform Types

| Type | What It Does |
|---|---|
| `CopyField` | Copy one field to another |
| `CopyFieldBatch` | Copy multiple fields in one declaration |
| `SetField` | Set a field to a literal value |
| `MapValue` | Remap values via lookup table |
| `MergeFields` | Merge N fields using a format template |
| `CalculateField` | Compute value from arithmetic/string expression |
| `ClearField` | Set a field to null |
| `ExcludeField` | Remove field from revision entirely |
| `ConditionalTag` | Add tag when field matches a pattern |
| `FieldToTag` | Copy field value as a tag |
| `MergeToTag` | Merge multiple fields into tags |
| `ConditionalField` | Set field conditionally based on other fields |
| `RegexField` | Regex find-and-replace on a field value |
| `TreeToTag` | Flatten tree path into tags |

## Enabling/Disabling

`enabled` is supported at three levels (omitting defaults to `true`):

```json
{
  "Tools": {
    "FieldTransform": {
      "enabled": true,                    // Tool level
      "transformGroups": [
        {
          "enabled": true,                // Group level
          "transforms": [
            { "enabled": false, ... }     // Transform level
          ]
        }
      ]
    }
  }
}
```

## What You Cannot Transform

- **Identity fields** (`System.AssignedTo`, `System.CreatedBy`, `System.ChangedBy`) — handled by `IIdentityMappingService`.
- **Area/Iteration paths** — handled by `NodeStructureTool` (T2, separate feature).
- **Work item types** — handled by `WorkItemTypeMappingTool` (T3, separate feature).
