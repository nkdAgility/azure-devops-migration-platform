# Architecture Discrepancies

**Feature**: Discovery Dependency Analysis
**Flagged by**: speckit.specify
**Status**: Pending rectification (resolve in speckit.implement)

## Discrepancies

### CLI command reference missing `discovery dependencies`
- **Source doc**: `.agents/context/cli-commands.md`
- **Section**: "3. Discovery Commands (`discovery`)" table
- **Issue**: The canonical command reference lists only `discovery inventory`. The `discovery dependencies` command added by this feature is not registered.
- **Suggested update**: Add the following row to the Discovery Commands table:

  | `discovery dependencies` | `DependencyCommandSettings` | Analyse work items for cross-project and cross-organisation links. Results written to `discovery-dependencies.csv`. |

  And add the following to the canonical example invocations block:
  ```
  devopsmigration discovery dependencies --config migration.json
  devopsmigration discovery dependencies --config migration.json --output ./reports/deps.csv
  ```
  And add to the `AddBranch("discovery", ...)` registration code example:
  ```csharp
  branch.AddCommand<DependencyCommand>("dependencies");
  ```

### `docs/cli-guide.md` does not describe `discovery dependencies`
- **Source doc**: `docs/cli-guide.md`
- **Section**: (no discovery commands section currently exists in narrative form)
- **Issue**: `docs/cli-guide.md` provides narrative context for CLI commands. The `discovery dependencies` command has no narrative description.
- **Suggested update**: Add a `### discovery dependencies` sub-section under a `## Discovery Commands` heading, describing the command's purpose, options (`--config`, `--output`), and console output format.

### `docs/capabilities-guide.md` has no Dependency section per source type
- **Source doc**: `docs/capabilities-guide.md`
- **Section**: "AzureDevOpsServices — Inventory", "TeamFoundationServer — Inventory", "Simulated — Inventory"
- **Issue**: Each source type documents how `discovery inventory` uses it. There is no equivalent `Dependency` subsection documenting how `discovery dependencies` uses each source type (REST API for ADO, subprocess delegation for TFS, synthetic generation for Simulated).
- **Suggested update**: Add a **Dependency Analysis** paragraph after each source type's Inventory section following the same pattern: describe the query mechanism, credential usage, and WIQL-scoped filtering behaviour.
