---
name: nkda-observability-contract
description: Inspects, validates, and amends a feature specification to ensure complete, decision-driven observability coverage across metrics, traces, and logs. Fails if minimum standards cannot be met.
---

# Skill: Observability Contract Enforcement

Enforce a complete, decision-driven observability contract on specifications, design documents, or implemented code. This skill is deterministic and idempotent — running it twice on the same input produces the same output.

**Invocation modes:**

| Mode | How to invoke | Input | Output |
|---|---|---|---|
| **Document** | Run against a markdown file (spec, plan, design doc) | File path or currently open file | Injects/rewrites `## Observability` section in the document |
| **Codebase** | Run against a project, folder, or feature area | Project path, folder path, namespace, or feature name | Produces an Observability Audit Report as a standalone markdown section or file |
| **SpecKit hook** | Automatic via `after_specify` in `.specify/extensions.yml` | The spec being generated | Same as Document mode |

When invoked manually, pass the target file or folder path. If no path is given, use the currently open file or its containing project. The skill auto-detects the mode:

1. If the target is a `.md` file → **Document mode**.
2. If the target is a `.cs` file, `.csproj`, folder, or namespace → **Codebase mode**.
3. If invoked by SpecKit hook → **Document mode** against the spec.

---

## Argument Parsing

The command MUST parse `--stage` before any other step.

**Valid values:** `spec` | `plan` | `tasks` | `implement`

- If `--stage` is present and valid: execute **Stage Precedence Rule** below. Do NOT execute Step 0.
- If `--stage` is absent: fall through to Step 0 (auto-detection).
- If `--stage` is present but invalid: STOP and emit:

  ```
  OBSERVABILITY CONTRACT FAILURE: Invalid --stage value "<value>".
  Expected one of: spec, plan, tasks, implement
  ```

---

## Stage Precedence Rule

When `--stage` is provided it is the **single source of truth**. It overrides all auto-detection.

- DO NOT execute Step 0.
- DO NOT infer the target from file type or current editor context.
- Resolve **target** and **enforcement behaviour** exclusively from the Stage Resolution table below.

---

## Stage Resolution (Authoritative)

| Stage | Target (resolved from current feature dir) | Enforcement Focus | Required Action |
|---|---|---|---|
| `spec` | `<feature-dir>/spec.md` | `## Observability` section: operations, decisions, metrics, traces, logging, correlation, validation queries | Amend `spec.md` directly — inject/rewrite the section. Do not stop at listing gaps. |
| `plan` | `<feature-dir>/plan.md` | Observability Contract section: O-1 spans, O-2 metrics, O-3 logs, O-4 progress events, CLI visibility per operation | Amend `plan.md` directly — inject/rewrite the section. Do not stop at listing gaps. |
| `tasks` | `<feature-dir>/tasks.md` | Every user story phase contains explicit tasks for O-1 `ActivitySource.StartActivity`, O-2 `IMigrationMetrics`, O-3 structured `ILogger`, O-4 `IProgressSink.EmitAsync` | Amend `tasks.md` directly — insert missing observability tasks into each phase. Block until gap-free. |
| `implement` | All `.cs` files created or modified in the current execution (see below) | O-1 span on every operation method; O-2 metrics at every boundary; O-3 structured logging (Info start/end, Warn skips/errors, Debug per-item); O-4 EmitAsync at start, per ≤50-item batch, and completion | Write missing instrumentation directly into source files. Produce gap table, fix every row to ✅. |

If the resolved target file does not exist, STOP and emit:

```
OBSERVABILITY CONTRACT FAILURE: Target file not found: <path>.
Ensure the target artefact exists before running this stage.
```

### Stage `implement` — target resolution

The target is, in priority order:

1. All `.cs` files explicitly passed as arguments.
2. All `.cs` files created or modified in the current SpecKit implementation session (from the task execution log).
3. If neither is available: all `.cs` files in the current feature namespace or project folder that contain a class implementing `IModule`, `IJob`, `ICommandHandler`, or a service/tool class.

Do not scan the entire solution. Scope is the feature under implementation.

