---
name: connector-coverage-check
description: Inspects, validates, and amends a feature specification, plan, or codebase to ensure full implementation coverage across all three connectors (Simulated, AzureDevOpsServices, TeamFoundationServer). Fails if any connector is left with stubs or placeholders where the API supports the capability.
---

# Skill: Connector Coverage Check

Enforce full connector coverage on specifications, design documents, plans, task lists, or implemented code. This skill is deterministic and idempotent — running it twice on the same input produces the same output.

**Invocation modes:**

| Mode | How to invoke | Input | Output |
|---|---|---|---|
| **Document** | Run against a markdown file (spec, plan, tasks) | File path or currently open file | Injects/rewrites `## Connector Coverage` section in the document |
| **Codebase** | Run against a project, folder, or feature area | Project path, folder path, namespace, or feature name | Produces a Connector Coverage Audit Report |
| **SpecKit hook** | Automatic via `after_specify` in `.specify/extensions.yml` | The spec being generated | Same as Document mode |

When invoked manually, pass the target file or folder path. If no path is given, use the currently open file or its containing project. The skill auto-detects the mode:

1. If the target is a `.md` file → **Document mode**.
2. If the target is a `.cs` file, `.csproj`, folder, or namespace → **Codebase mode**.
3. If invoked by SpecKit hook → **Document mode** against the spec.

---

## Role

When this skill is active, inspect the target for connector coverage completeness and **fail explicitly** if any connector is missing implementation where the API supports the capability.

- **Document mode:** Inject or rewrite the `## Connector Coverage` section to document which connectors are affected and what each must implement. Modifies the document directly.
- **Codebase mode:** Scan the actual source code for existing connector implementations, compare them for feature parity, and produce an audit report listing gaps, stubs, and violations. Does not modify source code automatically — reports findings first.

---

## Preconditions

Before executing, read the following context files:

- `.agents/guardrails/coding-standards.md` — Full Connector Implementation Required section
- `.agents/guardrails/migration-rules.md` — Connector-specific rules (AzureDevOps, TFS, Simulated)
- `docs/source-types.md` — Source type capabilities and constraints
- `docs/migration-agent.md` — TFS Migration Agent specification (for TFS capability assessment)
- `.agents/guardrails/system-architecture.md` — Module isolation and abstraction rules

These files establish the connector architecture and capability boundaries.

---

## The Three Connectors

| Connector | Runtime | API Surface | Notes |
|---|---|---|---|
| **Simulated** | .NET 10 | In-process, deterministic, no external connectivity | Used for testing and development. MUST implement the same `IModule` abstraction. |
| **AzureDevOpsServices** | .NET 10 | Azure DevOps REST API | Access token or service principal auth. Pinned API version. |
| **TeamFoundationServer** | .NET 4.8 agent process | TFS Object Model (`WorkItemStore`, etc.) | `IModule` dispatch in `TfsMigrationAgent`. Export only for now; Import returns `Task.CompletedTask` until implemented. |

### TFS Exemption Rule

TFS is exempt from implementing a capability **only** when the TFS Object Model API does not expose the required functionality (e.g., a REST-only feature with no SOAP equivalent). When exempt:

- The TFS code MUST emit a `ProgressEvent` with `EventKind.Warning` explaining the unsupported capability.
- The TFS code MUST log a structured warning at `Warning` level.
- The TFS code MUST NOT throw `NotImplementedException` — it gracefully skips the operation.
- The exemption MUST be documented in the spec's Connector Coverage section with a specific rationale.

---

## Execution Steps

Execute the following steps in order. Do not skip steps. Do not proceed past a FAIL gate.

### Step 0 — Detect Mode

1. Examine the target path or context.
2. If the target is a `.md` file, or the skill was invoked by a SpecKit hook → enter **Document mode**.
3. If the target is a `.cs` file, `.csproj` file, folder, or the user named a namespace/feature → enter **Codebase mode**.
4. Record the mode. All subsequent steps are executed in both modes unless marked otherwise.

### Step 1 — Extract Features

**Document mode:** Scan the entire document and extract every feature or capability that the spec introduces or modifies.

**Codebase mode:** Scan the source code within scope and extract features by identifying:

- Classes implementing `IModule` (export, import, validate entry points)
- Classes implementing `IWorkItemRevisionSource`, `IAttachmentBinarySource`, or similar source/target abstractions
- Command handlers that dispatch to connector-specific code paths
- DI registration methods that wire up connector implementations (`AddSimulated*`, `AddAzureDevOps*`, `AddTfs*`)
- Any interface with multiple implementations selected by `source.type` or `target.type`

A feature is any of:

