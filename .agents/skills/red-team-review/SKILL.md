---
name: red-team-review
description: Challenges a feature specification with adversarial thinking to surface blind spots, wrong assumptions, missing failure modes, and unstated risks before planning begins.
---

# Skill: Red Team Review

Use this skill immediately after a specification is written (`after_specify` hook) to stress-test the spec before it flows into planning and implementation. The goal is to catch problems that are cheaper to fix in the spec than in code.

---

## Role

When this skill is active, adopt the mindset of a hostile, skeptical reviewer whose job is to find every way the specification could lead to a flawed implementation. You are not trying to be helpful — you are trying to break the spec. Report findings as a prioritised challenge list. Do **not** edit the spec automatically — report challenges first and let the author decide what to address.

---

## The Five Lenses

Apply each lens independently. A single weakness may surface in more than one lens — this is intentional.

### Lens 1 — Assumption Assault

Question every assumption the spec makes, whether stated or implied.

- What does the spec take for granted that could be wrong?
- Are there unstated preconditions (data exists, service is available, user has permissions)?
- Does the spec assume a happy-path ordering of events? What if steps happen out of order or concurrently?
- Are volume/scale assumptions stated? If not, what happens at 10× or 100× the implied scale?
- Does the spec assume a single deployment environment, timezone, locale, or character encoding?

### Lens 2 — Failure Mode Analysis

Systematically enumerate how the feature can fail.

- What happens when each external dependency is unavailable, slow, or returns unexpected data?
- What happens on partial failure (half the batch succeeds, half fails)?
- What happens if the operation is interrupted mid-way and then retried?
- Are there race conditions, deadlocks, or ordering hazards?
- What happens when storage is full, quotas are exceeded, or tokens expire?
- Does the spec define rollback or compensation for every mutating operation?

### Lens 3 — Adversarial User / Misuse

Consider how a malicious or confused user could exploit or misuse the feature.

- Can input be crafted to cause excessive resource consumption (CPU, memory, disk, network)?
- Can the feature be used to exfiltrate data it should not expose?
- Are there injection vectors (command injection, path traversal, serialisation exploits)?
- What happens if the user provides valid-but-absurd inputs (empty strings, maximum-length strings, negative numbers, Unicode edge cases)?
- Can the feature be used to deny service to other users or operations?

### Lens 4 — Specification Contradictions & Gaps

Look for internal inconsistencies and missing pieces.

- Do any two requirements contradict each other?
- Are there requirements that are untestable as written (vague, subjective, unmeasurable)?
- Does the spec define behaviour for all states of the data lifecycle (create, read, update, delete, archive)?
- Are error messages and user-facing text specified, or left to the implementer's imagination?
- Does every acceptance scenario have a matching negative/failure scenario?
- Are there implicit ordering dependencies between requirements that are not stated?

### Lens 5 — Ecosystem & Integration Risk

Assess risk from the broader system context.

- Does the feature interact correctly with existing features, or could it break them?
- Are there versioning or migration concerns if the data model changes?
- Does the feature respect existing security boundaries (authentication, authorisation, tenancy)?
- Could the feature cause cascading failures in downstream systems?
- Are there compliance, licensing, or regulatory implications not addressed?

---

## Severity Classification

| Severity | Criteria |
|---|---|
| **Critical** | Spec gap that would cause data loss, security vulnerability, or unrecoverable state if implemented as written |
| **High** | Missing failure mode or contradiction that would require significant rework to fix post-implementation |
| **Medium** | Unstated assumption or missing edge case that would surface during testing and require spec amendment |
| **Low** | Minor ambiguity or style issue that could cause confusion but is unlikely to produce incorrect behaviour |

---

## Report Format

After completing all five lenses, produce a report in the following format:

---

### Red Team Review — Challenge Report

**Date:** `<date>`
**Feature:** `<spec title>`
**Spec file:** `<path to spec.md>`

---

#### Summary Table

| Lens | Critical | High | Medium | Low |
|---|---|---|---|---|
| Assumption Assault | `n` | `n` | `n` | `n` |
| Failure Mode Analysis | `n` | `n` | `n` | `n` |
| Adversarial User / Misuse | `n` | `n` | `n` | `n` |
| Contradictions & Gaps | `n` | `n` | `n` | `n` |
| Ecosystem & Integration Risk | `n` | `n` | `n` | `n` |
| **Total** | **`n`** | **`n`** | **`n`** | **`n`** |

---

#### Critical Challenges (must address before planning)

```
[RT-C1] <short title>
  Lens:    <which lens>
  Finding: <what the spec gets wrong or omits>
  Risk:    <what goes wrong if implemented as-is>
  Suggest: <minimum spec change to mitigate>
```

#### High Challenges (should address before planning)

```
[RT-H1] <short title>
  Lens:    <which lens>
  Finding: <what the spec gets wrong or omits>
  Risk:    <what goes wrong if implemented as-is>
  Suggest: <minimum spec change to mitigate>
```

#### Medium Challenges (address during clarify or plan phase)

```
[RT-M1] <short title>
  Lens:    <which lens>
  Finding: <what the spec gets wrong or omits>
  Suggest: <minimum spec change to mitigate>
```

#### Low Challenges (backlog)

```
[RT-L1] <short title>
  Lens:    <which lens>
  Finding: <description>
```

---

#### Recommended Actions

1. List the specific spec sections that need amendment, in priority order.
2. For Critical and High challenges, state whether they block proceeding to `/speckit.plan` or `/speckit.clarify`.
3. If no Critical or High challenges were found, state that the spec is cleared for planning.

---

## Completion Criteria

- [ ] All five lenses applied independently.
- [ ] Every finding classified by severity.
- [ ] Summary table populated.
- [ ] Critical and High findings include concrete risk description and suggested spec fix.
- [ ] Recommended actions provided with clear blocking/non-blocking status.

The review is not complete until all five lenses have been applied and all sections of the report are populated.
