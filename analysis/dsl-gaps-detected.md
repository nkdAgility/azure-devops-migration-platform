# DSL Migration Gaps Detected

This file is a **running engineering log** maintained automatically by the `nkda-testdsl-*` skills. Every time a `.feature` file or scenario is skipped, blocked, or fails conversion for an engineering reason, the skills append an entry here.

**Do not edit manually to resolve gaps** — fix the underlying engineering issue and re-run the migration. Mark resolved entries with a `Status: RESOLVED` line and the date.

---

## Gap Type Reference

| gap-type | Meaning |
|---|---|
| `unmatched-step` | Step definition exists in `.feature` but no matching step binding or DSL method can be found or inferred |
| `intent-unknown` | Scenario is `unwired`/`miswired` and intent cannot be safely inferred from scenario text alone |
| `parity-gap` | Converted test exists but does not cover equivalent assertions to the original scenario |
| `behaviour-conflict` | Converted test assertion contradicts observed production behaviour |
| `test-failure` | Converted test was written but fails and the failure cannot be resolved at migration time |
| `validity-gate` | Intent-derived test fails the validity gate (does not prove a real behaviour) |
| `dsl-missing-builder` | Required DSL builder or runner not yet implemented in `DevOpsMigrationPlatform.Testing` |
| `dsl-missing-assertion` | Required DSL assertion method not yet implemented |
| `infrastructure` | External infrastructure reason (e.g., test project missing, build broken) |
| `other` | Any other engineering reason — must include a specific detail |

---

## Open Gaps

Gaps surfaced during feature-to-DSL migration where a scenario's expected behaviour
cannot be confirmed against observed production code.

---

## GAP-001: IdentityMappingService — UPN and display-name matching unimplemented

**Detected during:** migration of `features/import/identities/identity-mapping-resolution.feature` (scenario 2)
**Status:** BLOCKED — scenario retained in feature file

### What the docstring promises

`src/DevOpsMigrationPlatform.Infrastructure.Agent/Identity/IdentityMappingService.cs:21`

```
/// Resolution order:
/// 1. Explicit override from Identities/mapping.json
/// 2. UPN/email matching
/// 3. Display name matching
/// 4. Configured default identity (falls back to the source identity when not set)
```

### What the implementation does

`Resolve()` (lines 69–88) implements only steps 1 and 4:

```csharp
if (_overrides.TryGetValue(sourceIdentity, out var mapped))
    return mapped;          // step 1 only

return FallbackIdentity(sourceIdentity);  // step 4 — skips 2 and 3 entirely
```

### Blocked scenario

```gherkin
Scenario: Automatic UPN match resolves identity
  Given a source identity "bob@source.com" with display name "Bob Smith"
  And the mapping.json file has no override for "bob@source.com"
  When the identity is resolved
  Then the resolved identity is "bob@target.com"
```

This outcome is impossible with the current implementation — `bob@target.com` would
never be produced unless it were an explicit mapping entry.

### Resolution options

1. **Implement UPN/email and display-name matching** — query the target tenant for
   identities matching the source UPN and auto-resolve. The docstring then becomes
   accurate and the scenario can be retired.

2. **Remove the unimplemented steps from the docstring** — if auto-matching is out of
   scope, correct the docstring to reflect two-step resolution (explicit override →
   configured default) and delete the blocked scenario.

---

## GAP-002: NodesModule — AutoCreateNodes attributed to wrong options class

**Detected during:** migration of `features/import/nodes/import-classification-tree.feature` (scenario 2)
**gap-type:** `behaviour-conflict`
**Status:** OPEN

### What the feature claims

```gherkin
Scenario: AutoCreateNodes ensures referenced paths exist on target
  And NodesModule is configured with AutoCreateNodes = true
  Then INodeEnsurer.EnsureReferencedPathsAsync is invoked
```

### What the code shows

`NodesModuleOptions` (`src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/NodesModuleOptions.cs`) has only `Enabled` and `ReplicateSourceTree`. There is no `AutoCreateNodes` property.

`AutoCreateNodes` exists on `NodeTranslationOptions` (`src/DevOpsMigrationPlatform.Abstractions/Options/NodeTranslationOptions.cs:56`) under config path `MigrationPlatform:Tools:NodeTranslation`. This controls node pre-creation before the work-item revision loop — a different concern from the classification-tree import driven by `NodesModule`.

`NodesModule.ImportAsync` never reads `AutoCreateNodes`.

### Resolution options

1. **Accept the current design** — clarify in the feature that `AutoCreateNodes` is a `NodeTranslation` tool option, not a `NodesModule` option, and rewrite the scenario to target `NodeTranslationOptions`. Delete or rewrite the blocked scenario.
2. **Add AutoCreateNodes to NodesModuleOptions** — if the intent is that NodesModule should also support an `AutoCreateNodes` mode, add the property and wire it through `NodesModule.ImportAsync`.

---

## GAP-003: NodesModule — INodeEnsurer does not exist; no skip-when-both-false guard

**Detected during:** migration of `features/import/nodes/import-classification-tree.feature` (scenario 3)
**gap-type:** `behaviour-conflict`
**Status:** OPEN

### What the feature claims

```gherkin
Scenario: Import is skipped when both ReplicateSourceTree and AutoCreateNodes are false
  Given NodesModule is configured with ReplicateSourceTree = false and AutoCreateNodes = false
  When NodesModule ImportAsync runs
  Then neither INodeEnsurer method is invoked
```

### What the code shows

1. `INodeEnsurer` does not exist anywhere in the codebase. The actual interface used is `INodesOrchestrator`.
2. `NodesModule.ImportAsync` (`src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs:240`) always calls `_orchestrator.ImportAsync(...)` when `Enabled = true`. There is no "both-false → skip" guard at the module level.

### Resolution options

1. **Add a skip guard to NodesModule.ImportAsync** — when both `ReplicateSourceTree = false` and `AutoCreateNodes = false` (if GAP-002 is resolved by adding `AutoCreateNodes` to `NodesModuleOptions`), return `Skipped` early without calling the orchestrator.
2. **Accept the current design** — if calling the orchestrator with `false` is intentional (allowing the orchestrator to decide), remove or rewrite the scenario to reflect actual observable behaviour.
