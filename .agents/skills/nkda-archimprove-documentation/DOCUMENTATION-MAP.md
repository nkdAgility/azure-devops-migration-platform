# Documentation Architecture Map

Use this as the target model when reviewing or restructuring documentation.

Do not create every file automatically. Create files when there is real content, a clear audience, and enough product maturity to justify them.

## Scope

Included:

- `.agents/20-guardrails`
- `.agents/30-context`
- `docs`

Excluded unless explicitly requested:

- `.agents/skills`
- `.github/agents`
- `.github/commands`
- `.specify`
- `features`
- `specs`

## Target Layout

```text
.
├── .agents/
│   ├── guardrails/
│   │   ├── README.md
│   │   ├── coding-standards.md
│   │   ├── testing-rules.md
│   │   ├── security-rules.md
│   │   ├── architecture-boundaries.md
│   │   ├── observability-requirements.md
│   │   ├── data-sovereignty-rules.md
│   │   ├── definition-of-done.md
│   │   ├── migration-rules.md
│   │   ├── workitems-rules.md
│   │   ├── module-rules.md
│   │   ├── connector-rules.md
│   │   ├── package-rules.md
│   │   ├── control-plane-rules.md
│   │   ├── cli-tui-rules.md
│   │   └── configuration-rules.md
│   │
│   └── context/
│       ├── README.md
│       ├── product-vision.md
│       ├── domain-model.md
│       ├── architecture-overview.md
│       ├── terminology.md
│       ├── migration-package-concept.md
│       ├── control-plane-concept.md
│       ├── telemetry-model.md
│       ├── entitlements-model.md
│       ├── decision-records-summary.md
│       ├── configuration-model.md
│       ├── connector-model.md
│       ├── job-lifecycle.md
│       ├── pipeline-phases.md
│       ├── module-model.md
│       ├── package-format-summary.md
│       ├── workitems-format-summary.md
│       ├── checkpointing-summary.md
│       └── data-classification-summary.md
│
└── docs/
    ├── README.md
    ├── getting-started.md
    ├── operator-guide.md
    ├── operator-advanced-guide.md
    ├── package-guide.md
    ├── configuration-guide.md
    ├── scenarios-guide.md
    ├── cli-guide.md
    ├── tui-guide.md
    ├── troubleshooting-guide.md
    ├── migration-process-guide.md
    ├── capabilities-guide.md
    ├── control-plane.md
    ├── agent-hosting.md
    ├── observability.md
    ├── security-and-data-sovereignty.md
    ├── contributor-guide.md
    ├── client-integration-guide.md
    ├── development-setup.md
    ├── testing-guide.md
    ├── module-development-guide.md
    ├── connector-development-guide.md
    ├── telemetry-development-guide.md
    ├── architecture.md
    ├── package-format-reference.md
    ├── configuration-reference.md
    └── adr/
        ├── README.md
        ├── 0001-source-files-target.md
        ├── 0002-filesystem-package-as-source-of-truth.md
        ├── 0003-cursor-based-checkpointing.md
        ├── 0004-control-plane-does-not-execute-migrations.md
        ├── 0005-agent-only-package-write-access.md
        ├── 0006-three-channel-observability.md
        ├── 0007-compiler-enforced-project-boundaries.md
        ├── 0008-configuration-travels-in-package.md
        ├── 0009-single-job-kind-discriminator.md
        ├── 0010-plan-driven-dag-execution.md
        ├── 0011-unified-platform-metric-namespace.md
        ├── 0012-imodule-five-phase-contract.md
        └── 0013-simulated-connector-as-ci-infrastructure.md
```

## Operator Documentation

Operator documentation should help a user run migrations safely.

Primary files:

- `docs/getting-started.md`
- `docs/operator-guide.md`
- `docs/package-guide.md`
- `docs/configuration-guide.md`
- `docs/scenarios-guide.md`
- `docs/cli-guide.md`
- `docs/tui-guide.md`
- `docs/troubleshooting-guide.md`
- `docs/migration-process-guide.md`
- `docs/capabilities-guide.md`

## Advanced Operator Documentation

Advanced operator documentation should help a user host, scale, secure, and diagnose the platform.

Primary files:

- `docs/operator-advanced-guide.md`
- `docs/control-plane.md`
- `docs/agent-hosting.md`
- `docs/observability.md`
- `docs/security-and-data-sovereignty.md`

## Contributor Documentation

Contributor documentation should help a developer change, extend, and verify the platform.

Primary files:

- `docs/contributor-guide.md`
- `docs/client-integration-guide.md`
- `docs/development-setup.md`
- `docs/testing-guide.md`
- `docs/module-development-guide.md`
- `docs/connector-development-guide.md`
- `docs/telemetry-development-guide.md`
- `docs/architecture.md`
- `docs/package-format-reference.md`
- `docs/configuration-reference.md`
- `docs/adr/`

## Agent Guardrails

Guardrails should define constraints, not explanations.

Recommended file purposes:

- `coding-standards.md`: code shape, naming, async, immutability, dependency injection, SPDX.
- `testing-rules.md`: MSTest, Reqnroll if applicable, assertion quality, no ignored tests.
- `security-rules.md`: credentials, secrets, safe logging, least privilege.
- `architecture-boundaries.md`: major system boundaries and prohibited coupling.
- `observability-requirements.md`: tracing, metrics, logs, progress, data classification.
- `data-sovereignty-rules.md`: package write access, customer data boundary, telemetry boundary.
- `definition-of-done.md`: completion checks.
- `migration-rules.md`: phase behaviour and resumability.
- `workitems-rules.md`: work item package and processing rules.
- `module-rules.md`: module isolation and contracts.
- `connector-rules.md`: connector contracts and test expectations.
- `package-rules.md`: package layout and access.
- `control-plane-rules.md`: coordination-only control plane behaviour.
- `cli-tui-rules.md`: client behaviour and telemetry display constraints.
- `configuration-rules.md`: configuration shape and schema discipline.

## Agent Context

Context should compress concepts, not replace docs.

Recommended file purposes:

- `product-vision.md`: what the platform is and is not.
- `domain-model.md`: core domain concepts.
- `architecture-overview.md`: compressed component map.
- `terminology.md`: canonical language.
- `migration-package-concept.md`: package concept.
- `control-plane-concept.md`: control plane concept.
- `telemetry-model.md`: traces, metrics, logs, progress, snapshots.
- `entitlements-model.md`: entitlement snapshot and enforcement concept.
- `decision-records-summary.md`: current ADR summary.
- `configuration-model.md`: top-level configuration model.
- `connector-model.md`: connector concept.
- `job-lifecycle.md`: job submission through completion.
- `pipeline-phases.md`: Inventory, Export, Prepare, Import, Validate, Migrate.
- `module-model.md`: module concept and responsibilities.
- `package-format-summary.md`: compressed package layout.
- `workitems-format-summary.md`: compressed work item layout.
- `checkpointing-summary.md`: cursor-based checkpointing.
- `data-classification-summary.md`: data classes and telemetry boundary.

