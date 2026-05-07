# Pipeline Phases

The platform defines five phases and a convenience mode:

| Phase | Command mode | Purpose |
|---|---|---|
| Inventory | `Inventory` | Count and catalogue source items. Produces an inventory manifest. Followed by an `analyse` sub-phase where registered `IAnalyser` implementations run (e.g. consolidating inventory counts, computing dependency graphs). |
| Export | `Export` | Read source items and write them to the package. Runs Inventory first if not already done. |
| Prepare | `Prepare` | Validate target readiness: check node structure, identity mapping, permissions. |
| Import | `Import` | Read the package and push items to the target. Runs Prepare first if not already done. |
| Validate | `Validate` | Compare source and target to verify the migration is complete. |
| Migrate | `Migrate` | Chains all five phases: Inventory → Export → Prepare → Import → Validate. |

## Phase Independence

Each phase is independent. A phase can be re-run without re-running earlier phases, as long as the package contains the output of those phases.

## Automatic Pre-Phase Behaviour

- **Export** auto-runs Inventory if the inventory manifest is absent.
- **Import** auto-runs Prepare if the prepare report is absent.

## Phase Outputs in Package

| Phase | Writes to package |
|---|---|
| Inventory | `<module>/inventory.json` |
| Export | WorkItems/, Teams/, Nodes/, Identities/ data |
| Prepare | `Identities/prepare-report.json` |
| Import | `.migration/Checkpoints/<module>-import.cursor` |
| Validate | `validation-report.json` |

## Rules

- Phases are executed by the Migration Agent or TFS Export Agent. Never by the Control Plane.
- Phase outputs are readable by operators for inspection.
- All phases are resumable via cursor-based checkpointing.

## Related

- [checkpointing-summary.md](./checkpointing-summary.md) — resume model
- [migration-package-concept.md](./migration-package-concept.md) — package structure
- [.agents/guardrails/migration-rules.md](../guardrails/migration-rules.md) — migration rules