**Resolve, do not just report.** For every stage:
- Document stages (`spec`, `plan`, `tasks`): **write the missing content into the target file**. Produce the gap table, then immediately fix every gap.
- Codebase stage (`implement`): **write the missing instrumentation code** into the correct source files. Produce the gap table, then immediately implement every fix.

---

## Execution Guard

Before performing any stage action:

1. Confirm the resolved target file or file set exists (see Stage Resolution above).
2. Attempt to extract at least one operation from the target (Step 2 below).

If either check fails: STOP execution and emit the appropriate failure message. Do not attempt inference or fallback.

---

## Role

When this skill is active, inspect the target for observability completeness and **fail explicitly** if minimum standards cannot be satisfied.

- **Document mode:** Inject or rewrite the `## Observability` section to meet the mandatory contract. Modifies the document directly.
- **Codebase mode:** Scan the actual source code for existing instrumentation, compare it against the mandatory contract, and produce an audit report listing gaps, violations, and required changes. Does not modify source code automatically — reports findings with specific file locations and fix instructions.

---

## Preconditions

Before executing, read the following context files:

- `.agents/30-context/domains/telemetry-model.md` — Three-layer model, metric naming, span inventory
- `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMetricNames.cs` — Existing metric names
- `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownActivitySourceNames.cs` — Existing ActivitySource names
- `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMeterNames.cs` — Existing meter names

These files establish the naming conventions and existing instrumentation. New metrics and spans defined by this skill MUST be consistent with them.

---

## Execution Steps

Execute the following steps in order. Do not skip steps. Do not proceed past a FAIL gate.

**If `--stage` was provided, begin at Step 1 using the target resolved by the Stage Resolution table. Skip Step 0 entirely.**

### Step 0 — Detect Mode (only when `--stage` is absent)

1. Examine the target path or context.
2. If the target is a `.md` file, or the skill was invoked by a SpecKit hook → enter **Document mode**.
3. If the target is a `.cs` file, `.csproj` file, folder, or the user named a namespace/feature → enter **Codebase mode**.
4. Record the mode. All subsequent steps are executed in both modes unless marked otherwise.

### Step 1 — Locate or Create Observability Section

**Document mode:**

1. Search the document for a heading `## Observability` (case-insensitive).
2. If it does not exist, insert an empty `## Observability` section immediately before `## Requirements` (or at the end if `## Requirements` is absent).
3. If it exists, read its full content for evaluation in subsequent steps.

**Codebase mode:**

1. Prepare an empty audit report structure. No document is modified in this step.
2. Identify the scope: if a single file, scope to that file's class/namespace; if a folder or project, scope to all `.cs` files within.

### Step 2 — Extract Operations

**Document mode:** Scan the entire document and extract every operation that the feature introduces or modifies.

**Codebase mode:** Scan the source code within scope and extract operations by identifying:

- Classes implementing `IModule`, `IJob`, `ICommandHandler`, or similar platform interfaces
- Controller actions or endpoint mappings
- Methods decorated with `[Command]`, `[HttpGet]`, `[HttpPost]`, etc.
- Public async methods that represent workflow steps (e.g. `ExecuteAsync`, `RunAsync`, `ProcessAsync`)
- Message/event handlers (classes implementing `IHandler<T>`, `IConsumer<T>`, etc.)
- Background services (`BackgroundService`, `IHostedService`)

An operation is any of:

- API endpoint (REST, gRPC, SignalR)
- CLI command or subcommand
- Background job or scheduled task
- Module entry point (export, import, inventory, discovery)
- Workflow or orchestration step
- Event handler or message consumer

For each operation, record:

| Field | Value |
|---|---|
| **Name** | A short, unambiguous identifier (e.g. `workitems.export`, `job.enqueue`) |
| **Type** | One of: `api`, `command`, `job`, `module`, `workflow`, `handler` |
| **Entry point** | The class or method that initiates the operation (from spec or actual source file + line) |
| **Dependencies** | External calls, store operations, SDK calls the operation makes |

**Codebase mode — additional extraction:**

For each operation found in code, also record:

