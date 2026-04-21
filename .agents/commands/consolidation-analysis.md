---
name: consolidation-analysis
description: Analyse the source tree for duplicated logic, parallel abstractions, and consolidation opportunities. Outputs a structured report to /analysis/consolidation.md
intent: run
---

You are performing a **full architectural consolidation analysis** of a .NET solution.

## Output Requirements

- Output MUST be a single Markdown document.
- Write the result to: `/analysis/consolidation.md`
- Use the exact structure and section ordering defined below.
- Be precise, technical, and evidence-based.
- Do NOT speculate. If uncertain, state "uncertain".
- Do NOT summarise code you have not inspected.
- Do NOT propose abstract improvements without pointing to concrete code locations.

---

# Consolidation Analysis

## Metadata

- Date: {{current_date}}
- Scope: Full source tree under `/src`
- Purpose: Identify duplicated classes, reimplemented concepts, and refactoring opportunities to improve architectural consistency and flexibility.

---

## Framework Target Topology (MANDATORY CONTEXT)

You MUST:

1. Detect and document the actual framework targeting (`net481`, `net10.0`, multi-targeting).
2. Explicitly identify:
   - net481-only projects
   - multi-targeted projects
   - net10-only projects
3. Explain WHY the split exists based on code dependencies.

You MUST treat the following as **hard constraints**:

- TfsObjectModel COM interop MUST remain in net481-only projects.
- Shared abstractions MUST remain multi-targeted.
- No recommendation may violate this boundary.

---

## Executive Summary

Provide a **high-signal summary** that:

- Identifies the dominant architectural pattern (or intended pattern).
- Identifies where divergence exists.
- States the single highest-value consolidation action.
- States whether duplication is systemic or isolated.

---

## Findings

For EACH finding:

## Finding N — <Short Title> (<Severity: CRITICAL | HIGH | MEDIUM | LOW>)

### What it is
Describe the duplication or inconsistency.

### Where it is
List exact files and paths.

### Evidence
- Class names
- Interfaces
- Methods
- Concrete behaviour differences

### Why it matters
Explain:
- Maintenance cost
- Behaviour divergence risk
- Violations of architectural rules

### Recommended consolidation
Provide **concrete steps only**:
- Classes to create
- Classes to delete
- Interfaces to replace or reuse
- Exact migration direction

---

## What to Look For (Detection Heuristics)

You MUST actively search for:

### 1. Duplicate orchestration loops
- Multiple implementations of the same workflow
- Inline logic vs shared orchestrator

### 2. Parallel abstractions
- Two interfaces solving the same problem
- Especially single-use interfaces

### 3. Format divergence
- Same logical artefact written differently
- File formats, encodings, extensions

### 4. Retry/resilience duplication
- Polly vs custom loops vs inline retry

### 5. Strategy vs inline logic
- Strategy interfaces ignored in favour of extension methods or inline code

### 6. Progress/event fragmentation
- Multiple event models for the same concept

### 7. Boundary violations
- net481 code leaking into net10 logic or vice versa
- Missing reuse of multi-targeted abstractions

---

## Prioritised Refactoring Roadmap

Produce a table:

| Priority | Finding | Effort | Benefit |

Rules:
- CRITICAL items must appear first
- Identify cascading effects (e.g. "this removes X, Y, Z automatically")
- Be explicit about effort in days

---

## Files Affected by Full Consolidation

Group into:

### Delete
List full paths and explain why

### Create
List:
- File path
- Interface implemented
- Which project it belongs to
- Framework constraints

### Update
List files and required changes

### Verify (no changes)
List correctly implemented components

---

## Well-Enforced Patterns (No Action Required)

Identify patterns that are:

- Consistently applied
- Correctly abstracted
- Already aligned with architecture

---

## Architectural Rules (MANDATORY)

Apply these rules when analysing:

1. **No duplicate orchestration logic**
2. **No single-use abstractions**
3. **All cross-source behaviour must use shared abstractions**
4. **Multi-targeting is the sharing mechanism, not duplication**
5. **File system / artefact output must be structurally consistent**
6. **Retry logic must be intentional and context-specific**
7. **Progress reporting must converge on a canonical model**

Flag violations explicitly.

---

## Output Constraints

- Do NOT include conversational text
- Do NOT include explanations of what you are doing
- Do NOT include recommendations without evidence
- Use concise, technical language

---

## Final Check

Before completing:

- Ensure every finding has:
  - Evidence
  - File paths
  - Concrete actions
- Ensure all recommendations respect net481 constraints
- Ensure no duplication of analysis across findings