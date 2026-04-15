# Architecture Discrepancies

**Feature**: Azure DevOps Work Items Import
**Flagged by**: speckit.specify
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### 1. WorkItemsModule.ImportAsync is a stub
- **Source doc**: `docs/modules.md`
- **Section**: Module Responsibilities — WorkItemsModule
- **Issue**: The doc describes WorkItemsModule as handling "High-fidelity work item revision export/import" but the actual implementation currently throws `NotSupportedException` for `ImportAsync`. The spec implements the import side.
- **Suggested update**: No doc update needed — the doc already describes the intended behaviour. The code must be updated to match.

### 2. Import CLI command is hidden/stubbed
- **Source doc**: `docs/cli.md` and `.agents/context/cli-commands.md`
- **Section**: Migration Commands — `import`
- **Issue**: Both docs describe the `import` command as a first-class migration command with `--force-fresh` support. The actual implementation has `[HideFromChannel(ReleaseChannel.Preview)]` and returns a stub message. The spec requires enabling this command.
- **Suggested update**: No doc update needed — the docs already reflect the intended state. The code must be updated to match.

### 3. IWorkItemImportTarget abstraction not yet documented
- **Source doc**: `docs/work-item-iteration-pattern.md`
- **Section**: Required Interfaces
- **Issue**: The doc documents `IWorkItemRevisionSource` for export but has no corresponding `IWorkItemImportTarget` (or equivalent) for import. FR-018 in this spec requires an abstraction wrapping Azure DevOps SDK write calls. This pattern mirrors the documented export pattern but is not yet in the docs.
- **Suggested update**: Add a new section "Import Pattern: WorkItemImportOrchestrator" (or similar) to `docs/work-item-iteration-pattern.md` describing the import-side iteration and the target abstraction, mirroring the existing export documentation.

### 4. Scenario config for import not yet in docs
- **Source doc**: `docs/configuration.md`
- **Section**: Scenario Configs
- **Issue**: The scenario configs table lists export and inventory scenarios but no import scenario config (e.g. `import-ado-workitems-single-project.json`). This spec will require at least one import scenario config for testing.
- **Suggested update**: Add an import scenario row to the Scenario Configs table in `docs/configuration.md` once the scenario file is created during implementation.

### 5. launch.json entry for import not yet present
- **Source doc**: `.agents/guardrails/coding-standards.md` (reject trigger: "Adds or changes a CLI command without a corresponding `.vscode/launch.json` entry")
- **Section**: Reject triggers
- **Issue**: The `import` CLI command requires a `.vscode/launch.json` debug profile for scenario testing. This must be added during implementation.
- **Suggested update**: No doc update needed — the guardrail already requires this. Implementation must add the launch profile.