| Field | Value |
|---|---|
| **File** | Source file path |
| **Has ActivitySource?** | Whether the class creates or uses an `ActivitySource` / `Activity` |
| **Has Metrics?** | Whether the class injects or uses `IMigrationMetrics`, `IDiscoveryMetrics`, `Counter`, `Histogram`, etc. |
| **Has Structured Logging?** | Whether the class uses `ILogger` with structured templates (not string concatenation) |
| **Has Correlation?** | Whether `Activity.Current`, `traceId`, or `operationId` is propagated |

**FAIL GATE:** If zero operations can be extracted or inferred, emit the following and stop:

```
OBSERVABILITY CONTRACT FAILURE: No operations could be determined from the input.
The target must contain at least one concrete operation (API, command, job, module, workflow, or handler)
before observability can be assessed. Amend the spec or verify the correct code scope.
```

### Step 3 — Define Operator Decisions per Operation

For each operation, define the decisions an operator must be able to make using telemetry. Every signal (metric, span, log) must map to at least one decision.

Standard decision categories:

| Decision | Question it answers |
|---|---|
| **Is it working?** | Are requests succeeding at an acceptable rate? |
| **Is it fast enough?** | Is latency within SLO bounds? |
| **Is it overloaded?** | Is concurrency or queue depth exceeding capacity? |
| **What failed?** | Which specific operation failed and why? |
| **Where is it slow?** | Which dependency or step is the bottleneck? |
| **Is it correct?** | Do output counts match input counts? Are invariants maintained? |

For each operation, assign at minimum: `Is it working?`, `Is it fast enough?`, `Is it overloaded?`, and `What failed?`.

### Step 4 — Define Metrics (OpenTelemetry aligned)

For each operation, define the mandatory metrics. Use the naming convention:

```
<domain>.<capability>.<operation>.<measure>
```

Where:
- `<domain>` = `migration` or `discovery` (matching `WellKnownMeterNames`)
- `<capability>` = module or subsystem name (e.g. `export`, `import`, `inventory`)
- `<operation>` = specific action (e.g. `workitem`, `attachment`, `revision`)
- `<measure>` = what is measured (e.g. `count`, `duration_ms`, `errors`, `in_flight`)

**Mandatory metrics per operation:**

| Metric | Instrument | Unit | Decision served |
|---|---|---|---|
| Throughput | `Counter<long>` | `{operation}` | Is it working? |
| Latency | `Histogram<double>` | `ms` | Is it fast enough? |
| Outcome (success) | `Counter<long>` | `{operation}` | Is it working? |
| Outcome (failure) | `Counter<long>` | `{operation}` | What failed? |
| In-flight / queue depth | `UpDownCounter<long>` or `ObservableGauge<int>` | `{operation}` | Is it overloaded? |

**Validation rules:**

- Every metric name MUST follow the four-segment naming convention.
- Every metric MUST map to at least one decision from Step 3.
- Metrics that do not support any operator decision MUST be rejected and removed.
- Metrics MUST represent business activity. Infrastructure-level metrics (CPU, memory, GC) are out of scope for the spec — they are provided by the runtime.
- If the operation is a batch, add a batch-size `Histogram` metric.
- Check existing `WellKnownMetricNames` — reuse existing names where the semantics match; do not invent duplicates.

**Document mode:** Write the metrics table into the Observability section under `### Metrics`.

**Codebase mode:** For each required metric, check whether it already exists in the code:

1. Search the scoped files for `Counter<`, `Histogram<`, `UpDownCounter<`, `ObservableGauge<` instrument creation.
2. Search `WellKnownMetricNames` and `WellKnownDiscoveryMetricNames` for matching constant names.
3. For each mandatory metric: mark as **Present** (with file + line), **Partial** (instrument exists but wrong type/name), or **Missing**.
4. Record findings in the audit report.

### Step 5 — Define Traces (end-to-end)

For each operation, define the span hierarchy. Every operation MUST have a root span. Every dependency call MUST be a child span.

**Span table format:**

| Component | Span Name | Tags | Parent | Decision served |
|---|---|---|---|---|
| `<class>` | `<operation.name>` | `<tag list>` | Root or `<parent span>` | `<decision>` |

