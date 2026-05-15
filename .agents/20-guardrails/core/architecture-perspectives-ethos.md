# Architecture Perspectives Ethos

Canonical non-skill enforcement ethos for architectural integrity across `.agents`.

## Scope

This ethos defines required behavior and reject conditions for:

1. Modular Monolith
2. Clean Architecture
3. Hexagonal Architecture
4. Vertical Slice Architecture
5. Screaming Architecture
6. Architecture Deepening

These checks are mandatory for all changes and are enforced through guardrails, workflow gates, and preflight.

## Perspective Enforcement Matrix

| Perspective | Required ethos | Mandatory evidence | Reject conditions |
| --- | --- | --- | --- |
| Modular Monolith | Single owner seam per concern; no duplicate concern engines across modules | Touched concerns mapped to one canonical seam/surface | Parallel runtime entry points, duplicated engines, cross-module concern drift |
| Clean Architecture | Dependency direction points toward abstractions and use-case boundaries | Touched code paths show abstraction-first dependency flow | Infrastructure/policy leakage into core concerns |
| Hexagonal Architecture | Ports/adapters discipline; external systems accessed through boundary abstractions | Connector/tool/module interactions pass through declared interfaces | Direct SDK/infrastructure coupling in domain/module orchestration |
| Vertical Slice | Slice policy in adapters/extensions; core concern logic centralized | Slice-specific behavior identified as adapter policy, not alternate engine | Slice-specific duplicate engines or orchestration sprawl |
| Screaming Architecture | Naming and structure reveal domain intent and seam ownership | Added/changed names make boundary ownership obvious | Generic/ambiguous names that hide purpose or seam ownership |
| Architecture Deepening | Every change must strengthen clarity, boundaries, or duplication posture | Explicit deepening check recorded in review evidence for the touched scope | No deepening check recorded; knowingly leaving avoidable drift in touched scope |

## Deepening Rule (Mandatory Every Change)

Each change must include a deepening assessment for touched scope with one of:

- consolidation of duplicated concern logic
- boundary tightening (single seam/surface usage)
- naming clarity improvement
- removal of accidental coupling
- explicit "no safe deepening opportunity in touched scope" statement

Missing this assessment is non-compliant.

