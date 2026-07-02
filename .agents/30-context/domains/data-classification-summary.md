# Data Classification Summary

Short reference for agents. See `docs/security-and-data-sovereignty.md` for the full guide.

## Classification Categories

| Category | Examples | Application Insights safe? | Package safe? |
|---|---|---|---|
| Customer | Field values, project names, org URLs, display names, attachment paths | **No** | Yes |
| System | Work item IDs (integers), job IDs, module names, metric names | Yes | Yes |
| Derived | Counts, durations, error rates, percentages | Yes | Yes |

## Rules

1. Work item IDs are integers — they are System data, not Customer data.
2. Log statements that include field values, project names, org URLs, or attachment paths must use `DataClassification.Customer(value)` scope.
3. Activity span tags that contain Customer data must not be forwarded to Application Insights.
4. The Control Plane must not receive Customer data in telemetry exports.

## Usage

```csharp
_logger.LogInformation("Exporting project {Project}", DataClassification.Customer(projectName));
_logger.LogDebug("Processing work item {WorkItemId}", workItemId);  // integer, System data
```

## Related

- [data-sovereignty-rules.md](../../20-guardrails/domains/data-sovereignty-rules.md)
- [observability-requirements.md](../../20-guardrails/domains/observability-requirements.md)