**Validation rules:**

- Every operation MUST have exactly one root span.
- Every dependency identified in Step 2 MUST appear as a child span.
- Context propagation method MUST be stated (automatic via `Activity` hierarchy, or explicit `W3C TraceContext` header).
- Span names MUST use lowercase dot-separated segments matching the metric domain.
- Check the existing span inventory in `telemetry-model.md` — reuse existing spans where semantics match.
- Tags MUST include: `job.id`, `operation`, `module` at minimum for root spans.
- Child spans MUST include the entity identifier (e.g. `wi.id`, `attachment.name`).

**FAIL GATE:** If any operation has no root span defined, or any dependency has no child span, emit:

```
OBSERVABILITY CONTRACT FAILURE: Trace coverage is incomplete.
Operation '<name>' is missing a root span, or dependency '<dep>' has no child span.
Every operation must have end-to-end trace coverage.
```

**Document mode:** Write the span table into the Observability section under `### Traces`.

**Codebase mode:** For each required span, check whether it already exists:

1. Search for `ActivitySource` field declarations and `StartActivity` calls in the scoped files.
2. Match span names to the required span table.
3. Verify child spans exist for each identified dependency.
4. Verify tags include required fields (`job.id`, `operation`, `module`, entity identifiers).
5. Mark each span as **Present**, **Partial** (exists but missing tags or children), or **Missing**.
6. Record findings in the audit report.

### Step 6 — Define Structured Logging

For each operation, define the mandatory log events. Logs are structured (key-value pairs), not free-text.

**Mandatory log events per operation:**

| Event | Level | Fields | Decision served |
|---|---|---|---|
| Operation started | `Information` | operationId, operation, input summary | Is it working? |
| Operation completed | `Information` | operationId, operation, outcome, durationMs, output summary | Is it working? / Is it fast enough? |
| Operation failed | `Error` | operationId, operation, errorType, errorMessage, durationMs | What failed? |
| Dependency call slow | `Warning` | operationId, dependency, durationMs, threshold | Where is it slow? |
| Retry attempt | `Warning` | operationId, operation, attempt, maxAttempts, delay | Is it overloaded? |
| Step detail | `Debug` | operationId, step, detail | (diagnostic only) |
| Wire-level detail | `Trace` | operationId, payload summary | (diagnostic only) |

**Validation rules:**

- `Debug` and `Trace` level logs MUST be declared as optional and disabled by default.
- Log fields MUST NOT contain raw customer data (field values, project names, org URLs, attachment paths) without `DataClassification.Customer` scope — per project guardrails.
- Work item IDs are integer identifiers and are NOT customer data.
- Every log event MUST include `operationId` for correlation.
- No unstructured string concatenation in log templates.

**Document mode:** Write the log events into the Observability section under `### Logging`.

**Codebase mode:** For each required log event, check whether it already exists:

1. Search for `ILogger` usage, `LogInformation`, `LogWarning`, `LogError`, `LogDebug`, `LogTrace` calls.
2. Verify structured templates (message templates with `{Parameter}` placeholders, not string concatenation or interpolation).
3. Verify each operation has start, completion, and error log events.
4. Check that log fields do not contain raw customer data without `DataClassification.Customer` scope.
5. Mark each log event as **Present**, **Partial** (exists but unstructured or missing fields), or **Missing**.
6. Record findings in the audit report.

### Step 7 — Define Correlation Model

Define the correlation identifiers that MUST be present on all telemetry (metrics tags, span tags, log fields) for this feature.

**Mandatory correlation fields:**

| Field | Source | Scope |
|---|---|---|
| `operationId` / `traceId` | `Activity.Current.TraceId` or generated GUID | All telemetry |
| `parentId` | `Activity.Current.ParentSpanId` | Spans and logs within a parent context |
| `job.id` | Job context | All telemetry within a job |
| Domain identifiers | Feature-specific (e.g. `wi.id`, `project.name`, `org.url`) | Where applicable |

**FAIL GATE:** If correlation cannot be established (e.g. no job context, no activity source), emit:

