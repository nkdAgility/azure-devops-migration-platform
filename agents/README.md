# Agent Rules

This folder contains operational rules and style guides for all AI agents working in the Azure DevOps Migration Platform repository.

## Contents

| File | Purpose |
|---|---|
| [acceptance-test-format.md](acceptance-test-format.md) | Format and naming conventions for Gherkin `.feature` files |
| [testing-standards.md](testing-standards.md) | MSTest conventions, test naming, and test organisation rules |
| [atdd-workflow.md](atdd-workflow.md) | End-to-end ATDD session workflow, agent handoff sequence, and session discipline |

## Relationship to Other Guardrails

These rules supplement — but do not replace — the hard architectural guardrails in [agents/](../../agents/). When a rule in this folder conflicts with a rule in [agents/system-architecture.md](../../agents/system-architecture.md), the system architecture rule wins.

The rules here govern *how agents work*, while `agents/system-architecture.md` governs *what agents must never do*.
