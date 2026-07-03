# Guardrail: Spec Coverage Completeness

## Rule

Before implementing a spec that changes an interface, method, or communication
pattern, you MUST verify that the spec accounts for ALL call sites of the thing
being changed. If even one call site is not covered, **stop and ask the operator**
before writing any code.

## Why

Past incidents (e.g., migrating `IControlPlaneClient` in Phase E of the Iron
Communications plan) showed that specs written for one call site missed several
others. The agent implemented the change for the documented site and left the
remaining sites with the old pattern — creating an inconsistent codebase that
still compiled but violated the intended contract.

## How to Apply

1. When a spec says "change X" (method, interface, pattern, field), grep the
   codebase for ALL usages of X before touching any code:
   ```
   grep -r "FollowLogsAsync\|StreamDiagnosticsAsync\|GetTelemetryAsync" src/ tests/
   ```
2. Compare the grep results with the call sites listed in the spec.
3. If ANY call site is missing from the spec, surface the gap to the operator:

   > **STOP — spec coverage gap detected.**
   > The spec covers N call sites but the codebase has M. The following are
   > not covered: [list]. Should I extend the spec to cover them, or is this
   > intentional scope?

4. Do not proceed until the operator confirms the scope is complete or
   explicitly accepts the gap.

## Scope

Applies to all specs that change:
- Interface members (add, remove, rename, change signature)
- Communication patterns (e.g., replacing SSE methods, changing HTTP verbs)
- DI registrations that affect multiple commands
- Shared abstractions used across more than one project

Does NOT apply to:
- Purely additive changes (new method, new endpoint) that do not touch existing callers
- Changes scoped to a single file with no shared consumers

## Verification After Implementation

After completing an implementation, run a final grep to confirm zero remaining
uses of the old pattern. If any remain, treat them as bugs — not scope creep.
