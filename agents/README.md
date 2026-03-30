# Agent Rules

This folder contains operational rules and style guides for all AI agents working in the Azure DevOps Migration Platform repository.

## Contents

### Architectural Guardrails

| File | Purpose |
|---|---|
| [system-architecture.md](system-architecture.md) | Core architecture constraints that must never be violated |
| [workitems-rules.md](workitems-rules.md) | WorkItems module-specific rules (folder layout, streaming, attachments) |
| [migration-rules.md](migration-rules.md) | Migration behaviour constraints (checkpointing, identity mapping, source-to-package-to-target) |
| [aspire-integration.md](aspire-integration.md) | Microsoft Aspire orchestration rules (AppHost, service discovery, observability) |
| [coding-standards.md](coding-standards.md) | C# coding conventions, .NET runtime rules, and prohibited patterns |
| [module-template.md](module-template.md) | Requirements and checklist for creating new modules |

### Testing & ATDD Standards

| File | Purpose |
|---|---|
| [acceptance-test-format.md](acceptance-test-format.md) | Format and naming conventions for Gherkin `.feature` files |
| [testing-standards.md](testing-standards.md) | MSTest + Reqnroll conventions, test naming, and test organisation rules |
| [atdd-workflow.md](atdd-workflow.md) | End-to-end ATDD session workflow, agent handoff sequence, and session discipline |

## Relationship to Other Guardrails

These rules supplement — but do not replace — the hard architectural guardrails in this folder. When a rule in one file conflicts with another, the hierarchy is:

1. **system-architecture.md** — foundational constraints
2. **Domain-specific rules** (workitems-rules.md, migration-rules.md, aspire-integration.md)
3. **Coding standards** (coding-standards.md)
4. **Testing standards** (testing-standards.md, acceptance-test-format.md)
5. **Workflow guidance** (atdd-workflow.md)

The rules here govern *what agents must never do* and *how agents work*.
