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