```
OBSERVABILITY CONTRACT FAILURE: Correlation model is missing.
Feature '<name>' does not define how telemetry will be correlated across metrics, traces, and logs.
Define the correlation identifiers and their sources.
```

**Document mode:** Write the correlation model into the Observability section under `### Correlation`.

**Codebase mode:** Verify correlation in the actual code:

1. Check that `Activity.Current` is used or `ActivitySource.StartActivity` establishes trace context.
2. Check that `ILogger` scopes or log templates include `operationId` / `traceId`.
3. Check that metric tag lists include `job.id` and domain identifiers.
4. Mark correlation as **Present**, **Partial**, or **Missing** per operation.
5. Record findings in the audit report.

### Step 8 — Generate Validation Queries

For each operator decision defined in Step 3, generate a KQL-style validation query that proves the decision can be answered using the defined signals.

**Required query categories:**

| Category | Proves | Source |
|---|---|---|
| Failure identification | Failures can be identified by operation and cause | Metrics (outcome.failure) + Logs (Error) |
| Latency analysis | P50/P95/P99 latency can be computed per operation | Metrics (latency histogram) |
| Load observation | In-flight concurrency or queue depth is visible | Metrics (in_flight / queue_depth) |
| End-to-end trace | A single request can be traced from entry to all dependencies | Traces (root + child spans) |
| Error diagnosis | Root cause can be determined from logs + traces | Logs (Error) joined with Traces |

**Query format:**

```kql
// <Category>: <What it proves>
<table>
| where <filter>
| summarize <aggregation> by <dimensions>
```

**FAIL GATE:** If any required query category cannot be expressed using the defined metrics, traces, and logs, the observability contract is incomplete. Emit:

```
OBSERVABILITY CONTRACT FAILURE: Validation query '<category>' cannot be derived.
The defined signals do not support answering: '<decision question>'.
Add the missing metric, span, or log event to close the gap.
```

**Document mode:** Write the queries into the Observability section under `### Validation Queries`.

**Codebase mode:** Generate the same queries, but annotate each with whether the required signals are **Present** or **Missing** in the actual code. If a query cannot be satisfied because the underlying instrument or span does not exist in code, flag it as a gap.

### Step 9 — Idempotency Check

Before writing the final Observability section:

1. Compare the generated content with any existing `## Observability` section.
2. If the existing section already satisfies all validation rules, do not modify it.
3. If the existing section is partially correct, preserve correct structures and amend only the gaps.
4. Do not duplicate metrics, spans, or log events that already exist and are correct.
5. Do not reorder existing content unless required for correctness.

### Step 10 — Write Output

**Document mode:** Replace or insert the `## Observability` section with the validated content.

**Codebase mode:** Produce the **Observability Audit Report** (see Codebase Output Format below). Do not modify source files automatically. The report is the deliverable.

#### Document Output Structure

The `## Observability` section MUST follow this structure:

```markdown
## Observability

### Operations

| Name | Type | Entry Point | Dependencies |
|---|---|---|---|
| ... | ... | ... | ... |

### Operator Decisions

| Operation | Decision | Question |
|---|---|---|
| ... | ... | ... |

### Metrics

| Metric Name | Instrument | Unit | Operation | Decision |
|---|---|---|---|---|
| ... | ... | ... | ... | ... |

### Traces

| Component | Span Name | Tags | Parent | Decision |
|---|---|---|---|---|
| ... | ... | ... | ... | ... |

**Context propagation:** <method>

### Logging

| Event | Level | Fields | Operation | Decision |
|---|---|---|---|---|
| ... | ... | ... | ... | ... |

> Debug and Trace levels are disabled by default.

### Correlation

| Field | Source | Scope |
|---|---|---|
| ... | ... | ... |

### Validation Queries

#### Failure Identification
\```kql
<query>
\```

#### Latency Analysis
\```kql
<query>
\```

#### Load Observation
\```kql
<query>
\```

#### End-to-End Trace
\```kql
<query>
\```

#### Error Diagnosis
\```kql
<query>
\```
```

#### Codebase Output Structure

When in Codebase mode, produce the audit report in the following format:

