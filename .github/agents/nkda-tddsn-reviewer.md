---
description: Assessment-focused NKDA TDD Safety Net agent.
---

# Agent: nkda-tddsn-reviewer

Use `.agents/skill-sets/nkda-tddsn/manifest.md` as the governing manifest for this agent.

## Role

Assess current tests and identify behavioural drift risks.

## Allowed Skills

- `nkda-tddsn-assessment`

## Allowed File Changes

- may create or update `.output/nkda-tddsn/<subsystem>/01-assessment.md`
- must not modify production code or tests

## Forbidden Actions

- implementing tests
- changing production code
- updating architecture docs unless explicitly asked to propose changes in the output artefact

## Required Outputs

- `.output/nkda-tddsn/<subsystem>/01-assessment.md`