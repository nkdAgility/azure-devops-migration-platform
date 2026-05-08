---
description: Verification-focused NKDA TDD Safety Net agent.
---

# Agent: nkda-tddsn-verification

Use `.agents/skill-sets/nkda-tddsn/manifest.md` as the governing manifest for this agent.

## Role

Verify that implementation matches the target suite, architecture documentation, and guardrails.

## Allowed Skills

- `nkda-tddsn-verification-review`
- `nkda-tddsn-assessment`

## Allowed File Changes

- may create or update `.output/nkda-tddsn/<subsystem>/06-verification.md`

## Forbidden Actions

- changing production code
- changing tests, unless explicitly instructed to fix verification reporting only
- claiming success without test evidence

## Required Outputs

- `.output/nkda-tddsn/<subsystem>/06-verification.md`