```markdown
## Observability Audit Report

**Scope:** `<project, folder, or file path>`
**Date:** `<date>`
**Operations found:** `<count>`

### Operations Discovered

| Name | Type | Entry Point | File | Has Spans | Has Metrics | Has Logging | Has Correlation |
|---|---|---|---|---|---|---|---|
| ... | ... | ... | ... | ✅/❌/⚠️ | ✅/❌/⚠️ | ✅/❌/⚠️ | ✅/❌/⚠️ |

Legend: ✅ = Present and correct, ⚠️ = Partial (exists but incomplete), ❌ = Missing

### Gaps — Metrics

| Operation | Required Metric | Status | Detail |
|---|---|---|---|
| ... | ... | Missing/Partial | <what is wrong or missing, file + line if partial> |

### Gaps — Traces

| Operation | Required Span | Status | Detail |
|---|---|---|---|
| ... | ... | Missing/Partial | <what is wrong or missing> |

### Gaps — Logging

| Operation | Required Event | Status | Detail |
|---|---|---|---|
| ... | ... | Missing/Partial | <what is wrong or missing> |

### Gaps — Correlation

| Operation | Required Field | Status | Detail |
|---|---|---|---|
| ... | ... | Missing/Partial | <what is wrong or missing> |

### Required Changes

Prioritised list of changes needed to reach full observability coverage:

1. **[CRITICAL]** `<file>` — `<what to add/fix>` (closes gap: `<which metric/span/log>`)
2. **[HIGH]** ...
3. **[MEDIUM]** ...

### Validation Queries

<same query format as Document mode, annotated with signal availability>

### Verdict

**PASS** — All operations have complete observability coverage.
— or —
**FAIL** — `<N>` operations have incomplete coverage. See Required Changes above.
```

If all operations pass, the verdict is PASS. If any operation has a Missing metric, span, or correlation field, the verdict is FAIL.

---

## Enforcement Summary

| Condition | Action |
|---|---|
| No `## Observability` section | Create it |
| Section exists but incomplete | Amend missing parts |
| Section exists and complete | No modification (idempotent) |
| Zero operations extractable | **FAIL** — spec must define operations |
| Missing root span for any operation | **FAIL** — trace coverage incomplete |
| Missing correlation model | **FAIL** — correlation is mandatory |
| Validation query cannot be derived | **FAIL** — signals are insufficient |
| Metric without a mapped decision | **REJECT** — remove the metric |
| Log with raw customer data and no classification scope | **REJECT** — redact or classify |
| Duplicate of existing correct structure | **SKIP** — do not duplicate |

---

## Completion Criteria

### Both modes

- [ ] All operations extracted or inferred from input.
- [ ] Every operation has at least four operator decisions assigned.
- [ ] Every operation has all five mandatory metric types defined.
- [ ] Every metric follows `<domain>.<capability>.<operation>.<measure>` naming.
- [ ] Every metric maps to at least one operator decision.
- [ ] Every operation has a root span defined.
- [ ] Every dependency has a child span defined.
- [ ] Context propagation is stated.
- [ ] Every operation has start, completion, and error log events.
- [ ] Debug/Trace logs are marked optional and disabled by default.
- [ ] No raw customer data in log fields without `DataClassification.Customer`.
- [ ] Correlation model defines operationId, parentId, job.id, and domain identifiers.
- [ ] All five validation query categories are present and derivable.

### Document mode only

- [ ] Observability section follows the mandatory structure.
- [ ] Existing correct content is preserved (idempotent).
- [ ] No TODO placeholders remain in the Observability section.

### Codebase mode only

- [ ] Every operation has been checked for existing instrumentation.
- [ ] Every gap is marked with status (Missing/Partial) and file location.
- [ ] Required Changes list is prioritised (Critical > High > Medium).
- [ ] Verdict is explicitly stated (PASS or FAIL).
- [ ] No source files were modified without explicit user instruction.
- [ ] Observability section follows the mandatory structure.
- [ ] Existing correct content is preserved (idempotent).
- [ ] No TODO placeholders remain in the Observability section.

The skill is not complete until all criteria are checked. Any unchecked criterion is a failure.