- Export capability (e.g., export work items, export attachments, export links)
- Import capability (e.g., import work items, create attachments, map identities)
- Discovery/inventory capability (e.g., count work items, enumerate projects)
- Validation capability (e.g., validate schema, check field mappings)
- Tool/transform capability (e.g., field transforms, node structure mapping)

For each feature, record:

| Field | Value |
|---|---|
| **Name** | A short, unambiguous identifier (e.g. `workitems.export`, `attachments.download`) |
| **Type** | One of: `export`, `import`, `discovery`, `validation`, `tool` |
| **Abstraction** | The interface or base class that defines the capability |
| **Affects connectors** | Which connectors this feature applies to (typically all three) |

**Codebase mode — additional extraction:**

For each feature found in code, also record:

| Field | Value |
|---|---|
| **Simulated impl** | Class name + file path, or `Missing` |
| **AzureDevOps impl** | Class name + file path, or `Missing` |
| **TFS impl** | Class name + file path, `Missing`, or `Exempt: <reason>` |
| **Has stubs?** | Whether any implementation contains `NotImplementedException`, `NotSupportedException("not yet")`, `return default`, or `TODO` |

**FAIL GATE:** If zero features can be extracted or inferred, emit:

```
CONNECTOR COVERAGE CHECK FAILURE: No connector-relevant features could be determined.
The target must contain at least one feature that interacts with source or target systems.
If the feature is purely infrastructure or cross-cutting (no connector interaction), this
check does not apply — state this explicitly and exit with PASS (N/A).
```

### Step 2 — Determine Connector Applicability

For each feature, determine which connectors MUST implement it:

1. **Simulated** — Always required. The simulated connector MUST implement every feature for testing.
2. **AzureDevOpsServices** — Required unless the feature is TFS-only (which should never happen in practice).
3. **TeamFoundationServer** — Required unless the TFS Object Model API does not support the capability. Check `docs/migration-agent.md#tfs-migration-agent` and `docs/source-types.md` for known limitations.

For each connector-feature pair, assign one of:

