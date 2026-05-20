# Blocking Errors

Errors that must halt execution immediately. Do not catch and continue. Do not silently swallow. Do not log-only.

When a blocking error occurs, `JobPlanExecutor` writes `errors.json` to the package root automatically. No module-level handler is needed or permitted.

---

## Mandatory Behaviour

- **Let the exception propagate** from the module or orchestrator.
- `JobPlanExecutor.ExecuteTierAsync` catches every non-cancellation exception, writes `errors.json`, marks the task `Failed`, and stops that task.
- The task `Failed` status cascades to dependent tasks via the plan dependency graph.
- `errors.json` format:

```json
{
  "generatedAt": "<ISO-8601 UTC>",
  "taskId": "<task-id>",
  "errors": [
    {
      "phase": "<task-kind e.g. Import>",
      "module": "<handler-name e.g. WorkItems>",
      "message": "<exception.Message>",
      "exceptionType": "<exception.GetType().Name>"
    }
  ]
}
```

---

## Prohibited Patterns

| Pattern | Verdict |
|---|---|
| `catch (SomeException) { return; }` for a blocking condition | **REJECT** — silently hides a stopping error |
| `catch (SomeException) { _logger.LogWarning(...); return; }` for a blocking condition | **REJECT** — log-only is not a user-visible artefact |
| Module/orchestrator writing its own `errors.json` | **REJECT** — `JobPlanExecutor` owns this; duplication creates race conditions |
| Re-throwing a blocking exception after writing `errors.json` in a module | **REJECT** — same as above |

---

## Known Blocking Error Catalogue

| Code / Signal | Source | Description | Required Behaviour |
|---|---|---|---|
| `TF51005` (`VssServiceException`) | Azure DevOps WIT client | Field referenced in WIQL query does not exist in the target project. If the provenance field is missing, no work items can be written with that field either — the entire import is invalid. | Let the exception propagate. Do not catch. |
| `VS30063` (`VssUnauthorizedException`) | Azure DevOps VSSPS | Authentication failed. | Let the exception propagate. Do not catch. |

---

## Adding New Entries

When a new blocking error is identified and handled (or deliberately not caught):

1. Add a row to the catalogue table above.
2. Ensure the code **does not** have a catch block for that error code.
3. The guardrail file is the canonical source of record for why we chose to let it propagate.
