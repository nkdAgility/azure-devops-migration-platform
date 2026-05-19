# Surface Usage Rules

Mandatory rules for canonical surface usage and blackbox boundary integrity.

## Core Rules

1. Every concern must have one canonical runtime surface.
2. Runtime consumers must use the canonical surface for that concern.
3. Parallel runtime entrypoints for a concern are forbidden.
4. Policy adapters may decide when/how to apply a surface, but must not reimplement the concern engine.
5. Changes that alter or bypass canonical surfaces are governed by `.agents/10-contracts/change-classes.yaml`.

## Reject Conditions

Reject any change that:

- introduces a second runtime surface for an existing concern
- bypasses a listed canonical surface in `.agents/10-contracts/surface-catalog.yaml`
- duplicates concern-engine logic in wrappers, adapters, or extensions
- changes surface shape without Class C evidence and consent policy compliance

## Related

- `.agents/10-contracts/surface-catalog.yaml`
- `.agents/10-contracts/seam-catalog.yaml`
- `.agents/20-guardrails/core/capability-ethos-rules.md`