| Status | Meaning |
|---|---|
| **Required** | The connector MUST implement this feature |
| **Exempt** | The connector's API does not support this capability (TFS only — with documented rationale) |
| **N/A** | The feature does not apply to this connector type (e.g., import-only features don't apply to export-only connectors) |

### Step 3 — Check Coverage

**Document mode:** For each feature, verify that the spec includes:

1. **Acceptance scenarios** — At least one scenario per connector where the status is `Required`. Scenarios may be parameterised (e.g., `Scenario Outline` with connector type as a parameter) or individual per connector.
2. **Requirements** — Functional requirements that explicitly mention connector-specific behaviour where it differs.
3. **Edge cases** — Connector-specific edge cases (e.g., TFS rate limits differ from ADO REST, Simulated generates deterministic data).

**Codebase mode:** For each feature, verify that:

1. An implementation class exists for each `Required` connector.
2. The implementation is **real** — not a stub, placeholder, or `NotImplementedException`.
3. The implementation handles the feature's full scope (all fields, all edge cases).
4. DI registration wires up the implementation for the correct `source.type` / `target.type`.
5. Tests exist that exercise each connector's implementation.

### Step 4 — Identify Gaps

For each feature-connector pair where coverage is incomplete, record:

| Feature | Connector | Status | Gap Detail |
|---|---|---|---|
| `<feature>` | `<connector>` | `Missing` / `Stub` / `Partial` | What is missing or incomplete |

**Gap types:**

- **Missing** — No implementation class exists for this connector.
- **Stub** — Implementation exists but contains `NotImplementedException`, `NotSupportedException("not yet")`, `return default`, `TODO`, or equivalent placeholder.
- **Partial** — Implementation exists but does not cover the full feature scope (e.g., handles basic fields but skips attachments or links).
- **No scenarios** — (Document mode) No acceptance scenario exercises this connector for this feature.
- **No tests** — (Codebase mode) No test exercises this connector's implementation.

### Step 5 — Generate Report

**Document mode:** Inject or rewrite the `## Connector Coverage` section in the document.

**Codebase mode:** Produce the Connector Coverage Audit Report.

**FAIL GATE:** If any feature-connector pair has status `Missing`, `Stub`, or `No scenarios` (Document) / `No tests` (Codebase), emit:

```
CONNECTOR COVERAGE CHECK FAILURE: Incomplete coverage detected.
<N> feature-connector pair(s) have gaps. See the coverage table below.
Every feature MUST be fully implemented for all applicable connectors before done.
```

---

## Document Output Structure

The `## Connector Coverage` section MUST follow this structure:

```markdown
## Connector Coverage

### Features

| Feature | Type | Abstraction | Simulated | AzureDevOps | TFS |
|---|---|---|---|---|---|
| ... | ... | ... | Required | Required | Required/Exempt |

### Acceptance Scenario Mapping

| Feature | Connector | Scenario(s) |
|---|---|---|
| ... | Simulated | <scenario name or "MISSING"> |
| ... | AzureDevOps | <scenario name or "MISSING"> |
| ... | TFS | <scenario name, "MISSING", or "Exempt: reason"> |

### TFS Exemptions (if any)

| Feature | Reason | Graceful Behaviour |
|---|---|---|
| ... | <specific API limitation> | Emits Warning ProgressEvent, logs structured warning, skips operation |

### Gaps (if any)

| Feature | Connector | Gap | Required Action |
|---|---|---|---|
| ... | ... | ... | <what must be added to the spec> |

### Verdict

**PASS** — All features have complete connector coverage in the specification.
— or —
**FAIL** — <N> gaps found. Address the gaps listed above before proceeding to planning.
```

---

## Codebase Output Structure

```markdown
## Connector Coverage Audit Report

**Scope:** `<project, folder, or file path>`
**Date:** `<date>`
**Features found:** `<count>`

### Features Discovered

| Feature | Type | Abstraction | Simulated | AzureDevOps | TFS |
|---|---|---|---|---|---|
| ... | ... | ... | ✅ `Class` | ✅ `Class` | ✅ `Class` / ⚠️ Exempt / ❌ Missing |

Legend: ✅ = Implemented, ⚠️ = Exempt (documented), ❌ = Missing or Stub

### Implementation Detail

For each feature-connector pair:

| Feature | Connector | Class | File | Status | Issues |
|---|---|---|---|---|---|
| ... | Simulated | `SimulatedXxx` | `src/.../SimulatedXxx.cs` | ✅ Complete | — |
| ... | AzureDevOps | `AzureDevOpsXxx` | `src/.../AzureDevOpsXxx.cs` | ✅ Complete | — |
| ... | TFS | `TfsXxx` | `src/.../TfsXxx.cs` | ❌ Stub | Contains `NotImplementedException` at line N |

### Gaps

| Feature | Connector | Gap Type | Detail | Required Action |
|---|---|---|---|---|
| ... | ... | Missing/Stub/Partial/No tests | <detail> | <specific fix> |

### Required Changes

Prioritised list of changes needed:

1. **[CRITICAL]** `<file>` — Implement `<feature>` for `<connector>` (currently stub/missing)
2. **[HIGH]** ...
3. **[MEDIUM]** ...

### Verdict

**PASS** — All features have complete connector coverage in the codebase.
— or —
**FAIL** — `<N>` feature-connector pairs have incomplete coverage. See Required Changes above.
```

---

## Enforcement Summary

| Condition | Action |
|---|---|
| No `## Connector Coverage` section | Create it |
| Section exists but incomplete | Amend missing parts |
| Section exists and complete | No modification (idempotent) |
| Zero connector-relevant features | **PASS (N/A)** — state explicitly and exit |
| Feature missing Simulated impl or scenario | **FAIL** — Simulated is always required |
| Feature missing AzureDevOps impl or scenario | **FAIL** — AzureDevOps is always required |
| Feature missing TFS impl or scenario (API supports it) | **FAIL** — TFS is required unless exempt |
| TFS exempt without documented rationale | **FAIL** — exemption must state the specific API limitation |
| Implementation contains `NotImplementedException` | **FAIL** — stub is not an implementation |
| Implementation contains `throw new NotSupportedException("not yet")` | **FAIL** — deferred work is not done |
| Implementation contains `return default` where real value required | **FAIL** — silent no-op is not coverage |
| Deferred to follow-up PR or future task | **FAIL** — all connectors must be implemented together |

---

## Completion Criteria

### Both modes

- [ ] All connector-relevant features extracted from input.
- [ ] Every feature has connector applicability determined (Required / Exempt / N/A).
- [ ] Every Required feature-connector pair has coverage verified.
- [ ] TFS exemptions have specific API limitation rationale documented.
- [ ] No stubs, placeholders, or `NotImplementedException` in any Required implementation.
- [ ] No connector implementation deferred to a follow-up PR or future task.

### Document mode only

- [ ] Connector Coverage section follows the mandatory structure.
- [ ] Acceptance scenarios mapped for every Required feature-connector pair.
- [ ] Existing correct content is preserved (idempotent).
- [ ] No TODO placeholders remain in the Connector Coverage section.

### Codebase mode only

- [ ] Every feature has been checked for existing implementations across all three connectors.
- [ ] Every gap is marked with status (Missing/Stub/Partial) and file location.
- [ ] Required Changes list is prioritised (Critical > High > Medium).
- [ ] Verdict is explicitly stated (PASS or FAIL).
- [ ] No source files were modified without explicit user instruction.

The skill is not complete until all criteria are checked. Any unchecked criterion is a failure.
